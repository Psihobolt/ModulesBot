using System.Globalization;
using Bot.Platform.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Modules.MovieNight.Application;

namespace Modules.MovieNight.Telegram;

internal static class GroupBotRoutes
{
    private const string Module = "movienight";
    private const string JoinPrefix = "join_";

    public static void Map(IBotRouteBuilder bot)
    {
        bot.MapCommand("groups", "мои группы", ListCommandAsync);
        bot.MapCommand("newgroup", "создать группу", NewGroupCommandAsync);
        bot.MapStart(StartAsync);

        bot.MapCallback("groups", ctx => ShowListAsync(ctx));
        bot.MapCallback("new", ctx => AskNameAsync(ctx));
        bot.MapCallback("cancel", CancelAsync);
        bot.MapCallback("nojoin", ctx => ctx.EditAsync(new OutgoingMessage(GroupBotTexts.NotJoining)));
        bot.MapCallback("join", JoinAsync);
        bot.MapCallback("g", WithGroup(ShowCardAsync));
        bot.MapCallback("invite", WithGroup(InviteAsync));
        bot.MapCallback("leave", WithGroup(LeaveAsync));
        bot.MapCallback("leaveok", WithGroup(LeaveOkAsync));
        bot.MapCallback("rename", WithGroup(RenameAsync));
        bot.MapCallback("newlink", WithGroup(NewLinkAsync));
        bot.MapCallback("newlinkok", WithGroup(NewLinkOkAsync));
        bot.MapCallback("members", WithGroup(MembersAsync));
        bot.MapCallback("kick", WithGroup(KickAsync));
        bot.MapCallback("kickok", WithGroup(KickOkAsync));

        bot.MapReply("newname", NewNameReplyAsync);
        bot.MapReply("rename", RenameReplyAsync);
    }

    private static IGroupService Groups(BotContext ctx) => ctx.Services.GetRequiredService<IGroupService>();

    private static IBotConversation Conversation(BotContext ctx) => ctx.Services.GetRequiredService<IBotConversation>();

    private static OutgoingMessage CardMessage(BotContext ctx, GroupDetails group) =>
        new(
            GroupBotTexts.Card(group, ctx.From.Id),
            GroupBotKeyboards.GroupCard(group, ctx.From.Id, ctx.Services.GetRequiredService<IDeepLinks>().WebAppUrl));

    private static OutgoingMessage MembersMessage(BotContext ctx, GroupDetails group) =>
        new(GroupBotTexts.MembersTitle(group.Name), GroupBotKeyboards.Members(group, ctx.From.Id));

    private static async Task<OutgoingMessage> ListMessageAsync(BotContext ctx)
    {
        var groups = await Groups(ctx).ListAsync(ctx.From.Id, ctx.CancellationToken);
        return new OutgoingMessage(
            groups.Count == 0 ? GroupBotTexts.NoGroups : GroupBotTexts.GroupsTitle,
            GroupBotKeyboards.GroupList(groups));
    }

    // ---- команды и ввод текста ----

    private static async Task ListCommandAsync(BotCommandContext ctx) =>
        await ctx.ReplyAsync(await ListMessageAsync(ctx));

    private static async Task NewGroupCommandAsync(BotCommandContext ctx)
    {
        if (ctx.Arguments.Length == 0)
        {
            await AskNameAsync(ctx);
            return;
        }

        await CreateAsync(ctx, ctx.Arguments, retryOnInvalid: false);
    }

    private static async Task NewNameReplyAsync(BotReplyContext ctx) =>
        await CreateAsync(ctx, ctx.Text, retryOnInvalid: true);

