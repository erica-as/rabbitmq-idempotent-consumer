using Shared.Messages;

namespace Processador.Worker.Application.Impressao;

/// <summary>
/// Contrato de processamento de um pedido de impressão. O Consumer orquestra
/// (idempotência, retry, DLQ); o "como imprimir" é responsabilidade de quem
/// implementa esta interface.
/// </summary>
public interface IImpressaoService
{
    Task ProcessarAsync(PedidoImpressao pedido);
}
