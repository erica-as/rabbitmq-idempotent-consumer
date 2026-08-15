# Configuração RabbitMQ sem Segredos no Repo — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) ou superpowers:executing-plans para implementar este plano task por task. Steps usam checkbox (`- [ ]`).

**Goal:** Remover credenciais/fallbacks hardcoded do código e appsettings, usando config tipada (`RabbitMqOptions`), user-secrets em Development e fail-fast com mensagem clara.

**Architecture:** Classe `RabbitMqOptions` em `Shared` lida via `GetSection("RabbitMq").Get<RabbitMqOptions>()` nos dois projetos. `HostName` (não-secreto) fica no `appsettings.json`; `UserName`/`Password` ficam em user-secrets (fora do repo). `Validar()` lança `InvalidOperationException` se credencial ausente, antes de qualquer conexão.

**Tech Stack:** .NET 10, Microsoft.Extensions.Configuration.Binder (transitivo do Hosting/Web SDK), `dotnet user-secrets` CLI.

## Global Constraints

- Repositório público: nenhum segredo pode ser commitado (`UserName`/`Password` só via user-secrets ou env vars).
- Não alterar topologia, idempotência, DLQ, docker-compose ou lógica de consumo.
- `RabbitMqOptions`: `SectionName = "RabbitMq"`, `HostName` default `"localhost"`.
- Fail-fast: `Validar()` lança se `UserName` ou `Password` vazio/whitespace.
- Build final: 0 erros, 0 avisos.

---

### Task 1: Classe `RabbitMqOptions` no Shared

**Files:**
- Create: `ProcessadorImpressao/src/Shared/Configuration/RabbitMqOptions.cs`

**Interfaces:**
- Produces: `Shared.Configuration.RabbitMqOptions` — propriedades `string HostName` (init, default `"localhost"`), `string UserName` (init, default `string.Empty`), `string Password` (init, default `string.Empty`), `const string SectionName = "RabbitMq"`, método `void Validar()`.

- [ ] **Step 1: Criar o arquivo**

```csharp
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
```

- [ ] **Step 2: Compilar o Shared**

Run: `dotnet build src/Shared/Shared.csproj` (a partir de `ProcessadorImpressao/`)
Expected: `Êxito` / `0 Erro(s)`

- [ ] **Step 3: Commit**

```bash
git add ProcessadorImpressao/src/Shared/Configuration/RabbitMqOptions.cs
git commit -m "feat: RabbitMqOptions tipada no Shared com fail-fast"
```

### Task 2: Worker — config tipada + fail-fast, remoção de credenciais do appsettings

**Files:**
- Modify: `ProcessadorImpressao/src/Processador.Worker/Program.cs` (arquivo inteiro)
- Modify: `ProcessadorImpressao/src/Processador.Worker/appsettings.json`

**Interfaces:**
- Consumes: `RabbitMqOptions` (Task 1: `SectionName`, `HostName`, `Validar()`)
- Produces: `Program.cs` que lança `InvalidOperationException` na startup se credenciais ausentes.

- [ ] **Step 1: Reescrever `Program.cs`**

```csharp
using Processador.Worker;
using Processador.Worker.Application.Idempotency;
using Processador.Worker.Consumers;
using Processador.Worker.Infrastructure.Idempotency;
using RabbitMQ.Client;
using Shared.Configuration;

var builder = Host.CreateApplicationBuilder(args);

var rabbitMq = builder.Configuration
    .GetSection(RabbitMqOptions.SectionName)
    .Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();

rabbitMq.Validar();

var factory = new ConnectionFactory
{
    HostName = rabbitMq.HostName,
    UserName = rabbitMq.UserName,
    Password = rabbitMq.Password
};

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>();
builder.Services.AddSingleton(_ => factory.CreateConnection());
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnection>().CreateModel());
builder.Services.AddSingleton<PedidoImpressaoConsumer>(sp => new PedidoImpressaoConsumer(
    sp.GetRequiredService<IModel>(),
    sp.GetRequiredService<IProcessedMessageStore>(),
    sp.GetRequiredService<ILogger<PedidoImpressaoConsumer>>()));

var host = builder.Build();

host.Services.GetRequiredService<PedidoImpressaoConsumer>().IniciarConsumo();

host.Run();
```

- [ ] **Step 2: Remover credenciais do `appsettings.json` do Worker**

Conteúdo final:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "RabbitMq": {
    "HostName": "localhost"
  }
}
```

- [ ] **Step 3: Compilar**

Run: `dotnet build` (a partir de `ProcessadorImpressao/`)
Expected: `0 Erro(s)`, `0 Aviso(s)`

- [ ] **Step 4: Verificar fail-fast (sem user-secrets)**

Run: `ASPNETCORE_ENVIRONMENT=Production timeout 20 dotnet run --no-launch-profile --project src/Processador.Worker`
Expected: sai imediatamente com `Unhandled exception. System.InvalidOperationException: Credenciais do RabbitMQ (RabbitMq:UserName e RabbitMq:Password) não configuradas...` (exit code != 0). Em Production o user-secrets não é carregado, então a validação falha antes de qualquer conexão.

Nota: `--no-launch-profile` é obrigatório — o `launchSettings.json` existente define `DOTNET_ENVIRONMENT=Development` (que tem precedência sobre a env var do shell no `dotnet run`), e o `Host.CreateApplicationBuilder` usa `DOTNET_ENVIRONMENT`.

- [ ] **Step 5: Commit**

```bash
git add ProcessadorImpressao/src/Processador.Worker/Program.cs ProcessadorImpressao/src/Processador.Worker/appsettings.json
git commit -m "feat: Worker com config tipada RabbitMq e fail-fast"
```

### Task 3: Api — UserSecretsId, config tipada no DI, publisher sem IConfiguration

**Files:**
- Modify: `ProcessadorImpressao/src/Solicitante.Api/Solicitante.Api.csproj` (via `dotnet user-secrets init`)
- Modify: `ProcessadorImpressao/src/Solicitante.Api/Program.cs` (arquivo inteiro)
- Modify: `ProcessadorImpressao/src/Solicitante.Api/Messaging/RabbitMqPublisher.cs` (construtor + using)
- Modify: `ProcessadorImpressao/src/Solicitante.Api/appsettings.json`

**Interfaces:**
- Consumes: `RabbitMqOptions` (Task 1)
- Produces: `RabbitMqPublisher` com construtor `(RabbitMqOptions options)` em vez de `(IConfiguration configuration)`; `RabbitMqOptions` registrado como singleton no DI.

- [ ] **Step 1: Habilitar user-secrets no Api**

Run: `dotnet user-secrets init --project src/Solicitante.Api`
Expected: adiciona `<UserSecretsId>` ao csproj, mensagem "Set UserSecretsId to '...'"

- [ ] **Step 2: Reescrever `Program.cs`**

```csharp
using Shared.Configuration;
using Solicitante.Api.Messaging;
using Solicitante.Api.Messaging.Interface;

