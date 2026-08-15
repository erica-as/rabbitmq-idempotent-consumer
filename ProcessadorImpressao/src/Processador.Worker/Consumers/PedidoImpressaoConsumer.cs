using Processador.Worker.Application.Idempotency;
using Processador.Worker.Application.Impressao;
using Processador.Worker.Application.Retry;
using Processador.Worker.Application.Serialization;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Configuration;
using Shared.Constants;
using Shared.Messaging;
using Shared.Messages;

namespace Processador.Worker.Consumers;

/// <summary>
/// Consome a fila de pedidos de impressão. Fluxo por mensagem:
/// 1. Desserializa (via PedidoMessageSerializer).
/// 2. Reserva o PedidoId atomicamente (via IProcessedMessageStore); se já
///    estava reservado, descarta com ack (duplicata).
/// 3. Processa (via IImpressaoService) e confirma com ack.
/// 4. Em caso de falha: libera a reserva e reencaminha (republish com
///    x-retry-count) até o máximo de tentativas (via RetryPolicy); depois,
///    publica manualmente na DLQ. Em ambos os casos faz ack da mensagem
///    original, que já foi tratada.
/// </summary>
public sealed class PedidoImpressaoConsumer
{
    private readonly IModel _channel;
    private readonly IProcessedMessageStore _store;
    private readonly IImpressaoService _impressao;
    private readonly RetryPolicy _retryPolicy;
    private readonly ILogger<PedidoImpressaoConsumer> _logger;

    private string? _consumerTag;

    public PedidoImpressaoConsumer(
        IModel channel,
        IProcessedMessageStore store,
        IImpressaoService impressao,
        RetryPolicy retryPolicy,
        WorkerOptions options,
        ILogger<PedidoImpressaoConsumer> logger)
    {
        _channel = channel;
        _store = store;
        _impressao = impressao;
        _retryPolicy = retryPolicy;
        _logger = logger;

        _channel.BasicQos(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false);
    }

    public void IniciarConsumo()
    {
        DeclararTopologia();
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (_, evento) => await TratarEventoAsync(evento);

        _consumerTag = _channel.BasicConsume(
            queue: QueueNames.ImpressaoSolicitada,
            autoAck: false, // ack manual: só confirma depois de processar com sucesso
            consumer: consumer);

        _logger.LogInformation(
            "Consumo iniciado na fila {Fila} (autoAck desativado).", QueueNames.ImpressaoSolicitada);
    }

    public void PararConsumo()
    {
        if (_consumerTag is null)
        {
            return;
        }

        _channel.BasicCancel(_consumerTag);
        _consumerTag = null;
        _logger.LogInformation("Consumo cancelado na fila {Fila}.", QueueNames.ImpressaoSolicitada);
    }

    private void DeclararTopologia()
    {
        new TopologyBuilder(_channel).Declarar();

        _logger.LogInformation(
            "Topologia declarada: fila {Fila}, DLQ {Dlq} e exchange {Exchange}.",
            QueueNames.ImpressaoSolicitada, QueueNames.ImpressaoSolicitadaDlq, QueueNames.DlxExchange);
    }

    /// <summary>
    /// Rede de segurança: qualquer exceção fora do fluxo esperado (ex.:
    /// desserialização, store) derruba a mensagem para a DLQ via nack sem
    /// requeue — a fila tem dead-letter-exchange configurada.
    /// </summary>
    private async Task TratarEventoAsync(BasicDeliverEventArgs evento)
    {
        try
        {
            await ProcessarMensagemAsync(evento);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro inesperado ao processar mensagem (deliveryTag {DeliveryTag}), enviando para a DLQ.",
                evento.DeliveryTag);
            _channel.BasicNack(evento.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ProcessarMensagemAsync(BasicDeliverEventArgs evento)
    {
        var pedido = PedidoMessageSerializer.Desserializar(evento.Body.ToArray());

        _logger.LogInformation(
            "Mensagem recebida (deliveryTag {DeliveryTag}).", evento.DeliveryTag);

        if (pedido is null)
        {
            _logger.LogWarning("Mensagem não pôde ser desserializada, descartando.");
            _channel.BasicNack(evento.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (!await _store.TentarMarcarComoProcessadoAsync(pedido.PedidoId))
        {
            _logger.LogInformation(
                "Pedido {PedidoId} já foi processado anteriormente, ignorando duplicata.",
                pedido.PedidoId);
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            await _impressao.ProcessarAsync(pedido);
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            await _store.LiberarMarcacaoAsync(pedido.PedidoId);

            var tentativas = RetryPolicy.LerTentativasAnteriores(evento.BasicProperties) + 1;
            _logger.LogWarning(
                ex,
                "Falha ao processar pedido {PedidoId} (tentativa {Tentativas}/{MaxTentativas}).",
                pedido.PedidoId, tentativas, _retryPolicy.MaxTentativas);

            if (_retryPolicy.TentativasEsgotadas(tentativas))
            {
                _logger.LogWarning(
                    "Pedido {PedidoId} esgotou tentativas, enviando para a DLQ.", pedido.PedidoId);
                PublicarNaDlq(evento);
            }
            else
            {
                Republicar(evento, tentativas);
            }

            // A mensagem original já foi tratada (republicada ou enviada à
            // DLQ manualmente), então confirmamos para removê-la da fila.
            _channel.BasicAck(evento.DeliveryTag, multiple: false);
        }
    }

    /// <summary>
    /// Republica a mensagem na mesma fila, incrementando o header de contagem
    /// de tentativas. Fazemos isso manualmente (em vez de usar requeue nativo
    /// do RabbitMQ) porque o requeue simples não registra quantas vezes a
    /// mensagem já falhou.
    /// </summary>
    private void Republicar(BasicDeliverEventArgs evento, int tentativas)
    {
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        var headers = CopiarHeaders(evento.BasicProperties);
        headers[RetryPolicy.HeaderRetentativa] = tentativas;
        properties.Headers = headers;

        _logger.LogWarning(
            "Republicando mensagem na fila {Fila} ({Header} = {Tentativas}/{MaxTentativas}).",
            QueueNames.ImpressaoSolicitada, RetryPolicy.HeaderRetentativa,
            tentativas, _retryPolicy.MaxTentativas);

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: QueueNames.ImpressaoSolicitada,
            basicProperties: properties,
            body: evento.Body.ToArray());
    }

    private void PublicarNaDlq(BasicDeliverEventArgs evento)
    {
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Headers = CopiarHeaders(evento.BasicProperties);

        _logger.LogWarning("Publicando mensagem na DLQ {Dlq}.", QueueNames.ImpressaoSolicitadaDlq);

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: QueueNames.ImpressaoSolicitadaDlq,
            basicProperties: properties,
            body: evento.Body.ToArray());
    }

    private static Dictionary<string, object> CopiarHeaders(IBasicProperties? origem) =>
        origem?.Headers is { } headers
            ? new Dictionary<string, object>(headers)
            : new Dictionary<string, object>();
}
