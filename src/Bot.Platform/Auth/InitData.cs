namespace Bot.Platform.Auth;

public sealed record TelegramUser(
    long Id,
    string FirstName,
    string? LastName,
    string? Username,
    string? LanguageCode,
    bool IsPremium,
    bool? AllowsWriteToPm);

public sealed record InitData(
    TelegramUser User,
    DateTimeOffset AuthDate,
    string? StartParam,
    string? QueryId,
    string? ChatType,
    string? ChatInstance);

public enum InitDataError
{
    Missing,
    Invalid,
    Expired,
}

public sealed record InitDataValidationResult(InitData? Data, InitDataError? Error);

public interface IInitDataValidator
{
    InitDataValidationResult Validate(string? rawInitData);
}
