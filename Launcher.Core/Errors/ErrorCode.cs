namespace Launcher.Core.Errors;

public enum ErrorCode
{
    Unknown = 0,
    ManifestLoadFailed,
    ManifestInvalid,
    DownloadFailed,
    VerifyFailed,
    InstallFailed,
    ProfileLoadFailed,
    ProfileSaveFailed,
    ConfigWriteFailed,
    TuningFailed,
    LaunchFailed,
    DiagnosticsFailed
}
