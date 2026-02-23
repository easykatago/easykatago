using Launcher.Bridge.Contracts;

namespace Launcher.Bridge.Dispatch;

public sealed class CommandDispatcher
{
    public Task<BridgeResponse> DispatchAsync(BridgeRequest request) =>
        Task.FromResult(BridgeResponse.Fail("UNKNOWN_COMMAND", $"Unsupported command: {request.Command}"));
}
