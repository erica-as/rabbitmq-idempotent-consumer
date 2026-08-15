using Shared.Messages;

namespace Solicitante.Api.Messaging.Interface;

/// <summary>
/// Contrato para publicação de pedidos de impressão.
/// </summary>
public interface IPedidoPublisher
{
    void Publicar(PedidoImpressao pedido);
}