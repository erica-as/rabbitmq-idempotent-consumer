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

    public Task<bool> JaProcessadoAsync(Guid pedidoId)
    {
        return Task.FromResult(_processados.ContainsKey(pedidoId));
    }

    public Task MarcarComoProcessadoAsync(Guid pedidoId)
    {
        _processados.TryAdd(pedidoId, 0);
        return Task.CompletedTask;
    }
}