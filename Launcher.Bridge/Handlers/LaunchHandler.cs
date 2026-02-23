using Launcher.Bridge.Abstractions;
using Launcher.Bridge.Contracts;

namespace Launcher.Bridge.Handlers;

public sealed class LaunchHandler(IEventSink eventSink)
{
    public Task<BridgeResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        eventSink.Publish(new BridgeEvent("launch.progress", new
        {
            stage = "bootstrap",
            percent = 10
        }));

        return Task.FromResult(BridgeResponse.Ok(new
        {
            status = "started"
        }));
    }
}
