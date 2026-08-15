namespace Shared.Configuration;

/// <summary>
/// Configuração de conexão com o RabbitMQ. HostName não é secreto e vive no
/// appsettings.json; UserName/Password devem vir de user-secrets (Development)
/// ou variáveis de ambiente — nunca do repositório.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Fail-fast: lança exceção com instruções se as credenciais não estiverem
    /// configuradas, para nunca conectar com valores errados em silêncio.
    /// </summary>
    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException(
                "Credenciais do RabbitMQ (RabbitMq:UserName e RabbitMq:Password) não configuradas. " +
                "Em Development use: dotnet user-secrets set \"RabbitMq:UserName\" \"<valor>\" " +
                "(idem para RabbitMq:Password). Em outros ambientes, defina as variáveis de " +
                "ambiente RabbitMq__UserName e RabbitMq__Password.");
        }
    }
}
