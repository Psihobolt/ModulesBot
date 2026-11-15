using System.Security.Cryptography;
using Modules.MovieNight.Domain;

namespace Modules.MovieNight.Application;

internal sealed class InviteCodeGenerator : IInviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public string Next() => RandomNumberGenerator.GetString(Alphabet, GroupRules.InviteCodeLength);
}
