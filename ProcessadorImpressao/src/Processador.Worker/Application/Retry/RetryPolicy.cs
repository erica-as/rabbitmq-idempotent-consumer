using RabbitMQ.Client;

namespace Processador.Worker.Application.Retry;

/// <summary>
/// Política de retry do processamento. Centraliza a contagem de tentativas
/// (header "x-retry-count") e a decisão de esgotamento, deixando o Consumer
/// como orquestração pura.
/// </summary>
public sealed class RetryPolicy
{
    public const string HeaderRetentativa = "x-retry-count";

    public RetryPolicy(int maxTentativas)
    {
        MaxTentativas = maxTentativas;
    }

    public int MaxTentativas { get; }

    /// <summary>
    /// True quando a mensagem já atingiu o máximo de tentativas e deve ir para
    /// a DLQ; false quando ainda pode ser republicada para um novo retry.
    /// </summary>
    public bool TentativasEsgotadas(int tentativas) => tentativas >= MaxTentativas;

    /// <summary>
    /// Lê quantas vezes a mensagem já foi republicada, a partir do header
    /// "x-retry-count" adicionado no reencaminhamento. Mensagens que chegam
    /// pela primeira vez não têm esse header (retorna 0).
    /// </summary>
    public static int LerTentativasAnteriores(IBasicProperties? properties)
    {
        if (properties?.Headers is { } headers &&
            headers.TryGetValue(HeaderRetentativa, out var valor) &&
            valor is int tentativas)
        {
            return tentativas;
        }

        return 0;
    }
}