var builder = WebApplication.CreateBuilder(args);

var rabbitMq = builder.Configuration
    .GetSection(RabbitMqOptions.SectionName)
    .Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();

rabbitMq.Validar();

builder.Services.AddControllers();
builder.Services.AddSingleton(rabbitMq);
builder.Services.AddSingleton<IPedidoPublisher, RabbitMqPublisher>();

var app = builder.Build();

app.MapControllers();

app.Run();
```

- [ ] **Step 3: Ajustar `RabbitMqPublisher.cs`**

Substituir o construtor (linhas ~20–33) e o using:

```csharp
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.Configuration;
using Shared.Constants;
using Shared.Messages;
using Solicitante.Api.Messaging.Interface;

namespace Solicitante.Api.Messaging;

/// <summary>
/// Publica pedidos de impressão na fila principal. A fila é declarada como
/// durável e com dead-letter-exchange configurada, para que mensagens
/// rejeitadas pelo consumer sejam automaticamente roteadas para a DLQ.
/// </summary>
public sealed class RabbitMqPublisher : IPedidoPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            UserName = options.UserName,
            Password = options.Password
        };
```

O restante do arquivo (DeclararTopologia, Publicar, Dispose) permanece inalterado.

- [ ] **Step 4: Adicionar seção `RabbitMq` ao `appsettings.json` do Api**

Conteúdo final:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "RabbitMq": {
    "HostName": "localhost"
  }
}
```

- [ ] **Step 5: Compilar**

Run: `dotnet build` (a partir de `ProcessadorImpressao/`)
Expected: `0 Erro(s)`, `0 Aviso(s)`

- [ ] **Step 6: Verificar fail-fast (sem user-secrets)**

Run: `ASPNETCORE_ENVIRONMENT=Production timeout 20 dotnet run --no-launch-profile --project src/Solicitante.Api`
Expected: sai imediatamente com `Unhandled exception. System.InvalidOperationException: Credenciais do RabbitMQ...` (exit code != 0)

Nota: `--no-launch-profile` é obrigatório pelos mesmos motivos da Task 2 (o `launchSettings.json` do Api define `ASPNETCORE_ENVIRONMENT=Development`, vencendo a env var do shell no `dotnet run`).

- [ ] **Step 7: Commit**

```bash
git add ProcessadorImpressao/src/Solicitante.Api/Program.cs ProcessadorImpressao/src/Solicitante.Api/Messaging/RabbitMqPublisher.cs ProcessadorImpressao/src/Solicitante.Api/appsettings.json ProcessadorImpressao/src/Solicitante.Api/Solicitante.Api.csproj
git commit -m "feat: Api com config tipada RabbitMq, user-secrets e publisher sem IConfiguration"
```

### Task 4: User-secrets do Api e verificação ponta a ponta

**Files:**
- Nenhum arquivo do repo (user-secrets ficam fora do git)

**Interfaces:**
- Nenhuma nova.

- [ ] **Step 1: Definir credenciais de dev do Api**

```bash
dotnet user-secrets set "RabbitMq:UserName" "guest" --project src/Solicitante.Api
dotnet user-secrets set "RabbitMq:Password" "guest" --project src/Solicitante.Api
```

Expected: `Successfully saved RabbitMq:UserName to the secret store.` (idem Password)

- [ ] **Step 2: Confirmar que nenhum segredo está no index do git**

Run: `git grep -n "guest" -- ProcessadorImpressao/src || echo "OK: nenhum guest no repo"`
Expected: `OK: nenhum guest no repo` (o único `guest` restante vive em `~/.microsoft/usersecrets/`, fora do git)

- [ ] **Step 3: Verificar inicialização em Development (validação passa)**

Run: `timeout 15 dotnet run --project src/Processador.Worker` (a partir de `ProcessadorImpressao/`)
Expected: NÃO aparece `InvalidOperationException` de credenciais; a aplicação tenta conectar ao broker (`localhost:5672`, RabbitMQ fora do ar na máquina de dev) e a falha de conexão é a única exceção — prova que a validação de config passou.

Run: `timeout 15 dotnet run --project src/Solicitante.Api`
Expected: idem — sem erro de credenciais; apenas falha de conexão RabbitMQ ao resolver o singleton `RabbitMqPublisher`.

- [ ] **Step 4: Build final**

Run: `dotnet build` (a partir de `ProcessadorImpressao/`)
Expected: `0 Erro(s)`, `0 Aviso(s)` — fim da implementação.