namespace EmailPlatform.Compose.Contracts;

/// <summary>
/// Public POST body for creating an announcement. No ManagerId here — that
/// comes from the X-Manager-Id header so clients can't claim to be someone else.
///
/// In production this would come from a validated JWT; for this project the
/// header is a stand-in that keeps the contract clean without adding auth.
/// </summary>
public sealed record ComposeCreateRequest
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public required IReadOnlyList<string> Recipients { get; init; }
    public required DateOnly ScheduledFor { get; init; }
}
