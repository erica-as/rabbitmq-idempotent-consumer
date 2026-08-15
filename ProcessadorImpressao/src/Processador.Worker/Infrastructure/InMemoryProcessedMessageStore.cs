using System.Collections.Concurrent;
using Processador.Worker.Application.Idempotency;

namespace Processador.Worker.Infrastructure.Idempotency;

/// <summary>
/// Implementação simples em memória, útil para desenvolvimento e para o
/// propósito deste projeto de estudo. Não sobrevive a um restart do Worker.
///
/// Evolução natural: trocar por uma implementação com Redis (SETNX com TTL)
/// ou uma tabela relacional com PedidoId como chave única — sem precisar
/// alterar o Consumer, já que ele depende só de IProcessedMessageStore.
/// </summary>
public sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
{
    private readonly ConcurrentDictionary<Guid, byte> _processados = new();

    public Task<bool> TentarMarcarComoProcessadoAsync(Guid pedidoId)
    {
        return Task.FromResult(_processados.TryAdd(pedidoId, 0));
    }

    public Task LiberarMarcacaoAsync(Guid pedidoId)
    {
        _processados.TryRemove(pedidoId, out _);
        return Task.CompletedTask;
    }
}
