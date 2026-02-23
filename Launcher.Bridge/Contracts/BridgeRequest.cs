namespace Launcher.Bridge.Contracts;

public sealed record BridgeRequest(string Command, object? Payload);
