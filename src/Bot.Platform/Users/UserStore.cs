using Bot.Platform.Auth;
using Bot.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace Bot.Platform.Users;

internal sealed class UserStore(PlatformDbContext db, IClock clock) : IUserDirectory, IUserRegistry
{
    public async Task<PlatformUser?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyDictionary<long, PlatformUser>> FindManyAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        var entities = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
        return entities.ToDictionary(u => u.Id, ToModel);
    }

    public async Task UpsertAsync(TelegramUser user, bool? canReceiveMessages, CancellationToken cancellationToken)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        if (existing is null)
        {
            var now = clock.UtcNow;
            db.Users.Add(new PlatformUserEntity
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                LanguageCode = user.LanguageCode,
                CanReceiveMessages = canReceiveMessages ?? false,
                FirstSeenAt = now,
                LastSeenAt = now,
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException)
            {
                // Гонка: параллельный запрос вставил ту же запись. Перечитываем и обновляем.
                db.ChangeTracker.Clear();
                existing = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        existing.FirstName = user.FirstName;
        existing.LastName = user.LastName;
        existing.Username = user.Username;
        existing.LanguageCode = user.LanguageCode;
        existing.LastSeenAt = clock.UtcNow;
        if (canReceiveMessages is { } value)
        {
            existing.CanReceiveMessages = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCannotReceiveMessagesAsync(long userId, CancellationToken cancellationToken) =>
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CanReceiveMessages, false), cancellationToken);

    private static PlatformUser ToModel(PlatformUserEntity e) => new(
        e.Id,
        e.FirstName,
        e.LastName,
        e.Username,
        e.LanguageCode,
        e.CanReceiveMessages,
        e.FirstSeenAt,
        e.LastSeenAt);
}
