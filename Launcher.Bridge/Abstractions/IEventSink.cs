namespace Launcher.Bridge.Abstractions;

public sealed record BridgeEvent(string Type, object? Data);

public interface IEventSink
{
    void Publish(BridgeEvent bridgeEvent);
}
