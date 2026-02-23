using Launcher.Bridge.Abstractions;
using Launcher.Bridge.Contracts;
using Launcher.Bridge.Handlers;

namespace Launcher.Bridge.Dispatch;

public sealed class CommandDispatcher
{
    private readonly SettingsHandler _settingsHandler;
    private readonly ProfilesHandler _profilesHandler;
    private readonly InstallHandler _installHandler;
    private readonly LaunchHandler _launchHandler;

    public CommandDispatcher(
        string? settingsPath = null,
        string? profilesPath = null,
        IEventSink? eventSink = null)
    {
        var sink = eventSink ?? new NullEventSink();
        _settingsHandler = new SettingsHandler(
            settingsPath ?? Path.Combine(AppContext.BaseDirectory, "data", "settings.json"));
        _profilesHandler = new ProfilesHandler(
            profilesPath ?? Path.Combine(AppContext.BaseDirectory, "data", "profiles.json"));
        _installHandler = new InstallHandler(sink);
        _launchHandler = new LaunchHandler(sink);
    }

    public Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken = default)
    {
        return request.Command switch
        {
            "settings.read" => _settingsHandler.ReadAsync(cancellationToken),
            "settings.write" => _settingsHandler.WriteAsync(request.Payload, cancellationToken),
            "profiles.read" => _profilesHandler.ReadAsync(cancellationToken),
            "profiles.write" => _profilesHandler.WriteAsync(request.Payload, cancellationToken),
            "install.run" => _installHandler.RunAsync(cancellationToken),
            "launch.run" => _launchHandler.RunAsync(cancellationToken),
            _ => Task.FromResult(BridgeResponse.Fail("UNKNOWN_COMMAND", $"Unsupported command: {request.Command}"))
        };
    }

    private sealed class NullEventSink : IEventSink
    {
        public void Publish(BridgeEvent bridgeEvent)
        {
        }
    }
}
