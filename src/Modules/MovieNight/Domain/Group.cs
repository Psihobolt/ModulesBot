namespace Modules.MovieNight.Domain;

public sealed class Group
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string InviteCode { get; set; } = string.Empty;

    /// <summary>Участник с наименьшим JoinSeq; null, если в группе не осталось участников.</summary>
    public long? OwnerId { get; set; }

    public long CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GroupMember
{
    /// <summary>Глобально возрастающий id членства; порядок участников — по нему.</summary>
    public long JoinSeq { get; set; }

    public Guid GroupId { get; set; }

    public long UserId { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class RevokedInviteCode
{
    public string Code { get; set; } = string.Empty;

    public Guid GroupId { get; set; }

    public DateTimeOffset RevokedAt { get; set; }
}
