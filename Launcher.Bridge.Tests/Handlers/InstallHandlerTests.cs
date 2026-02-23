using Launcher.Bridge.Abstractions;
using Launcher.Bridge.Handlers;

namespace Launcher.Bridge.Tests.Handlers;

public sealed class InstallHandlerTests
{
    [Fact]
    public async Task InstallRun_EmitsProgressEvents()
    {
        var sink = new InMemoryEventSink();
        var handler = new InstallHandler(sink);

        await handler.RunAsync(CancellationToken.None);

        Assert.Contains(sink.Events, e => e.Type == "install.progress");
    }

    private sealed class InMemoryEventSink : IEventSink
    {
        public List<BridgeEvent> Events { get; } = [];

        public void Publish(BridgeEvent bridgeEvent)
        {
            Events.Add(bridgeEvent);
        }
    }
}
