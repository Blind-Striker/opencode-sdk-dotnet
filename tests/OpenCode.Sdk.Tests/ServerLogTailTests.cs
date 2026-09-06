using System.Globalization;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

public sealed class ServerLogTailTests
{
    [Test]
    public async Task Snapshot_Should_Bound_Long_Lines_And_Report_Discarded_Output()
    {
        var tail = new ServerLogTail();
        for (var index = 0; index < 501; index++)
        {
            tail.Append(index.ToString(CultureInfo.InvariantCulture) + new string('x', 5_000));
        }

        var snapshot = tail.Snapshot();

        await Assert.That(snapshot.Count).IsEqualTo(501);
        await Assert.That(snapshot[0]).Contains("discarded-lines=1 shortened-lines=501");
        await Assert.That(snapshot.Skip(1).All(static line => line.Length == 4_096)).IsTrue();
        await Assert.That(snapshot[500]).StartsWith("500");
        await Assert.That(snapshot[500]).EndsWith(" [truncated]");
    }
}
