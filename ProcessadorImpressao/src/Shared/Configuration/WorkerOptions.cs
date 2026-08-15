namespace Shared.Configuration;

/// <summary>
/// Configuração do Worker (Processador.Worker): política de retry, paralelismo
/// de consumo e arquivo que dispara a falha simulada de processamento.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Processamento";

    /// <summary>Número máximo de tentativas antes de enviar para a DLQ.</summary>
    public int MaxTentativas { get; init; } = 3;

    /// <summary>Prefetch count do BasicQos (mensagens em voo por canal).</summary>
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>Nome de arquivo que faz o processamento lançar exceção de propósito.</summary>
    public string ArquivoParaFalhar { get; init; } = "falhar.pdf";
}
