using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Sandbox;

var endpoint = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_ENDPOINT");
if (string.IsNullOrWhiteSpace(endpoint))
{
    await Console
        .Error.WriteLineAsync(
            "Set OPENCODE_SANDBOX_ENDPOINT to an absolute server endpoint; the launchSettings.json profile prefills it.")
        .ConfigureAwait(false);
    await Console
        .Error.WriteLineAsync(
            "Optional: OPENCODE_PASSWORD or OPENCODE_SERVER_PASSWORD (resolved here; the SDK reads no environment).")
        .ConfigureAwait(false);
    return 1;
}

// The consumer owns environment resolution (upstream's own layering; the CLI does the same):
// the SDK itself never reads environment variables.
var password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD")
               ?? Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD");

var streamMode = args.Contains("--stream", StringComparer.Ordinal);
var hostArgs = args.Where(static argument => !string.Equals(argument, "--stream", StringComparison.Ordinal)).ToArray();
var builder = Host.CreateApplicationBuilder(hostArgs);
_ = builder.Services.AddOpenCode(options =>
{
    options.Endpoint = new Uri(endpoint);
    options.Password = string.IsNullOrWhiteSpace(password) ? null : password;
});
if (streamMode)
{
    _ = builder.Services.AddSingleton<SessionLogWorker>();
    _ = builder.Services.AddHostedService(static provider => provider.GetRequiredService<SessionLogWorker>());
}

using var host = builder.Build();

if (streamMode)
{
    var worker = host.Services.GetRequiredService<SessionLogWorker>();
    await host.RunAsync().ConfigureAwait(false);
    return worker.Failure is null ? 0 : 1;
}

var client = host.Services.GetRequiredService<OpenCodeClient>();

var health = await client.GetHealthAsync().ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"health:  status={health.Status} healthy={health.Health.Healthy} version={health.Health.Version} pid={health.Health.Pid}"));

// Sub-clients resolve directly from the container as well.
var sessionsClient = host.Services.GetRequiredService<SessionsClient>();

var created = await sessionsClient
    .CreateSessionAsync(new SessionCreateRequest
    {
        Title = "sdk breadth demo",
    })
    .ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"create:  status={created.Status} id={created.Session.Id} title={created.Session.Title}"));

var page = await sessionsClient
    .ListSessionsAsync(new SessionListRequest
    {
        Limit = "3",
        Order = ListOrder.Descending,
    })
    .ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"list:    status={page.Status} sessions={page.Sessions.Count} cursor.next={page.Cursor.Next ?? "<none>"}"));
foreach (var session in page.Sessions)
{
    Console.WriteLine($"         {session.Id}  {session.Title}");
}

var handle = sessionsClient.GetSessionClient(created.Session.Id);
var fetched = await handle.GetSessionAsync().ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"get:     status={fetched.Status} id={fetched.Session.Id} directory={fetched.Session.Location.Directory}"));

var messages = await handle
    .ListMessagesAsync(new MessageListRequest
    {
        Limit = "5",
    })
    .ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"messages: status={messages.Status} count={messages.Messages.Count} cursor.next={messages.Cursor.Next ?? "<none>"}"));
foreach (var message in messages.Messages)
{
    Console.WriteLine($"         {message.GetType().Name}");
}

return 0;
