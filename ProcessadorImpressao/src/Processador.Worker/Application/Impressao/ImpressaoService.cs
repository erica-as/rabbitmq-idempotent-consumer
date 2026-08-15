using Shared.Configuration;
using Shared.Messages;

namespace Processador.Worker.Application.Impressao;

/// <summary>
/// Simula a impressão de um pedido (aqui, apenas loga). Lança exceção de
/// propósito quando o nome do arquivo é o configurado em
/// <see cref="WorkerOptions.ArquivoParaFalhar"/>, útil para testar retry e
/// DLQ manualmente.
/// </summary>
public sealed class ImpressaoService : IImpressaoService
{
    private readonly WorkerOptions _options;
    private readonly ILogger<ImpressaoService> _logger;

    public ImpressaoService(WorkerOptions options, ILogger<ImpressaoService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task ProcessarAsync(PedidoImpressao pedido)
    {
        if (pedido.NomeArquivo.Equals(_options.ArquivoParaFalhar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Falha simulada de impressão.");
        }

        _logger.LogInformation(
            "Imprimindo {Copias} cópia(s) de {NomeArquivo} (Pedido {PedidoId}).",
            pedido.Copias, pedido.NomeArquivo, pedido.PedidoId);

        return Task.CompletedTask;
    }
}
