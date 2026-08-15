namespace Solicitante.Api.DTOs;

/// <summary>
/// Resposta do POST /pedidos.
/// </summary>
public sealed record CriarPedidoResponse(Guid PedidoId);
