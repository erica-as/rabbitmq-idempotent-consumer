using Shared.Messages;

namespace Solicitante.Api.Messaging.Interface;

/// <summary>
/// Contrato para publicação de pedidos de impressão. A implementação deve
/// garantir que a publicação foi confirmada pelo broker (publisher confirms).
/// </summary>
public interface IPedidoPublisher
{
    Task PublicarAsync(PedidoImpressao pedido);
}
