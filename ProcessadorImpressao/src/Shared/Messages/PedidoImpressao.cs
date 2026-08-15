namespace Shared.Messages;

public record PedidoImpressao(
    Guid PedidoId,
    string NomeArquivo,
    int Copias
);