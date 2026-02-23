namespace Launcher.Bridge.Contracts;

public sealed record BridgeError(string Code, string Message);

public sealed record BridgeResponse(bool Success, object? Data, BridgeError? Error)
{
    public static BridgeResponse Ok(object? data) => new(true, data, null);

    public static BridgeResponse Fail(string code, string message) =>
        new(false, null, new BridgeError(code, message));
}
