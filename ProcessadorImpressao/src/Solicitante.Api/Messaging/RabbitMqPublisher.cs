using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.Configuration;
using Shared.Constants;
using Shared.Messaging;
using Shared.Messages;
using Solicitante.Api.Messaging.Interface;

namespace Solicitante.Api.Messaging;

/// <summary>
/// Publica pedidos de impressão na fila principal com publisher confirms
/// (<see cref="IModel.ConfirmSelect"/> + <see cref="IModel.WaitForConfirmsAsync"/>):
/// o método só retorna quando o broker confirmou a mensagem, então falhas
/// reais de roteamento/publicação são detectadas — e não engolidas.
/// </summary>
public sealed class RabbitMqPublisher : IPedidoPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(RabbitMqOptions options, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            UserName = options.UserName,
            Password = options.Password
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ConfirmSelect();

        _logger.LogInformation("Conectado ao RabbitMQ em {HostName}.", options.HostName);

        new TopologyBuilder(_channel).Declarar();
    }

    /// <summary>
    /// RabbitMQ.Client 6.x não expõe WaitForConfirmsAsync; a espera síncrona
    /// com timeout é o equivalente: lança se o broker não confirmar.
    /// </summary>
    public Task PublicarAsync(PedidoImpressao pedido)
    {
        var json = JsonSerializer.Serialize(pedido);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true; // sobrevive a restart do broker

        _logger.LogInformation(
            "Publicando pedido {PedidoId} na fila {Fila}.", pedido.PedidoId, QueueNames.ImpressaoSolicitada);

        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: QueueNames.ImpressaoSolicitada,
            basicProperties: properties,
            body: body);

        if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException(
                $"RabbitMQ não confirmou a publicação do pedido {pedido.PedidoId}.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
