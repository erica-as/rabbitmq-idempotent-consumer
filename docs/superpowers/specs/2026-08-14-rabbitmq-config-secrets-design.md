# Configuração do RabbitMQ sem segredos no repositório público

## Objetivo

Remover credenciais e fallbacks hardcoded de conexão RabbitMQ do código e dos
arquivos versionados. O repositório é público, então **nada secreto pode ser
commitado**.

## Decisões de design

- **Config != segredo**: `HostName` (localhost) é config não-secreta e fica no
  `appsettings.json` versionado. `UserName`/`Password` são segredos e ficam em
  user-secrets (fora do repo), no padrão oficial do .NET para desenvolvimento
  local.
- **Fail-fast**: se não houver configuração, a aplicação falha ao iniciar com
  mensagem clara apontando o comando `dotnet user-secrets set` a executar.
  Nunca roda com valores errados em silêncio.
- **Sem fallback em código**: remove os `?? "localhost"` / `?? "guest"` do
  Worker; os valores vêm 100% da cascata de configuração.

## Arquivos afetados

| Arquivo | Mudança |
|---|---|
| `src/Shared/Configuration/RabbitMqOptions.cs` | **novo**: classe `RabbitMqOptions` (HostName, UserName, Password) + `const string SectionName = "RabbitMq"` |
| `src/Processador.Worker/Program.cs` | lê seção tipada; valida credenciais com erro claro; remove fallbacks hardcoded |
| `src/Solicitante.Api/Program.cs` | lê seção tipada, registra `RabbitMqOptions` no DI com validação na startup |
| `src/Solicitante.Api/Messaging/RabbitMqPublisher.cs` | construtor recebe `RabbitMqOptions` (não mais `IConfiguration`) |
| `src/Solicitante.Api/Solicitante.Api.csproj` | adiciona `<UserSecretsId>` (Worker já possui) |
| `src/Processador.Worker/appsettings.json` | mantém apenas `RabbitMq.HostName = localhost`; remove UserName/Password |
| `src/Solicitante.Api/appsettings.json` | adiciona seção `RabbitMq` apenas com `HostName = localhost` |

## Fluxo de dados

1. Startup: `builder.Configuration.GetSection("RabbitMq").Get<RabbitMqOptions>()`.
   A cascata do .NET já mescla: `appsettings.json` (host) → `appsettings.{Env}.json`
   → user-secrets (credenciais, em Development) → variáveis de ambiente.
2. `HostName` ausente durante geração do objeto: permanece default `localhost`
   (não-secreto, default sensato).

   Observação: o `RabbitMqOptions` é instanciado por binding de configuração;
   `HostName` terá `localhost` vindo do `appsettings.json` (existe nos dois
   projetos), portanto o default da classe é apenas proteção extra.
3. `UserName` ou `Password` ausentes/vazios → exceção na inicialização.

## Tratamento de erros

- Mensagem de erro em português, apontando exatamente o que configurar:
  `dotnet user-secrets set RabbitMq:UserName guest` (idem `Password`) no diretório
  do projeto que falhou.

## Setup local (uma vez por máquina)

```bash
dotnet user-secrets set RabbitMq:UserName guest   # em src/Processador.Worker
dotnet user-secrets set RabbitMq:Password guest
dotnet user-secrets set RabbitMq:UserName guest   # em src/Solicitante.Api
dotnet user-secrets set RabbitMq:Password guest
```

Os valores ficam em `~/.microsoft/usersecrets/`, nunca no git.

## Verificação

Sem framework de testes no projeto; verificação manual:
1. `dotnet build` — 0 erros/avisos.
2. Rodar sem user-secrets (limpar `secrets.json`) → erro claro na inicialização.
3. Rodar com user-secrets → validação passa (falha de conexão só se o broker
   estiver fora do ar, com port 5672 inacessível).

## Escopo

Somente refatoração de configuração. Não altera filas, topologia, idempotência
ou DLQ. Não altera o docker-compose (RabbitMQ default guest/guest continua
válido para dev).