    private static async Task CreateAsync(BotContext ctx, string name, bool retryOnInvalid)
    {
        try
        {
            var group = await Groups(ctx).CreateAsync(ctx.From.Id, name, ctx.CancellationToken);
            await ctx.ReplyAsync(CardMessage(ctx, group));
        }
        catch (GroupException e) when (e.Error == GroupError.InvalidName)
        {
            if (retryOnInvalid)
            {
                await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.InvalidNameRetry));
                await Conversation(ctx).ExpectReplyAsync(ctx.From.Id, Module, "newname", null, ctx.CancellationToken);
            }
            else
            {
                await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.InvalidName));
            }
        }
        catch (GroupException e) when (e.Error == GroupError.GroupLimit)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.GroupLimit));
        }
    }

    private static async Task AskNameAsync(BotContext ctx)
    {
        await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.AskName, GroupBotKeyboards.Cancel()));
        await Conversation(ctx).ExpectReplyAsync(ctx.From.Id, Module, "newname", null, ctx.CancellationToken);
    }

    private static async Task CancelAsync(BotCallbackContext ctx)
    {
        await Conversation(ctx).CancelAsync(ctx.From.Id, ctx.CancellationToken);
        await ctx.EditAsync(new OutgoingMessage(GroupBotTexts.Cancelled));
    }

    private static async Task RenameReplyAsync(BotReplyContext ctx)
    {
        if (!Guid.TryParseExact(ctx.Payload, "D", out var id))
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.NotMember));
            return;
        }

        try
        {
            var group = await Groups(ctx).RenameAsync(ctx.From.Id, id, ctx.Text, ctx.CancellationToken);
            await ctx.ReplyAsync(CardMessage(ctx, group));
        }
        catch (GroupException e) when (e.Error == GroupError.InvalidName)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.InvalidNameRetry));
            await Conversation(ctx).ExpectReplyAsync(ctx.From.Id, Module, "rename", id.ToString("D"), ctx.CancellationToken);
        }
        catch (GroupException e) when (e.Error == GroupError.GroupNotFound)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.NotMember));
        }
        catch (GroupException e) when (e.Error == GroupError.NotOwner)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.NotOwner));
        }
    }

    // ---- обёртка колбэков с {id} ----

    private static BotHandler<BotCallbackContext> WithGroup(Func<BotCallbackContext, Guid, string, Task> handler) =>
        async ctx =>
        {
            var parts = ctx.Payload.Split(':', 2);
            if (!Guid.TryParseExact(parts[0], "D", out var id))
            {
                await NotMemberAsync(ctx);
                return;
            }

            try
            {
                await handler(ctx, id, parts.Length > 1 ? parts[1] : string.Empty);
            }
            catch (GroupException e)
            {
                switch (e.Error)
                {
                    case GroupError.GroupNotFound:
                        await NotMemberAsync(ctx);
                        break;
                    case GroupError.NotOwner:
                        await AlertThenAsync(ctx, id, GroupBotTexts.NotOwner, members: false);
                        break;
                    case GroupError.MemberNotFound:
                        await AlertThenAsync(ctx, id, GroupBotTexts.MemberGone, members: true);
                        break;
                    case GroupError.CannotRemoveSelf:
                        await AlertThenAsync(ctx, id, GroupBotTexts.CannotRemoveSelf, members: true);
                        break;
                    default:
                        throw;
                }
            }
        };

    private static async Task NotMemberAsync(BotCallbackContext ctx)
    {
        await ctx.AnswerAsync(GroupBotTexts.NotMember, showAlert: true);
        await ShowListAsync(ctx);
    }

    private static async Task AlertThenAsync(BotCallbackContext ctx, Guid id, string alert, bool members)
    {
        GroupDetails group;
        try
        {
            group = await Groups(ctx).GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        }
        catch (GroupException e) when (e.Error == GroupError.GroupNotFound)
        {
            await NotMemberAsync(ctx);
            return;
        }

        if (members && group.OwnerId != ctx.From.Id)
        {
            await ctx.AnswerAsync(GroupBotTexts.NotOwner, showAlert: true);
            await ctx.EditAsync(CardMessage(ctx, group));
            return;
        }

        await ctx.AnswerAsync(alert, showAlert: true);
        await ctx.EditAsync(members ? MembersMessage(ctx, group) : CardMessage(ctx, group));
    }

    /// <summary>Членство → владелец (порядок 004 AC-10). null — ответ уже отправлен.</summary>
    private static async Task<GroupDetails?> OwnedAsync(BotCallbackContext ctx, Guid id)
    {
        var group = await Groups(ctx).GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        if (group.OwnerId == ctx.From.Id)
        {
            return group;
        }

        await ctx.AnswerAsync(GroupBotTexts.NotOwner, showAlert: true);
        await ctx.EditAsync(CardMessage(ctx, group));
        return null;
    }

    // ---- навигация ----

    private static async Task ShowListAsync(BotCallbackContext ctx) =>
        await ctx.EditAsync(await ListMessageAsync(ctx));

    private static async Task ShowCardAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        var group = await Groups(ctx).GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        await ctx.EditAsync(CardMessage(ctx, group));
    }

    private static async Task InviteAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        var group = await Groups(ctx).GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        if (group.MiniAppLink is null || group.BotLink is null)
        {
            await ctx.AnswerAsync(GroupBotTexts.InviteUnavailable, showAlert: true);
            return;
        }

        await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.Invite(group)));
    }

    private static async Task LeaveAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        var group = await Groups(ctx).GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        await ctx.EditAsync(new OutgoingMessage(
            GroupBotTexts.LeaveConfirm(group, ctx.From.Id),
            GroupBotKeyboards.Confirm(
                GroupBotTexts.LeaveYesButton,
                GroupBotKeyboards.GroupData("leaveok", id),
                GroupBotKeyboards.GroupData("g", id))));
    }

    private static async Task LeaveOkAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        var service = Groups(ctx);
        var group = await service.GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        await service.LeaveAsync(ctx.From.Id, id, ctx.CancellationToken);
        await ctx.EditAsync(new OutgoingMessage(
            GroupBotTexts.Left(group.Name),
            GroupBotKeyboards.Single(GroupBotTexts.ToListButton, GroupBotKeyboards.Data("groups"))));
    }

    // ---- действия владельца ----

    private static async Task RenameAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is not { } group)
        {
            return;
        }

        await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.AskRename(group.Name), GroupBotKeyboards.Cancel()));
        await Conversation(ctx).ExpectReplyAsync(ctx.From.Id, Module, "rename", id.ToString("D"), ctx.CancellationToken);
    }

    private static async Task NewLinkAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is null)
        {
            return;
        }

        await ctx.EditAsync(new OutgoingMessage(
            GroupBotTexts.NewLinkConfirm,
            GroupBotKeyboards.Confirm(
                GroupBotTexts.NewLinkYesButton,
                GroupBotKeyboards.GroupData("newlinkok", id),
                GroupBotKeyboards.GroupData("g", id))));
    }

    private static async Task NewLinkOkAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is null)
        {
            return;
        }

        var service = Groups(ctx);
        await service.RegenerateInviteAsync(ctx.From.Id, id, ctx.CancellationToken);
        var group = await service.GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        await ctx.EditAsync(CardMessage(ctx, group));
        await ctx.AnswerAsync(GroupBotTexts.NewLinkCreated);
        if (group.MiniAppLink is not null && group.BotLink is not null)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.Invite(group)));
        }
    }

    private static async Task MembersAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is not { } group)
        {
            return;
        }

        await ctx.EditAsync(MembersMessage(ctx, group));
    }

    private static async Task KickAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is not { } group)
        {
            return;
        }

        var member = TryParseJoinSeq(rest, out var joinSeq)
            ? group.Members.FirstOrDefault(m => m.JoinSeq == joinSeq && m.UserId != ctx.From.Id)
            : null;
        if (member is null)
        {
            await ctx.AnswerAsync(GroupBotTexts.MemberGone, showAlert: true);
            await ctx.EditAsync(MembersMessage(ctx, group));
            return;
        }

        await ctx.EditAsync(new OutgoingMessage(
            GroupBotTexts.KickConfirm(member.DisplayName, group.Name),
            GroupBotKeyboards.Confirm(
                GroupBotTexts.KickYesButton,
                GroupBotKeyboards.MemberData("kickok", id, joinSeq),
                GroupBotKeyboards.GroupData("members", id))));
    }

    private static async Task KickOkAsync(BotCallbackContext ctx, Guid id, string rest)
    {
        if (await OwnedAsync(ctx, id) is not { } group)
        {
            return;
        }

        if (!TryParseJoinSeq(rest, out var joinSeq))
        {
            await ctx.AnswerAsync(GroupBotTexts.MemberGone, showAlert: true);
            await ctx.EditAsync(MembersMessage(ctx, group));
            return;
        }

        var service = Groups(ctx);
        var name = await service.RemoveMembershipAsync(ctx.From.Id, id, joinSeq, ctx.CancellationToken);
        var updated = await service.GetAsync(ctx.From.Id, id, ctx.CancellationToken);
        await ctx.AnswerAsync(GroupBotTexts.Kicked(name));
        await ctx.EditAsync(MembersMessage(ctx, updated));
    }

    private static bool TryParseJoinSeq(string text, out long joinSeq) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out joinSeq);

    // ---- вступление ----

    private static async Task StartAsync(BotStartContext ctx)
    {
        var code = ctx.Payload.StartsWith(JoinPrefix, StringComparison.Ordinal) ? ctx.Payload[JoinPrefix.Length..] : string.Empty;
        try
        {
            var preview = await Groups(ctx).GetInviteAsync(ctx.From.Id, code, ctx.CancellationToken);
            if (preview.AlreadyMember)
            {
                var text = GroupBotTexts.AlreadyMember(preview.GroupName);
                await ctx.ReplyAsync(preview.GroupId is { } groupId
                    ? new OutgoingMessage(text, GroupBotKeyboards.Single(GroupBotTexts.OpenGroupButton, GroupBotKeyboards.GroupData("g", groupId)))
                    : new OutgoingMessage(text));
                return;
            }

            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.InvitePreview(preview), GroupBotKeyboards.JoinPrompt(code)));
        }
        catch (GroupException e) when (e.Error == GroupError.InviteNotFound)
        {
            await ctx.ReplyAsync(new OutgoingMessage(GroupBotTexts.InviteInvalid));
        }
    }

    private static async Task JoinAsync(BotCallbackContext ctx)
    {
        try
        {
            var result = await Groups(ctx).JoinAsync(ctx.From.Id, ctx.Payload, ctx.CancellationToken);
            await ctx.EditAsync(CardMessage(ctx, result.Group));
        }
        catch (GroupException e) when (e.Error == GroupError.InviteNotFound)
        {
            await ctx.EditAsync(new OutgoingMessage(GroupBotTexts.InviteInvalid));
        }
        catch (GroupException e) when (e.Error == GroupError.GroupLimit)
        {
            await ctx.AnswerAsync(GroupBotTexts.GroupLimit, showAlert: true);
        }
    }
}
