using Launcher.Bridge.Contracts;
using Launcher.Bridge.Dispatch;

namespace Launcher.Bridge.Tests.Dispatch;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_UnknownCommand_ReturnsErrorEnvelope()
    {
        var dispatcher = new CommandDispatcher();
        var response = await dispatcher.DispatchAsync(new BridgeRequest("x.unknown", null));
        Assert.False(response.Success);
        Assert.Equal("UNKNOWN_COMMAND", response.Error?.Code);
    }
}
