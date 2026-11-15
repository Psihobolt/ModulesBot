using System.Security.Claims;
using System.Text.Encodings.Web;
using Bot.Platform.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Bot.Platform.Auth;

internal static class TelegramInitDataDefaults
{
    public const string Scheme = "TelegramInitData";
    public const string HeaderScheme = "tma";

    internal static readonly object ErrorKey = new();
}

internal sealed class TelegramInitDataHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IInitDataValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Prefix = TelegramInitDataDefaults.HeaderScheme + " ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return Task.FromResult(Reject(InitDataError.Missing));
        }

        if (header.Length > InitDataValidator.MaxLength + Prefix.Length
            || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Reject(InitDataError.Invalid));
        }

        var result = validator.Validate(header[Prefix.Length..]);
        if (result.Data is not { } data)
        {
            return Task.FromResult(Reject(result.Error ?? InitDataError.Invalid));
        }

        Context.Items[TelegramHttpContextExtensions.InitDataKey] = data;
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, data.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var error = Context.Items.TryGetValue(TelegramInitDataDefaults.ErrorKey, out var value) && value is InitDataError e
            ? e
            : InitDataError.Missing;

        var (code, title) = error switch
        {
            InitDataError.Expired => ("auth_expired", "Authorization data expired"),
            InitDataError.Invalid => ("auth_invalid", "Authorization data invalid"),
            _ => ("auth_missing", "Authorization required"),
        };

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return ApiErrors.Problem(StatusCodes.Status401Unauthorized, code, title).ExecuteAsync(Context);
    }

    private AuthenticateResult Reject(InitDataError error)
    {
        Context.Items[TelegramInitDataDefaults.ErrorKey] = error;
        // Анонимные запросы (/health, статика) приходят без заголовка — это не событие. Остальное — без секретов.
        if (error != InitDataError.Missing)
        {
            Logger.LogInformation("initData rejected: {Reason}", error);
        }

        return AuthenticateResult.Fail(error.ToString());
    }
}
