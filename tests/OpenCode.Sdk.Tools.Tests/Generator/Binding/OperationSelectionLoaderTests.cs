using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class OperationSelectionLoaderTests
{
    private const string ProfilePath = "tools/generation-profile.txt";

    [Test]
    public async Task LoadAsync_Should_Preserve_Operation_Order()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(ProfilePath, "v2.health.get\nv2.session.message\n"));

        var selection = await new OperationSelectionLoader(fileSystem).LoadAsync(ProfilePath, CancellationToken.None);

        await Assert.That(selection.OperationIds).Count().IsEqualTo(2);
        await Assert.That(selection.OperationIds[0]).IsEqualTo("v2.health.get");
        await Assert.That(selection.OperationIds[1]).IsEqualTo("v2.session.message");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_Duplicate_Operation_IDs()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(ProfilePath, "v2.health.get\nv2.health.get\n"));

        var exception = await Assert
            .That(async () => _ = await new OperationSelectionLoader(fileSystem).LoadAsync(ProfilePath, CancellationToken.None))
            .Throws<BindingException>();

        await Assert.That(exception!.Errors.Single().Category).IsEqualTo(BindingErrorCategory.Selection);
        await Assert.That(exception.Errors.Single().Problem).Contains("duplicate");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_Blank_Lines()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(ProfilePath, "v2.health.get\n\nv2.session.message\n"));

        var exception = await Assert
            .That(async () => _ = await new OperationSelectionLoader(fileSystem).LoadAsync(ProfilePath, CancellationToken.None))
            .Throws<BindingException>();

        await Assert.That(exception!.Errors.Single().Problem).Contains("blank");
    }
}
