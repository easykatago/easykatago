using Launcher.Bridge.Abstractions;
using Launcher.Bridge.Contracts;

namespace Launcher.Bridge.Handlers;

public sealed class InstallHandler(IEventSink eventSink)
{
    public Task<BridgeResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        eventSink.Publish(new BridgeEvent("install.progress", new
        {
            stage = "download",
            percent = 5
        }));

        return Task.FromResult(BridgeResponse.Ok(new
        {
            status = "completed"
        }));
    }
}
