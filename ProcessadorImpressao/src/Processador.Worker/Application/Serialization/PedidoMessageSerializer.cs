using System.Text;
using System.Text.Json;
using Shared.Messages;

namespace Processador.Worker.Application.Serialization;

/// <summary>
/// Serialização do contrato <see cref="PedidoImpressao"/> em/para JSON.
/// </summary>
public static class PedidoMessageSerializer
{
    public static PedidoImpressao? Desserializar(byte[] body)
    {
        var json = Encoding.UTF8.GetString(body);
        return JsonSerializer.Deserialize<PedidoImpressao>(json);
    }
}
