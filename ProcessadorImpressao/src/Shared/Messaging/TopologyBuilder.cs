using RabbitMQ.Client;
using Shared.Constants;

namespace Shared.Messaging;

/// <summary>
/// Declara a topologia (fila principal com DLX, exchange dlx e DLQ) em um
/// canal. Usado tanto pela API quanto pelo Worker, para que a topologia não
/// fique duplicada nos dois projetos. Redeclarar com os mesmos argumentos é
/// seguro no RabbitMQ.
/// </summary>
public sealed class TopologyBuilder
{
    private readonly IModel _channel;

    public TopologyBuilder(IModel channel)
    {
        _channel = channel;
    }

    public void Declarar()
    {
        // Exchange e fila da DLQ primeiro, para poder referenciá-las na fila principal.
        _channel.ExchangeDeclare(QueueNames.DlxExchange, ExchangeType.Direct, durable: true);
        _channel.QueueDeclare(
            queue: QueueNames.ImpressaoSolicitadaDlq,
            durable: true,
            exclusive: false,
            autoDelete: false);
        _channel.QueueBind(
            QueueNames.ImpressaoSolicitadaDlq,
            QueueNames.DlxExchange,
            QueueNames.ImpressaoSolicitadaDlq);

        var argumentosFilaPrincipal = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", QueueNames.DlxExchange },
            { "x-dead-letter-routing-key", QueueNames.ImpressaoSolicitadaDlq }
        };

        _channel.QueueDeclare(
            queue: QueueNames.ImpressaoSolicitada,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: argumentosFilaPrincipal);
    }
}
