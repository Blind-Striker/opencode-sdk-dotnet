using System.Globalization;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

var endpoint = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_ENDPOINT");
if (string.IsNullOrWhiteSpace(endpoint))
{
    await Console.Error.WriteLineAsync(
        "Set OPENCODE_SANDBOX_ENDPOINT to an absolute server endpoint; the launchSettings.json profile prefills it.").ConfigureAwait(false);
    await Console.Error.WriteLineAsync(
        "Optional: OPENCODE_SERVER_PASSWORD (read by the SDK itself).").ConfigureAwait(false);
    return 1;
}

// No explicit password: the SDK resolves OPENCODE_SERVER_PASSWORD on its own at construction.
using var client = new OpenCodeClient(new Uri(endpoint));

var health = await client.GetHealthAsync().ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"health:  status={health.Status} healthy={health.Health.Healthy} version={health.Health.Version} pid={health.Health.Pid}"));

var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
{
    Title = "sdk breadth demo",
}).ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"create:  status={created.Status} id={created.Session.Id} title={created.Session.Title}"));

var page = await client.Sessions.ListSessionsAsync(new SessionListOptions
{
    Limit = 3,
    Order = ListOrder.Descending,
}).ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"list:    status={page.Status} sessions={page.Sessions.Count} cursor.next={page.Cursor.Next ?? "<none>"}"));
foreach (var session in page.Sessions)
{
    Console.WriteLine($"         {session.Id}  {session.Title}");
}

var handle = client.Sessions.GetSessionClient(created.Session.Id);
var fetched = await handle.GetSessionAsync().ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"get:     status={fetched.Status} id={fetched.Session.Id} directory={fetched.Session.Location.Directory}"));

var messages = await handle.ListMessagesAsync(new MessageListOptions
{
    Limit = 5,
}).ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"messages: status={messages.Status} count={messages.Messages.Count} cursor.next={messages.Cursor.Next ?? "<none>"}"));
foreach (var message in messages.Messages)
{
    Console.WriteLine($"         {message.GetType().Name}");
}

return 0;
