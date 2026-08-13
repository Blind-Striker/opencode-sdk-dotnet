using System.Globalization;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

var endpoint = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_ENDPOINT");
if (string.IsNullOrWhiteSpace(endpoint))
{
    await Console.Error.WriteLineAsync(
        "Set OPENCODE_SANDBOX_ENDPOINT to an absolute server endpoint; the launchSettings.json profile prefills it.").ConfigureAwait(false);
    await Console.Error.WriteLineAsync(
        "Optional: OPENCODE_SERVER_PASSWORD (read by the SDK itself), OPENCODE_SANDBOX_SESSION_ID, OPENCODE_SANDBOX_MESSAGE_ID.").ConfigureAwait(false);
    return 1;
}

// No explicit password: the SDK resolves OPENCODE_SERVER_PASSWORD on its own at construction.
using var client = new OpenCodeClient(new Uri(endpoint));

var health = await client.GetHealthAsync().ConfigureAwait(false);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"health: status={health.Status} healthy={health.Health.Healthy} version={health.Health.Version} pid={health.Health.Pid}"));

var sessionId = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_SESSION_ID");
var messageId = Environment.GetEnvironmentVariable("OPENCODE_SANDBOX_MESSAGE_ID");
if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(messageId))
{
    await Console.Out.WriteLineAsync("message: skipped (set OPENCODE_SANDBOX_SESSION_ID and OPENCODE_SANDBOX_MESSAGE_ID to fetch one)").ConfigureAwait(false);
    return 0;
}

var message = await client.Sessions.GetSessionClient(sessionId).GetMessageAsync(messageId).ConfigureAwait(false);
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"message: status={message.Status} type={message.Message.GetType().Name}"));
if (message.Message is SessionMessageAssistant assistant)
{
    Console.WriteLine($"  id:      {assistant.Id}");
    Console.WriteLine($"  agent:   {assistant.Agent}");
    Console.WriteLine($"  model:   {assistant.Model}");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  finish:  {assistant.Finish}  cost: {assistant.Cost}  tokens: {assistant.Tokens}"));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  content: {assistant.Content.Count} part(s)"));
    foreach (var part in assistant.Content)
    {
        var summary = part switch
        {
            SessionMessageAssistantText text => Excerpt(text.Text),
            SessionMessageAssistantReasoning reasoning => Excerpt(reasoning.Text),
            UnknownSessionMessageAssistantContent unknown => $"unknown tag '{unknown.Type}'",
            _ => Excerpt(part.ToString()),
        };
        Console.WriteLine($"    [{part.Type}] {summary}");
    }
}
else
{
    Console.WriteLine($"  payload: {Excerpt(message.Message.ToString())}");
}

return 0;

static string Excerpt(string? value)
{
    if (value is null)
    {
        return "<null>";
    }

    var flattened = value.ReplaceLineEndings(" ");
    return flattened.Length <= 160 ? flattened : $"{flattened[..160]}…";
}
