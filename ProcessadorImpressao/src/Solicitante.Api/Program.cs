using Shared.Configuration;
using Solicitante.Api.Messaging;
using Solicitante.Api.Messaging.Interface;

var builder = WebApplication.CreateBuilder(args);

var rabbitMq = builder.Configuration
    .GetSection(RabbitMqOptions.SectionName)
    .Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();

rabbitMq.Validar();

builder.Services.AddControllers();
builder.Services.AddSingleton(rabbitMq);
builder.Services.AddSingleton<IPedidoPublisher, RabbitMqPublisher>();

var app = builder.Build();

app.MapControllers();

app.Run();
