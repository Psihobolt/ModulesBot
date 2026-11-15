namespace Bot.Platform.Api;

public sealed record HealthDto(string Status);

public sealed record MeDto(long Id, string FirstName, string? LastName, string? Username, string? LanguageCode);
