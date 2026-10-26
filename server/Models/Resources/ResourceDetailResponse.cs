namespace Server.Models.Resources;

public sealed class ResourceDetailResponse : ResourceSummaryResponse
{
    public required ResourceSpaceResponse Space { get; init; }
    public string? Description { get; init; }
    public string ApprovalMode { get; init; } = string.Empty;
    public bool FollowTeamCalendar { get; init; }
    public string? PublicAccess { get; init; }
    public bool AllowReservationSharing { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public ResourceConfigResponse? Config { get; init; }
}
