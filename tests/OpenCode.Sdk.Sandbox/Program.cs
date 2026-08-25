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
var password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD") ?? Environment.GetEnvironmentVariable("OPENCODE_SERVER_PASSWORD");

var streamMode = args.Contains("--stream", StringComparer.Ordinal);
var eventMode = args.Contains("--events", StringComparer.Ordinal);
var paginationMode = args.Contains("--paginate", StringComparer.Ordinal);
var selectedModeCount = (streamMode ? 1 : 0) + (eventMode ? 1 : 0) + (paginationMode ? 1 : 0);
if (selectedModeCount > 1)
{
    await Console.Error.WriteLineAsync("Choose only one of --stream, --events, or --paginate.").ConfigureAwait(false);
    return 1;
}

var hostArgs = args
    .Where(static argument => !string.Equals(argument, "--stream", StringComparison.Ordinal)
                              && !string.Equals(argument, "--events", StringComparison.Ordinal)
                              && !string.Equals(argument, "--paginate", StringComparison.Ordinal))
    .ToArray();

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
else if (eventMode)
{
    _ = builder.Services.AddSingleton<EventBusWorker>();
    _ = builder.Services.AddHostedService(static provider => provider.GetRequiredService<EventBusWorker>());
}

using var host = builder.Build();

if (streamMode)
{
    var worker = host.Services.GetRequiredService<SessionLogWorker>();
    await host.RunAsync().ConfigureAwait(false);
    return worker.Failure is null ? 0 : 1;
}

if (eventMode)
{
    var worker = host.Services.GetRequiredService<EventBusWorker>();
    await host.RunAsync().ConfigureAwait(false);
    return worker.Failure is null ? 0 : 1;
}

var client = host.Services.GetRequiredService<OpenCodeClient>();

var health = await client.GetHealthAsync().ConfigureAwait(false);
Console.WriteLine($"health:  status={health.Status} healthy={health.Health.Healthy} version={health.Health.Version} pid={health.Health.Pid}");

if (paginationMode)
{
    var sessionId = Environment.GetEnvironmentVariable("OPENCODE_PAGINATION_SESSION_ID");
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        await Console.Error.WriteLineAsync("Set OPENCODE_PAGINATION_SESSION_ID for --paginate.").ConfigureAwait(false);
        return 1;
    }

    var count = 0;
    var sessionClient = client.Sessions.GetSessionClient(sessionId);

    var listRequest = new MessageListRequest
    {
        Limit = "1",
        Order = ListOrder.Ascending,
    };
    var messageStream = sessionClient.EnumerateMessagesAsync(listRequest, CancellationToken.None);

    await foreach (var message in messageStream.WithCancellation(CancellationToken.None))
    {
        count++;
        Console.WriteLine($"page-item-{count}: {message.GetType().Name}/{message.Type}");
        if (count is 2)
        {
            break;
        }
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"enumerated={count}"));
    return count is 2 ? 0 : 1;
}

// Sub-clients resolve directly from the container as well.
var sessionsClient = host.Services.GetRequiredService<SessionsClient>();

var createRequest = new SessionCreateRequest
{
    Title = "sdk breadth demo",
};
var created = await sessionsClient.CreateSessionAsync(createRequest).ConfigureAwait(false);

Console.WriteLine($"create:  status={created.Status} id={created.Session.Id} title={created.Session.Title}");

var sessionListRequest = new SessionListRequest
{
    Limit = "3",
    Order = ListOrder.Descending,
};
var page = await sessionsClient.ListSessionsAsync(sessionListRequest).ConfigureAwait(false);

Console.WriteLine($"list:    status={page.Status} sessions={page.Sessions.Count} cursor.next={page.Cursor.Next ?? "<none>"}");

foreach (var session in page.Sessions)
{
    Console.WriteLine($"         {session.Id}  {session.Title}");
}

var handle = sessionsClient.GetSessionClient(created.Session.Id);
var fetched = await handle.GetSessionAsync().ConfigureAwait(false);

Console.WriteLine($"get:     status={fetched.Status} id={fetched.Session.Id} directory={fetched.Session.Location.Directory}");

var messageListRequest = new MessageListRequest
{
    Limit = "5",
};
var messages = await handle.ListMessagesAsync(messageListRequest).ConfigureAwait(false);

Console.WriteLine($"messages: status={messages.Status} count={messages.Messages.Count} cursor.next={messages.Cursor.Next ?? "<none>"}");

foreach (var message in messages.Messages)
{
    Console.WriteLine($"         {message.GetType().Name}");
}

await SessionActionsWalkthrough.RunAsync(handle).ConfigureAwait(false);

return 0;
