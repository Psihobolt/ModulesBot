namespace Bot.Platform.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public TimeSpan InitDataMaxAge { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan AllowedClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
