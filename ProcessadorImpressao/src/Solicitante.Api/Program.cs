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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(rabbitMq);
builder.Services.AddSingleton<IPedidoPublisher, RabbitMqPublisher>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// Fail-fast: força a criação da conexão com o broker já no startup, para a
// API não subir e só descobrir o RabbitMQ fora no primeiro POST /pedidos.
app.Services.GetRequiredService<IPedidoPublisher>();

app.Run();
