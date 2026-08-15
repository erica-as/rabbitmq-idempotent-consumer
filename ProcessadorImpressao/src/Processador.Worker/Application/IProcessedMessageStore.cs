namespace Processador.Worker.Application.Idempotency;
 
/// <summary>
/// Contrato para verificação de idempotência. O Consumer depende apenas
/// desta interface — não sabe (nem precisa saber) se o controle de mensagens
/// já processadas está em memória, Redis ou banco relacional. Essa é a
/// Inversão de Dependência (DIP) aplicada: a regra "não processar duas vezes"
/// é da aplicação; o "onde armazenar isso" é detalhe de infraestrutura.
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Retorna true se o PedidoId já foi processado anteriormente.
    /// </summary>
    Task<bool> JaProcessadoAsync(Guid pedidoId);
 
    /// <summary>
    /// Marca o PedidoId como processado.
    /// </summary>
    Task MarcarComoProcessadoAsync(Guid pedidoId);
}