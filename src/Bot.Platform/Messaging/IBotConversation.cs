using Bot.Platform.Configuration;
using Bot.Platform.Time;
using Bot.Platform.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bot.Platform.Messaging;

/// <summary>Ожидание текстового ответа пользователя в личном чате (одно на пользователя, хранится в БД платформы).</summary>
public interface IBotConversation
{
    Task ExpectReplyAsync(long userId, string module, string action, string? payload, CancellationToken cancellationToken);

    /// <returns>true, если ожидание было и удалено.</returns>
    Task<bool> CancelAsync(long userId, CancellationToken cancellationToken);
}

internal sealed record PendingReply(string Module, string Action, string Payload);

internal sealed class BotConversation(
    PlatformDbContext db,
    BotRouteTable routes,
    IClock clock,
    IOptions<BotOptions> options) : IBotConversation
{
    private const int MaxPayloadLength = 256;

    public async Task ExpectReplyAsync(long userId, string module, string action, string? payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(action);
        if (routes.FindReply(module, action) is null)
        {
            throw new ArgumentException($"Reply '{action}' is not registered for module '{module}'.", nameof(action));
        }

        var value = payload ?? string.Empty;
        if (value.Length > MaxPayloadLength)
        {
            throw new ArgumentException($"Payload must not exceed {MaxPayloadLength} characters.", nameof(payload));
        }

        var expiresAt = clock.UtcNow + options.Value.ReplyTimeout;
        if (!await TryUpdateAsync(userId, module, action, value, expiresAt, cancellationToken))
        {
            db.PendingReplies.Add(new PendingReplyEntity
            {
                UserId = userId,
                Module = module,
                Action = action,
                Payload = value,
                ExpiresAt = expiresAt,
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Гонка: параллельный запрос вставил запись этого пользователя. Перезаписываем её.
                db.ChangeTracker.Clear();
                if (!await TryUpdateAsync(userId, module, action, value, expiresAt, cancellationToken))
                {
                    throw;
                }
            }
        }
    }

    public async Task<bool> CancelAsync(long userId, CancellationToken cancellationToken) =>
        await db.PendingReplies.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <summary>
    /// Атомарно забирает ожидание: удаляет запись и возвращает её, только если удаление затронуло строку
    /// и срок не истёк. Просроченное удаляется, возвращается null.
    /// </summary>
    internal async Task<PendingReply?> ConsumeAsync(long userId, CancellationToken cancellationToken)
    {
        var entity = await db.PendingReplies.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        // Условие включает все прочитанные поля: если параллельный запрос уже забрал ожидание и handler поставил
        // новое, строка отличается и не удаляется — handler не вызывается повторно, новое ожидание сохраняется.
        var deleted = await db.PendingReplies
            .Where(r => r.UserId == userId
                && r.Module == entity.Module
                && r.Action == entity.Action
                && r.Payload == entity.Payload
                && r.ExpiresAt == entity.ExpiresAt)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted != 1 || entity.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        return new PendingReply(entity.Module, entity.Action, entity.Payload);
    }

    internal async Task<bool> HasActiveAsync(long userId, CancellationToken cancellationToken)
    {
        var expiresAt = await db.PendingReplies
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => (DateTimeOffset?)r.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
        return expiresAt is { } value && value > clock.UtcNow;
    }

    private async Task<bool> TryUpdateAsync(
        long userId,
        string module,
        string action,
        string payload,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        await db.PendingReplies
            .Where(r => r.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Module, module)
                    .SetProperty(r => r.Action, action)
                    .SetProperty(r => r.Payload, payload)
                    .SetProperty(r => r.ExpiresAt, expiresAt),
                cancellationToken) > 0;
}
