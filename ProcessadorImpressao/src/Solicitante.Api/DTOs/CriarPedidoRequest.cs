using System.ComponentModel.DataAnnotations;

namespace Solicitante.Api.DTOs;

/// <summary>
/// Entrada do POST /pedidos. A validação (DataAnnotations + [ApiController])
/// devolve 400 automaticamente quando o modelo é inválido.
/// </summary>
public sealed record CriarPedidoRequest(
    [Required, StringLength(255)] string NomeArquivo,
    [Range(1, 1000)] int Copias,
    Guid? PedidoId = null);
