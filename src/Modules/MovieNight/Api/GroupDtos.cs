namespace Modules.MovieNight.Api;

public sealed record CreateGroupRequest(string? Name);

public sealed record RenameGroupRequest(string? Name);

public sealed record GroupSummaryDto(Guid Id, string Name, int MemberCount, bool IsOwner);

public sealed record MemberDto(
    long UserId,
    string DisplayName,
    string? Username,
    int Position,
    DateTimeOffset JoinedAt,
    bool IsMe,
    bool IsOwner);

public sealed record InviteDto(string Code, string? MiniAppLink, string? BotLink);

public sealed record GroupDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    long OwnerId,
    bool IsOwner,
    IReadOnlyList<MemberDto> Members,
    InviteDto Invite);

public sealed record InvitePreviewDto(
    string GroupName,
    IReadOnlyList<string> MemberNames,
    int MemberCount,
    bool AlreadyMember,
    Guid? GroupId);

public sealed record JoinResultDto(bool Joined, GroupDto Group);
