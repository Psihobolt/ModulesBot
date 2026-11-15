using Bot.Platform.Messaging;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.MovieNight.Application;
using Modules.MovieNight.Domain;

namespace Modules.MovieNight.Persistence;

internal sealed class EfGroupService(
    MovieNightDbContext db,
    IClock clock,
    IInviteCodeGenerator codeGenerator,
    IUserDirectory directory,
    IMessageSender sender,
    IDeepLinks deepLinks,
    ILogger<EfGroupService> logger) : IGroupService
{
    private const string ModuleName = "movienight";
    private const int CodeAttempts = 5;
    private const int WriteAttempts = 3;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteConstraint = 19;

    private enum NoticeKind
    {
        Joined,
        Left,
        LeftNewOwner,
        Removed,
    }

    private sealed record Notice(long RecipientId, NoticeKind Kind, long SubjectId, string GroupName);

    public async Task<IReadOnlyList<GroupSummary>> ListAsync(long userId, CancellationToken ct)
    {
        var rows = await (
            from m in db.Members.AsNoTracking()
            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
            where m.UserId == userId
            orderby m.JoinSeq descending
            select new { g.Id, g.Name, g.OwnerId, Count = db.Members.Count(x => x.GroupId == g.Id) })
            .ToListAsync(ct);
        return rows.Select(r => new GroupSummary(r.Id, r.Name, r.Count, r.OwnerId == userId)).ToList();
    }

    public async Task<GroupDetails> CreateAsync(long userId, string? name, CancellationToken ct)
    {
        if (!GroupRules.TryNormalizeName(name, out var normalized))
        {
            throw new GroupException(GroupError.InvalidName);
        }

        var id = await WriteAsync<Guid>(
            async () =>
            {
                if (await CountMembershipsAsync(userId, ct) >= GroupRules.MaxGroupsPerUser)
                {
                    throw new GroupException(GroupError.GroupLimit);
                }

                var now = clock.UtcNow;
                var group = new Group
                {
                    Id = Guid.NewGuid(),
                    Name = normalized,
                    InviteCode = await AllocateCodeAsync(ct),
                    OwnerId = userId,
                    CreatedBy = userId,
                    CreatedAt = now,
                };
                db.Groups.Add(group);
                db.Members.Add(new GroupMember { GroupId = group.Id, UserId = userId, JoinedAt = now });
                await db.SaveChangesAsync(ct);
                return (group.Id, []);
            },
            ct);
        return await LoadDetailsAsync(id, ct);
    }

    public async Task<GroupDetails> GetAsync(long userId, Guid groupId, CancellationToken ct)
    {
        await RequireMembershipAsync(userId, groupId, ct);
        return await LoadDetailsAsync(groupId, ct);
    }

    public async Task<GroupDetails> RenameAsync(long userId, Guid groupId, string? name, CancellationToken ct)
    {
        await WriteAsync<int>(
            async () =>
            {
                var group = await RequireOwnedGroupAsync(userId, groupId, ct);
                if (!GroupRules.TryNormalizeName(name, out var normalized))
                {
                    throw new GroupException(GroupError.InvalidName);
                }

                group.Name = normalized;
                await db.SaveChangesAsync(ct);
                return (0, []);
            },
            ct);
        return await LoadDetailsAsync(groupId, ct);
    }

    public async Task<InviteInfo> RegenerateInviteAsync(long userId, Guid groupId, CancellationToken ct)
    {
        var code = await WriteAsync<string>(
            async () =>
            {
                var group = await RequireOwnedGroupAsync(userId, groupId, ct);
                var newCode = await AllocateCodeAsync(ct);
                db.RevokedInviteCodes.Add(new RevokedInviteCode
                {
                    Code = group.InviteCode,
                    GroupId = group.Id,
                    RevokedAt = clock.UtcNow,
                });
                group.InviteCode = newCode;
                await db.SaveChangesAsync(ct);
                return (newCode, []);
            },
            ct);
        return new InviteInfo(code, deepLinks.MiniApp(ModuleName, "join_" + code), deepLinks.BotStart(ModuleName, "join_" + code));
    }

    public async Task RemoveMemberAsync(long userId, Guid groupId, long memberUserId, CancellationToken ct) =>
        await WriteAsync<int>(
            async () =>
            {
                var group = await RequireOwnedGroupAsync(userId, groupId, ct);
                if (memberUserId == userId)
                {
                    throw new GroupException(GroupError.CannotRemoveSelf);
                }

                var target = await db.Members.FirstOrDefaultAsync(
                    m => m.GroupId == groupId && m.UserId == memberUserId, ct)
                    ?? throw new GroupException(GroupError.MemberNotFound);
                return (0, await RemoveAsync(group, userId, target, ct));
            },
            ct);

    public async Task<string> RemoveMembershipAsync(long userId, Guid groupId, long joinSeq, CancellationToken ct) =>
        await WriteAsync<string>(
            async () =>
            {
                var group = await RequireOwnedGroupAsync(userId, groupId, ct);
                var target = await db.Members.FirstOrDefaultAsync(
                    m => m.GroupId == groupId && m.JoinSeq == joinSeq, ct)
                    ?? throw new GroupException(GroupError.MemberNotFound);
                if (target.UserId == userId)
                {
                    throw new GroupException(GroupError.CannotRemoveSelf);
                }

                var users = await directory.FindManyAsync([target.UserId], ct);
                var name = NameOf(users, target.UserId);
                var notices = await RemoveAsync(group, userId, target, ct);
                return (name, notices);
            },
            ct);

    public async Task LeaveAsync(long userId, Guid groupId, CancellationToken ct) =>
        await WriteAsync<int>(
            async () =>
            {
                var group = await RequireMemberGroupAsync(userId, groupId, ct);
                var own = await db.Members.FirstAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
                db.Members.Remove(own);

                var remaining = await db.Members.Where(m => m.GroupId == groupId && m.UserId != userId)
                    .OrderBy(m => m.JoinSeq).Select(m => m.UserId).ToListAsync(ct);
                var notices = new List<Notice>();
                if (remaining.Count == 0)
                {
                    group.OwnerId = null;
                }
                else
                {
                    var newOwner = group.OwnerId == userId ? remaining[0] : (long?)null;
                    if (newOwner is { } owner)
                    {
                        group.OwnerId = owner;
                    }

                    notices.AddRange(remaining.Select(id => new Notice(
                        id,
                        id == newOwner ? NoticeKind.LeftNewOwner : NoticeKind.Left,
                        userId,
                        group.Name)));
                }

                await db.SaveChangesAsync(ct);
                return (0, notices);
            },
            ct);

    public async Task<InvitePreview> GetInviteAsync(long userId, string code, CancellationToken ct)
    {
        var group = await FindGroupByCodeAsync(code, ct);
        var memberIds = await db.Members.AsNoTracking().Where(m => m.GroupId == group.Id)
            .OrderBy(m => m.JoinSeq).Select(m => m.UserId).ToListAsync(ct);
        if (memberIds.Count == 0)
        {
            throw new GroupException(GroupError.InviteNotFound);
        }

        var users = await directory.FindManyAsync(memberIds, ct);
        var already = memberIds.Contains(userId);
        return new InvitePreview(
            group.Name,
            memberIds.Select(id => NameOf(users, id)).ToList(),
            already,
            already ? group.Id : null);
    }

    public async Task<JoinResult> JoinAsync(long userId, string code, CancellationToken ct)
    {
        var (joined, groupId) = await WriteAsync<(bool, Guid)>(
            async () =>
            {
                var group = await FindGroupByCodeAsync(code, ct);
                var existing = await db.Members.Where(m => m.GroupId == group.Id)
                    .Select(m => m.UserId).ToListAsync(ct);
                if (existing.Count == 0)
                {
                    throw new GroupException(GroupError.InviteNotFound);
                }

                if (existing.Contains(userId))
                {
                    return ((false, group.Id), []);
                }

                if (await CountMembershipsAsync(userId, ct) >= GroupRules.MaxGroupsPerUser)
                {
                    throw new GroupException(GroupError.GroupLimit);
                }

                db.Members.Add(new GroupMember { GroupId = group.Id, UserId = userId, JoinedAt = clock.UtcNow });
                await db.SaveChangesAsync(ct);
                var notices = existing.Select(id => new Notice(id, NoticeKind.Joined, userId, group.Name)).ToList();
                return ((true, group.Id), notices);
            },
            ct);
        return new JoinResult(joined, await LoadDetailsAsync(groupId, ct));
    }

    // Общая часть исключения: удаление членства и уведомления (остальным, кроме исполнителя, и исключённому).
    private async Task<List<Notice>> RemoveAsync(Group group, long executorId, GroupMember target, CancellationToken ct)
    {
        db.Members.Remove(target);
        await db.SaveChangesAsync(ct);

        var others = await db.Members.Where(m => m.GroupId == group.Id && m.UserId != executorId)
            .Select(m => m.UserId).ToListAsync(ct);
        var notices = others.Select(id => new Notice(id, NoticeKind.Left, target.UserId, group.Name)).ToList();
        notices.Add(new Notice(target.UserId, NoticeKind.Removed, target.UserId, group.Name));
        return notices;
    }

    private static bool IsRetryable(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqliteException { SqliteErrorCode: SqliteBusy or SqliteLocked or SqliteConstraint })
            {
                return true;
            }
        }

        return false;
    }

    private static string NameOf(IReadOnlyDictionary<long, PlatformUser> users, long id) =>
        users.TryGetValue(id, out var user) && !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : GroupRules.UnknownMemberName;

    // Все изменяющие сценарии: транзакция BEGIN IMMEDIATE (блокировка записи сразу) — "прочитать → проверить → записать"
    // сериализуется между запросами. SQLITE_BUSY/LOCKED/CONSTRAINT — повтор всей операции (до WriteAttempts).
    // Уведомления уходят после commit и не влияют на результат.
    private async Task<T> WriteAsync<T>(Func<Task<(T Value, List<Notice> Notices)>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                var (value, notices) = await RunInTransactionAsync(operation, ct);
                await NotifyAsync(notices);
                return value;
            }
            catch (Exception e) when (attempt < WriteAttempts && IsRetryable(e))
            {
                logger.LogWarning("MovieNight write attempt {Attempt} failed with a retryable database error ({Type}).", attempt, e.GetType().Name);
            }
        }
    }

    private async Task<(T Value, List<Notice> Notices)> RunInTransactionAsync<T>(
        Func<Task<(T Value, List<Notice> Notices)>> operation,
        CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await db.Database.UseTransactionAsync(transaction, ct);
            try
            {
                var result = await operation();
                await transaction.CommitAsync(ct);
                return result;
            }
            finally
            {
                await db.Database.UseTransactionAsync(null, ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task NotifyAsync(IReadOnlyList<Notice> notices)
    {
        if (notices.Count == 0)
        {
            return;
        }

        try
        {
            var ids = notices.SelectMany(n => new[] { n.RecipientId, n.SubjectId }).Distinct().ToArray();
            var users = await directory.FindManyAsync(ids, CancellationToken.None);
            foreach (var notice in notices)
            {
                if (!users.TryGetValue(notice.RecipientId, out var recipient) || !recipient.CanReceiveMessages)
                {
                    continue;
                }

                try
                {
                    var result = await sender.SendAsync(
                        notice.RecipientId,
                        new OutgoingMessage(TextOf(notice, NameOf(users, notice.SubjectId))),
                        CancellationToken.None);
                    if (result.Status != SendStatus.Sent)
                    {
                        logger.LogWarning("MovieNight notification to user {UserId} was not sent: {Status}.", notice.RecipientId, result.Status);
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning("MovieNight notification to user {UserId} failed ({Type}).", notice.RecipientId, e.GetType().Name);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("MovieNight notifications were not delivered ({Type}).", e.GetType().Name);
        }
    }

    private static string TextOf(Notice notice, string subjectName) => notice.Kind switch
    {
        NoticeKind.Joined => $"{subjectName} теперь в группе «{notice.GroupName}».",
        NoticeKind.Left => $"{subjectName} больше не в группе «{notice.GroupName}».",
        NoticeKind.LeftNewOwner => $"{subjectName} больше не в группе «{notice.GroupName}». Теперь вы владелец группы.",
        NoticeKind.Removed => $"Вас исключили из группы «{notice.GroupName}».",
        _ => throw new ArgumentOutOfRangeException(nameof(notice)),
    };

    private Task<int> CountMembershipsAsync(long userId, CancellationToken ct) =>
        db.Members.CountAsync(m => m.UserId == userId, ct);

    private async Task<string> AllocateCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < CodeAttempts; i++)
        {
            var code = codeGenerator.Next();
            if (!await db.Groups.AnyAsync(g => g.InviteCode == code, ct)
                && !await db.RevokedInviteCodes.AnyAsync(r => r.Code == code, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique invite code.");
    }

    private async Task<Group> RequireMemberGroupAsync(long userId, Guid groupId, CancellationToken ct) =>
        await (from m in db.Members
               join g in db.Groups on m.GroupId equals g.Id
               where m.UserId == userId && g.Id == groupId
               select g).FirstOrDefaultAsync(ct)
        ?? throw new GroupException(GroupError.GroupNotFound);

    private async Task RequireMembershipAsync(long userId, Guid groupId, CancellationToken ct)
    {
        if (!await db.Members.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct))
        {
            throw new GroupException(GroupError.GroupNotFound);
        }
    }

    // Членство проверяется раньше владельца: не-участник получает 404, а не 403.
    private async Task<Group> RequireOwnedGroupAsync(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireMemberGroupAsync(userId, groupId, ct);
        return group.OwnerId == userId ? group : throw new GroupException(GroupError.NotOwner);
    }

    private async Task<Group> FindGroupByCodeAsync(string code, CancellationToken ct)
    {
        if (!GroupRules.IsValidInviteCode(code))
        {
            throw new GroupException(GroupError.InviteNotFound);
        }

        // SQLite "=" для TEXT регистрозависим (BINARY collation); коды перевыпущенных групп в Groups не хранятся.
        return await db.Groups.FirstOrDefaultAsync(g => g.InviteCode == code, ct)
            ?? throw new GroupException(GroupError.InviteNotFound);
    }

    private async Task<GroupDetails> LoadDetailsAsync(Guid groupId, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new GroupException(GroupError.GroupNotFound);
        var members = await db.Members.AsNoTracking().Where(m => m.GroupId == groupId)
            .OrderBy(m => m.JoinSeq).ToListAsync(ct);
        if (members.Count == 0)
        {
            throw new GroupException(GroupError.GroupNotFound);
        }

        var users = await directory.FindManyAsync(members.Select(m => m.UserId).ToArray(), ct);
        var ownerId = group.OwnerId ?? members[0].UserId;
        var infos = members.Select((m, index) => new MemberInfo(
            m.UserId,
            m.JoinSeq,
            NameOf(users, m.UserId),
            users.TryGetValue(m.UserId, out var user) ? user.Username : null,
            index + 1,
            m.JoinedAt,
            m.UserId == ownerId)).ToList();
        var payload = "join_" + group.InviteCode;
        return new GroupDetails(
            group.Id,
            group.Name,
            group.CreatedAt,
            ownerId,
            infos,
            group.InviteCode,
            deepLinks.MiniApp(ModuleName, payload),
            deepLinks.BotStart(ModuleName, payload));
    }
}
