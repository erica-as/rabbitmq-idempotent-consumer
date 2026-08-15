using Microsoft.AspNetCore.Mvc;
using Shared.Messages;
using Solicitante.Api.Messaging.Interface;
 
namespace Solicitante.Api.Controllers;
 
[ApiController]
[Route("pedidos")]
public class PedidosController : ControllerBase
{
    private readonly IPedidoPublisher _publisher;
 
    public PedidosController(IPedidoPublisher publisher)
    {
        _publisher = publisher;
    }
 
    public record CriarPedidoRequest(string NomeArquivo, int Copias, Guid? PedidoId = null);
 
    /// <summary>
    /// Cria e publica um pedido de impressão. PedidoId é opcional: se não for
    /// informado, um novo é gerado. Para testar idempotência manualmente,
    /// envie o mesmo PedidoId em duas requisições seguidas.
    /// </summary>
    [HttpPost]
    public IActionResult Criar([FromBody] CriarPedidoRequest request)
    {
        var pedido = new PedidoImpressao(
            PedidoId: request.PedidoId ?? Guid.NewGuid(),
            NomeArquivo: request.NomeArquivo,
            Copias: request.Copias);
 
        _publisher.Publicar(pedido);
 
        return Accepted(new { pedido.PedidoId });
    }
}