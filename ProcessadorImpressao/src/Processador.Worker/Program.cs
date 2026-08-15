using Processador.Worker;
using Processador.Worker.Application.Idempotency;
using Processador.Worker.Consumers;
using Processador.Worker.Infrastructure.Idempotency;
using RabbitMQ.Client;
using Shared.Configuration;

var builder = Host.CreateApplicationBuilder(args);

var rabbitMq = builder.Configuration
    .GetSection(RabbitMqOptions.SectionName)
    .Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();

rabbitMq.Validar();

var factory = new ConnectionFactory
{
    HostName = rabbitMq.HostName,
    UserName = rabbitMq.UserName,
    Password = rabbitMq.Password
};

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>();
builder.Services.AddSingleton(_ => factory.CreateConnection());
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnection>().CreateModel());
builder.Services.AddSingleton<PedidoImpressaoConsumer>(sp => new PedidoImpressaoConsumer(
    sp.GetRequiredService<IModel>(),
    sp.GetRequiredService<IProcessedMessageStore>(),
    sp.GetRequiredService<ILogger<PedidoImpressaoConsumer>>()));

var host = builder.Build();

host.Services.GetRequiredService<PedidoImpressaoConsumer>().IniciarConsumo();

host.Run();
