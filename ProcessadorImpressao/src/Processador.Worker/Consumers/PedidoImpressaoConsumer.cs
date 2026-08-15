using System.Text;
using System.Text.Json;
using Processador.Worker.Application.Idempotency;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Constants;
using Shared.Messages;

namespace Processador.Worker.Consumers;

/// <summary>
/// Consome a fila de pedidos de impressão. Fluxo por mensagem:
/// 1. Desserializa.
/// 2. Verifica idempotência (se PedidoId já foi processado, descarta com ack).
/// 3. "Processa" (aqui, simulado com log).
/// 4. Em caso de falha: reencaminha (requeue) até um número máximo de tentativas;
///    depois disso, rejeita sem requeue — a fila está configurada com
///    dead-letter-exchange, então a mensagem cai automaticamente na DLQ.
/// </summary>
public sealed class PedidoImpressaoConsumer
{
    private const int MaxTentativas = 3;

    private readonly IModel _channel;
    private readonly IProcessedMessageStore _store;
    private readonly ILogger<PedidoImpressaoConsumer> _logger;

    public PedidoImpressaoConsumer(
        IModel channel,
        IProcessedMessageStore store,
        ILogger<PedidoImpressaoConsumer> logger)
    {
        _channel = channel;
        _store = store;
        _logger = logger;
    }

    public void IniciarConsumo()
    {
        DeclararTopologia();
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (_, evento) => await ProcessarMensagemAsync(evento);

        _channel.BasicConsume(
            queue: QueueNames.ImpressaoSolicitada,
            autoAck: false, // ack manual: só confirma depois de processar com sucesso
            consumer: consumer);
    }

    /// <summary>
    /// Declara exchange/filas que este consumer usa (fila principal, DLX e
    /// DLQ), para que o Worker funcione mesmo se iniciar antes do Api.
    /// Redeclarar com os mesmos argumentos é seguro no RabbitMQ.
    /// </summary>
    private void DeclararTopologia()
    {
        _channel.ExchangeDeclare("dlx", ExchangeType.Direct, durable: true);
        _channel.QueueDeclare(
            queue: QueueNames.ImpressaoSolicitadaDlq,
            durable: true,
            exclusive: false,
            autoDelete: false);
        _channel.QueueBind(QueueNames.ImpressaoSolicitadaDlq, "dlx", QueueNames.ImpressaoSolicitadaDlq);

        var argumentosFilaPrincipal = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "dlx" },
            { "x-dead-letter-routing-key", QueueNames.ImpressaoSolicitadaDlq }
        };

        _channel.QueueDeclare(
            queue: QueueNames.ImpressaoSolicitada,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: argumentosFilaPrincipal);
    }

    private async Task ProcessarMensagemAsync(BasicDeliverEventArgs evento)
    {
        var json = Encoding.UTF8.GetString(evento.Body.ToArray());
        var pedido = JsonSerializer.Deserialize<PedidoImpressao>(json);

        if (pedido is null)
        {
            _logger.LogWarning("Mensagem não pôde ser desserializada, descartando.");
            _channel.BasicNack(evento.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (await _store.JaProcessadoAsync(pedido.PedidoId))
        {
            _logger.LogInformation(
                "Pedido {PedidoId} já foi processado anteriormente, ignorando duplicata.",
                pedido.PedidoId);
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            Imprimir(pedido);

            await _store.MarcarComoProcessadoAsync(pedido.PedidoId);
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            var tentativas = LerTentativasAnteriores(evento) + 1;
            _logger.LogWarning(
                ex,
                "Falha ao processar pedido {PedidoId} (tentativa {Tentativas}/{MaxTentativas}).",
                pedido.PedidoId, tentativas, MaxTentativas);

            if (tentativas >= MaxTentativas)
            {
                _logger.LogWarning(
                    "Pedido {PedidoId} esgotou tentativas, enviando para a DLQ.", pedido.PedidoId);
                PublicarNaDlq(evento.Body.ToArray());
            }
            else
            {
                Republicar(evento.Body.ToArray(), tentativas);
            }

            // A mensagem original já foi tratada (reencaminhada ou enviada à
            // DLQ manualmente), então confirmamos para removê-la da fila.
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
        }
    }

    /// <summary>
    /// Republica a mensagem na mesma fila, incrementando o header de
    /// contagem de tentativas. Fazemos isso manualmente (em vez de usar
    /// requeue nativo do RabbitMQ) porque o requeue simples não registra
    /// quantas vezes a mensagem já falhou.
    /// </summary>
    private void Republicar(byte[] body, int tentativas)
    {
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = new Dictionary<string, object> { { "x-retry-count", tentativas } };

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: QueueNames.ImpressaoSolicitada,
            basicProperties: properties,
            body: body);
    }

    private void PublicarNaDlq(byte[] body)
    {
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: QueueNames.ImpressaoSolicitadaDlq,
            basicProperties: properties,
            body: body);
    }

    /// <summary>
    /// Simula o processamento. Lança exceção de propósito para NomeArquivo
    /// "falhar.pdf", útil para testar retry e DLQ manualmente.
    /// </summary>
    private void Imprimir(PedidoImpressao pedido)
    {
        if (pedido.NomeArquivo.Equals("falhar.pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Falha simulada de impressão.");
        }

        _logger.LogInformation(
            "Imprimindo {Copias} cópia(s) de {NomeArquivo} (Pedido {PedidoId}).",
            pedido.Copias, pedido.NomeArquivo, pedido.PedidoId);
    }

    /// <summary>
    /// Lê quantas vezes a mensagem já foi reencaminhada, a partir do header
    /// "x-retry-count" que nós mesmos adicionamos em Republicar(). Mensagens
    /// que chegam pela primeira vez não têm esse header (retorna 0).
    /// </summary>
    private static int LerTentativasAnteriores(BasicDeliverEventArgs evento)
    {
        if (evento.BasicProperties?.Headers is { } headers &&
            headers.TryGetValue("x-retry-count", out var valor) &&
            valor is int tentativas)
        {
            return tentativas;
        }

        return 0;
    }
}