using System.Text.Json;
using Bot.Platform.Api;
using Bot.Platform.Auth;
using Modules.MovieNight.Application;

namespace Modules.MovieNight.Api;

internal static class GroupEndpoints
{
    private const string NameError = "Название — от 1 до 64 символов, без переводов строк.";

    public static void Map(IEndpointRouteBuilder api)
    {
        var groups = api.MapGroup("/groups");
        groups.MapGet("/", (HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var list = await service.ListAsync(http.GetTelegramUserId(), ct);
                return Results.Ok(list.Select(g => new GroupSummaryDto(g.Id, g.Name, g.MemberCount, g.IsOwner)).ToList());
            }));

        groups.MapPost("/", (HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var request = await ReadRequestAsync(http.Request, ct, name => new CreateGroupRequest(name));
                if (request is null)
                {
                    return BadBody();
                }

                var userId = http.GetTelegramUserId();
                var group = await service.CreateAsync(userId, request.Name, ct);
                return Results.Created($"/api/movienight/groups/{group.Id:D}", ToDto(group, userId));
            }));

        groups.MapGet("/{id:guid}", (Guid id, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var userId = http.GetTelegramUserId();
                return Results.Ok(ToDto(await service.GetAsync(userId, id, ct), userId));
            }));

        groups.MapPut("/{id:guid}/name", (Guid id, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var request = await ReadRequestAsync(http.Request, ct, name => new RenameGroupRequest(name));
                if (request is null)
                {
                    return BadBody();
                }

                var userId = http.GetTelegramUserId();
                return Results.Ok(ToDto(await service.RenameAsync(userId, id, request.Name, ct), userId));
            }));

        groups.MapPost("/{id:guid}/invite", (Guid id, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var invite = await service.RegenerateInviteAsync(http.GetTelegramUserId(), id, ct);
                return Results.Ok(new InviteDto(invite.Code, invite.MiniAppLink, invite.BotLink));
            }));

        groups.MapDelete("/{id:guid}/members/{userId:long}", (Guid id, long userId, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                await service.RemoveMemberAsync(http.GetTelegramUserId(), id, userId, ct);
                return Results.NoContent();
            }));

        groups.MapPost("/{id:guid}/leave", (Guid id, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                await service.LeaveAsync(http.GetTelegramUserId(), id, ct);
                return Results.NoContent();
            }));

        var invites = api.MapGroup("/invites");
        invites.MapGet("/{code}", (string code, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var preview = await service.GetInviteAsync(http.GetTelegramUserId(), code, ct);
                return Results.Ok(new InvitePreviewDto(
                    preview.GroupName,
                    preview.MemberNames,
                    preview.MemberNames.Count,
                    preview.AlreadyMember,
                    preview.GroupId));
            }));

        invites.MapPost("/{code}/join", (string code, HttpContext http, IGroupService service, CancellationToken ct) =>
            Run(async () =>
            {
                var userId = http.GetTelegramUserId();
                var result = await service.JoinAsync(userId, code, ct);
                return Results.Ok(new JoinResultDto(result.Joined, ToDto(result.Group, userId)));
            }));
    }

    private static async Task<IResult> Run(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler();
        }
        catch (GroupException e)
        {
            return ToProblem(e.Error);
        }
    }

    private static IResult ToProblem(GroupError error) => error switch
    {
        GroupError.InvalidName => ApiErrors.Validation(new Dictionary<string, string[]> { ["name"] = [NameError] }),
        GroupError.CannotRemoveSelf => ApiErrors.Problem(
            StatusCodes.Status400BadRequest, "movienight_cannot_remove_self", "Чтобы уйти из группы, используйте «Выйти»."),
        GroupError.NotOwner => ApiErrors.Forbidden("movienight_not_owner", "Это может сделать только владелец группы."),
        GroupError.GroupNotFound => ApiErrors.NotFound("movienight_group_not_found", "Группа не найдена."),
        GroupError.MemberNotFound => ApiErrors.NotFound("movienight_member_not_found", "Участник не найден."),
        GroupError.InviteNotFound => ApiErrors.NotFound(
            "movienight_invite_not_found", "Приглашение недействительно или группа больше не существует."),
        GroupError.GroupLimit => ApiErrors.Problem(
            StatusCodes.Status409Conflict, "movienight_group_limit", "Можно состоять не больше чем в 20 группах."),
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };

    private static IResult BadBody() =>
        ApiErrors.Problem(StatusCodes.Status400BadRequest, "bad_request", "Request body must be a JSON object.");

    // Тело разбирается вручную: поле name другого типа (число, null) — это validation_failed, а не ошибка привязки,
    // а отсутствие/некорректность JSON — 400 независимо от окружения (в Development привязка минимального API бросает).
    private static async Task<T?> ReadRequestAsync<T>(HttpRequest request, CancellationToken ct, Func<string?, T> create)
        where T : class
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("name") || property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    return create(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null);
                }
            }

            return create(null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GroupDto ToDto(GroupDetails group, long userId) => new(
        group.Id,
        group.Name,
        group.CreatedAt,
        group.OwnerId,
        group.OwnerId == userId,
        group.Members.Select(m => new MemberDto(
            m.UserId, m.DisplayName, m.Username, m.Position, m.JoinedAt, m.UserId == userId, m.IsOwner)).ToList(),
        new InviteDto(group.InviteCode, group.MiniAppLink, group.BotLink));
}
