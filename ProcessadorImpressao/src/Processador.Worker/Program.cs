using Processador.Worker;
using Processador.Worker.Application.Idempotency;
using Processador.Worker.Application.Impressao;
using Processador.Worker.Application.Retry;
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

var workerOptions = builder.Configuration
    .GetSection(WorkerOptions.SectionName)
    .Get<WorkerOptions>()
    ?? new WorkerOptions();

var factory = new ConnectionFactory
{
    HostName = rabbitMq.HostName,
    UserName = rabbitMq.UserName,
    Password = rabbitMq.Password
};

builder.Services.AddSingleton(rabbitMq);
builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton<RetryPolicy>(_ => new RetryPolicy(workerOptions.MaxTentativas));
builder.Services.AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>();
builder.Services.AddSingleton<IImpressaoService, ImpressaoService>();
builder.Services.AddSingleton(_ => factory.CreateConnection());
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnection>().CreateModel());
builder.Services.AddSingleton<PedidoImpressaoConsumer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();
