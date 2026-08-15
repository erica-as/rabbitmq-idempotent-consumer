using Microsoft.AspNetCore.Mvc;
using Shared.Messages;
using Solicitante.Api.DTOs;
using Solicitante.Api.Messaging.Interface;

namespace Solicitante.Api.Controllers;

[ApiController]
[Route("pedidos")]
public class PedidosController : ControllerBase
{
    private readonly IPedidoPublisher _publisher;
    private readonly ILogger<PedidosController> _logger;

    public PedidosController(IPedidoPublisher publisher, ILogger<PedidosController> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarPedidoRequest request)
    {
        var pedido = new PedidoImpressao(
            PedidoId: request.PedidoId ?? Guid.NewGuid(),
            NomeArquivo: request.NomeArquivo,
            Copias: request.Copias);

        _logger.LogInformation(
            "Recebida requisição de impressão: {NomeArquivo}, {Copias} cópia(s), PedidoId {PedidoId}.",
            pedido.NomeArquivo, pedido.Copias, pedido.PedidoId);

        try
        {
            await _publisher.PublicarAsync(pedido);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao publicar pedido {PedidoId} na fila.", pedido.PedidoId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { erro = "Falha ao publicar o pedido na fila." });
        }

        return Accepted(new CriarPedidoResponse(pedido.PedidoId));
    }
}
