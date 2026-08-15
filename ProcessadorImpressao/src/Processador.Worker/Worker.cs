using Processador.Worker.Consumers;
using Shared.Constants;

namespace Processador.Worker;

/// <summary>
/// Hosts o ciclo de vida do consumo: inicia o consumer quando o host sobe e
/// cancela o consumo no desligamento gracioso.
/// </summary>
public class Worker(
    PedidoImpressaoConsumer consumer,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.IniciarConsumo();
        logger.LogInformation(
            "Worker iniciado, consumindo a fila {Fila}.", QueueNames.ImpressaoSolicitada);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            consumer.PararConsumo();
            logger.LogInformation("Worker parado, consumo cancelado.");
        }
    }
}
