namespace Processador.Worker.Application.Idempotency;
 
/// <summary>
/// Contrato para controle de idempotência. O Consumer depende apenas desta
/// interface — não sabe (nem precisa saber) se o controle de mensagens já
/// processadas está em memória, Redis ou banco relacional. Essa é a Inversão
/// de Dependência (DIP) aplicada: a regra "não processar duas vezes" é da
/// aplicação; o "onde armazenar isso" é detalhe de infraestrutura.
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Reserva o PedidoId como processado, atomicamente. Retorna true se esta
    /// chamada venceu a reserva (mensagem ainda não processada) e false se já
    /// estava reservado por outra mensagem (duplicata em voo).
    /// </summary>
    Task<bool> TentarMarcarComoProcessadoAsync(Guid pedidoId);

    /// <summary>
    /// Libera a reserva do PedidoId. Usado quando o processamento falha, para
    /// que a mensagem reencaminhada possa ser processada de novo no retry.
    /// </summary>
    Task LiberarMarcacaoAsync(Guid pedidoId);
}