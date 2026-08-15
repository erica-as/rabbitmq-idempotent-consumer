using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.Configuration;
using Shared.Constants;
using Shared.Messages;
using Solicitante.Api.Messaging.Interface;
 
namespace Solicitante.Api.Messaging;
 
/// <summary>
/// Publica pedidos de impressão na fila principal. A fila é declarada como
/// durável e com dead-letter-exchange configurada, para que mensagens
/// rejeitadas pelo consumer sejam automaticamente roteadas para a DLQ.
/// </summary>
public sealed class RabbitMqPublisher : IPedidoPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;
 
    public RabbitMqPublisher(RabbitMqOptions options, ILogger<RabbitMqPublisher> logger)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            UserName = options.UserName,
            Password = options.Password
        };
 
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _logger = logger;
 
        _logger.LogInformation("Conectado ao RabbitMQ em {HostName}.", options.HostName);
        DeclararTopologia();
    }
 
    private void DeclararTopologia()
    {
        // Exchange e fila da DLQ primeiro, para poder referenciá-las na fila principal.
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
 
public void Publicar(PedidoImpressao pedido)
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
    }
 
    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}

