namespace Launcher.Core.Errors;

public sealed record UserFacingError(
    ErrorCode Code,
    string Title,
    string Message,
    string? Suggestion = null,
    string? TechnicalDetails = null);
