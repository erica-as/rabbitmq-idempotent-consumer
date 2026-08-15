# rabbitmq-idempotent-consumer

Serviço .NET que consome mensagens do RabbitMQ com garantia de idempotência, retry e Dead Letter Queue (DLQ).

## Arquitetura

| Projeto | Descrição |
|---|---|
| `Solicitante.Api` | API REST (`POST /pedidos`) que publica pedidos de impressão na fila |
| `Processador.Worker` | Consumer que consome a fila, valida idempotência, processa, faz retry e descarta para a DLQ |
| `Shared` | Contratos compartilhados: `PedidoImpressao`, `RabbitMqOptions` e `QueueNames` |

## Tecnologias necessárias

- .NET SDK **10.0** (projetos usam `net10.0`)
- RabbitMQ 3.x (local ou Docker)
- (Opcional) Docker, para rodar o RabbitMQ

## Subindo o RabbitMQ (Docker)

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest \
  rabbitmq:management
```

- Conexão AMQP: `localhost:5672` 
- Management UI: `http://localhost:15672`

## Como executar

Pré-requisito: RabbitMQ rodando e credenciais configuradas (seção anterior).

### 1. API (publica pedidos)

```bash
cd src/Solicitante.Api
dotnet run
```

- Swagger UI: `http://localhost:5000/swagger`
- Endpoint: `POST http://localhost:5000/pedidos`

### 2. Worker (consome a fila)

Em outro terminal:

```bash
cd src/Processador.Worker
dotnet run
```

O Worker declara a topologia (fila principal com DLX configurada e a DLQ) na inicialização, não depende da API ter subido antes. Logs de consumo, idempotência, retry e DLQ aparecem no console.

## Filas

| Fila | Uso |
|---|---|
| `impressao.solicitada` | Fila principal, durável, com `x-dead-letter-exchange=dlx` |
| `impressao.solicitada.dlq` | Dead Letter Queue, durável |
| Exchange `dlx` (direct, durável) | Roteia mensagens mortas para a DLQ |

## Payloads de teste (body do `POST /pedidos`)

O `pedidoId` é opcional, se ausente, um GUID novo é gerado no servidor.

**Cenário 1 — Happy path (PedidoId gerado no servidor):**

```json
{
  "nomeArquivo": "relatorio.pdf",
  "copias": 2
}
```

**Cenário 2 — Idempotência (executar 2x com o mesmo `pedidoId`):**

```json
{
  "nomeArquivo": "contrato.pdf",
  "copias": 1,
  "pedidoId": "11111111-1111-1111-1111-111111111111"
}
```

**Cenário 3 — Sem pedidoId (executar 2x, GUIDs diferentes):**

```json
{
  "nomeArquivo": "anexo.pdf",
  "copias": 1
}
```

**Cenário 4 — `falhar.pdf` (3 retries + DLQ):**

```json
{
  "nomeArquivo": "falhar.pdf",
  "copias": 1,
  "pedidoId": "22222222-2222-2222-2222-222222222222"
}
```

Resposta esperada em todos os cenários: `202 Accepted` com `{"pedidoId": "<guid>"}`.

## Cenários de teste e comportamento esperado

### 1. Happy path
Envie o cenário 1 → Worker loga `Imprimindo 2 cópia(s) de relatorio.pdf (Pedido <guid>)` e dá ack; a mensagem sai da fila.

### 2. Publicar 2x com o mesmo `pedidoId`
1ª mensagem é processada; a 2ª é descartada com ack — Worker loga `já foi processado anteriormente, ignorando duplicata`.

### 3. Mensagem inválida (JSON malformado publicado direto na fila)
Nack sem requeue; como a fila tem DLX, a mensagem cai automaticamente na DLQ.