using Bot.Platform.Auth;

namespace Bot.Platform.Users;

public sealed record PlatformUser(
    long Id,
    string FirstName,
    string? LastName,
    string? Username,
    string? LanguageCode,
    bool CanReceiveMessages,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(LastName) ? FirstName.Trim() : $"{FirstName.Trim()} {LastName.Trim()}".Trim();
}

public interface IUserDirectory
{
    Task<PlatformUser?> FindAsync(long userId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, PlatformUser>> FindManyAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken);
}

public interface IUserRegistry
{
    Task UpsertAsync(TelegramUser user, bool? canReceiveMessages, CancellationToken cancellationToken);

    Task MarkCannotReceiveMessagesAsync(long userId, CancellationToken cancellationToken);
}
