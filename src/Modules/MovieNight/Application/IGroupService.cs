namespace Modules.MovieNight.Application;

public interface IInviteCodeGenerator
{
    string Next();
}

public enum GroupError
{
    GroupNotFound,
    MemberNotFound,
    InviteNotFound,
    NotOwner,
    CannotRemoveSelf,
    GroupLimit,
    InvalidName,
}

public sealed class GroupException(GroupError error) : Exception($"Group error: {error}")
{
    public GroupError Error { get; } = error;
}

/// <summary>JoinSeq — стабильный id членства.</summary>
public sealed record MemberInfo(
    long UserId,
    long JoinSeq,
    string DisplayName,
    string? Username,
    int Position,
    DateTimeOffset JoinedAt,
    bool IsOwner);

public sealed record GroupDetails(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    long OwnerId,
    IReadOnlyList<MemberInfo> Members,
    string InviteCode,
    string? MiniAppLink,
    string? BotLink);

public sealed record GroupSummary(Guid Id, string Name, int MemberCount, bool IsOwner);

public sealed record InviteInfo(string Code, string? MiniAppLink, string? BotLink);

public sealed record InvitePreview(string GroupName, IReadOnlyList<string> MemberNames, bool AlreadyMember, Guid? GroupId);

public sealed record JoinResult(bool Joined, GroupDetails Group);

/// <summary>Ошибки бизнес-правил — GroupException; уведомления шлёт сам сервис.</summary>
public interface IGroupService
{
    Task<IReadOnlyList<GroupSummary>> ListAsync(long userId, CancellationToken ct);

    Task<GroupDetails> CreateAsync(long userId, string? name, CancellationToken ct);

    Task<GroupDetails> GetAsync(long userId, Guid groupId, CancellationToken ct);

    Task<GroupDetails> RenameAsync(long userId, Guid groupId, string? name, CancellationToken ct);

    Task<InviteInfo> RegenerateInviteAsync(long userId, Guid groupId, CancellationToken ct);

    Task RemoveMemberAsync(long userId, Guid groupId, long memberUserId, CancellationToken ct);

    Task LeaveAsync(long userId, Guid groupId, CancellationToken ct);

    Task<InvitePreview> GetInviteAsync(long userId, string code, CancellationToken ct);

    Task<JoinResult> JoinAsync(long userId, string code, CancellationToken ct);
}
