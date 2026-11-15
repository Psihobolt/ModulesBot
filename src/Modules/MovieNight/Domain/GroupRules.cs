namespace Modules.MovieNight.Domain;

public static class GroupRules
{
    public const int NameMaxLength = 64;
    public const int MaxGroupsPerUser = 20;
    public const int InviteCodeLength = 10;
    public const string UnknownMemberName = "Участник";

    /// <summary>Trim; 1..64 символов; управляющие символы (в исходной строке) запрещены.</summary>
    public static bool TryNormalizeName(string? raw, out string name)
    {
        name = string.Empty;
        if (raw is null || raw.Any(char.IsControl))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            return false;
        }

        name = trimmed;
        return true;
    }

    /// <summary>^[A-Za-z0-9]{10}$ (ASCII, регистрозависимо).</summary>
    public static bool IsValidInviteCode(string? code) =>
        code is { Length: InviteCodeLength } && code.All(char.IsAsciiLetterOrDigit);
}
