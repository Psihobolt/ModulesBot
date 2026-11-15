using Bot.Platform.Messaging;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;

namespace Bot.Platform.Tests;

// TEST-4: AC-12, AC-13
public class BotRoutingRegistrationTests
{
    private static readonly BotHandler<BotCommandContext> Noop = _ => Task.CompletedTask;
    private static readonly BotHandler<BotCallbackContext> NoopCb = _ => Task.CompletedTask;
    private static readonly BotHandler<BotStartContext> NoopStart = _ => Task.CompletedTask;

    private static WebApplicationBuilder NewBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Bot:Token"] = TestInitData.BotToken;
        builder.Configuration["Bot:WebhookSecret"] = TestBot.WebhookSecret;
        builder.AddPlatform();
        return builder;
    }

    private static InvalidOperationException Reject(string module, Action<IBotRouteBuilder> map, Action<WebApplicationBuilder>? before = null)
    {
        var builder = NewBuilder();
        before?.Invoke(builder);
        using var _ = ConfigurableModule.Use(module, map);
        return Assert.Throws<InvalidOperationException>(() => builder.AddModule<ConfigurableModule>());
    }

    private static void Accept(Action<IBotRouteBuilder> map)
    {
        var builder = NewBuilder();
        using var _ = ConfigurableModule.Use("mod", map);
        builder.AddModule<ConfigurableModule>();
    }

    // ---------------- AC-12: commands ----------------

    [Theory]
    [InlineData("Ping")]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("пинг")]
    [InlineData("x-y")]
    [InlineData("/ping")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]   // 33
    public void AC12_InvalidCommandFormat_ThrowsNamingModuleAndCommand(string command)
    {
        var ex = Reject("mod", b => b.MapCommand(command, "Описание", Noop));

        Assert.Contains("mod", ex.Message);
        Assert.Contains(command, ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("_")]
    [InlineData("0")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdef")]    // 32
    [InlineData("ping_2")]
    public void AC12_ValidCommandFormat_Accepted(string command) =>
        Accept(b => b.MapCommand(command, "Описание", Noop));

    [Theory]
    [InlineData("start")]
    [InlineData("help")]
    public void AC12_ReservedPlatformCommand_Throws(string command)
    {
        var ex = Reject("mod", b => b.MapCommand(command, "Описание", Noop));

        Assert.Contains("mod", ex.Message);
        Assert.Contains(command, ex.Message);
    }

    [Fact]
    public void AC12_CommandTakenByOtherModule_ThrowsNamingBothModules()
    {
        var ex = Reject("mod", b => b.MapCommand("ping", "Описание", Noop), b => b.AddModule<DemoModule>());

        Assert.Contains("demo", ex.Message);
        Assert.Contains("mod", ex.Message);
        Assert.Contains("ping", ex.Message);
    }

    [Fact]
    public void AC12_SameCommandInTwoConfigurableModules_Throws()
    {
        var builder = NewBuilder();
        using (ConfigurableModule.Use("moda", b => b.MapCommand("same", "Описание", Noop)))
        {
            builder.AddModule<ConfigurableModule>();
        }

        using var _ = ConfigurableModuleB.Use("modb", b => b.MapCommand("same", "Описание", Noop));
        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddModule<ConfigurableModuleB>());

        Assert.Contains("moda", ex.Message);
        Assert.Contains("modb", ex.Message);
    }

    [Fact]
    public void AC12_DuplicateCommandInSameModule_Throws()
    {
        var ex = Reject("mod", b =>
        {
            b.MapCommand("dup", "Описание", Noop);
            b.MapCommand("dup", "Другое", Noop);
        });

        Assert.Contains("mod", ex.Message);
        Assert.Contains("dup", ex.Message);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    [InlineData("")]
    [InlineData("   ")]
    public void AC12_DescriptionTooShortAfterTrim_Throws(string description)
    {
        var ex = Reject("mod", b => b.MapCommand("cmd", description, Noop));

        Assert.Contains("mod", ex.Message);
        Assert.Contains("cmd", ex.Message);
    }

    [Fact]
    public void AC12_DescriptionLongerThan256_Throws()
    {
        var ex = Reject("mod", b => b.MapCommand("cmd", new string('д', 257), Noop));

        Assert.Contains("cmd", ex.Message);
    }

    [Fact]
    public void AC12_DescriptionOfBoundaryLengths_Accepted()
    {
        Accept(b => b.MapCommand("cmd", "abc", Noop));
        Accept(b => b.MapCommand("cmd", new string('д', 256), Noop));
        Accept(b => b.MapCommand("cmd", " abc ", Noop));
    }

    [Fact]
    public void AC12_NullCommandHandler_Throws()
    {
        var ex = Reject("mod", b => b.MapCommand("cmd", "Описание", null!));

        Assert.Contains("cmd", ex.Message);
    }

    [Fact]
    public void AC12_TotalCommandsAtLimit100IncludingStartAndHelp_Accepted() =>
        Accept(b =>
        {
            for (var i = 0; i < 98; i++) b.MapCommand("c" + i, "Описание", Noop);
        });

    [Fact]
    public void AC12_MoreThan100CommandsInTotal_Throws() =>
        Reject("mod", b =>
        {
            for (var i = 0; i < 99; i++) b.MapCommand("c" + i, "Описание", Noop);
        });

    // ---------------- AC-12: callbacks / start ----------------

    [Theory]
    [InlineData("Act")]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void AC12_InvalidActionFormat_Throws(string action)
    {
        var ex = Reject("mod", b => b.MapCallback(action, NoopCb));

        Assert.Contains("mod", ex.Message);
        Assert.Contains(action, ex.Message);
    }

    [Fact]
    public void AC12_ValidActions_Accepted() =>
        Accept(b =>
        {
            b.MapCallback("a", NoopCb);
            b.MapCallback("act_1", NoopCb);
            b.MapCallback("abcdefghijklmnopqrstuvwxyzabcdef", NoopCb);
        });

    [Fact]
    public void AC12_DuplicateAction_Throws()
    {
        var ex = Reject("mod", b =>
        {
            b.MapCallback("act", NoopCb);
            b.MapCallback("act", NoopCb);
        });

        Assert.Contains("act", ex.Message);
    }

    [Fact]
    public void AC12_SameActionInDifferentModules_Accepted()
    {
        var builder = NewBuilder();
        builder.AddModule<DemoModule>();   // has callback "act"
        using var _ = ConfigurableModule.Use("mod", b => b.MapCallback("act", NoopCb));

        builder.AddModule<ConfigurableModule>();
    }

    [Fact]
    public void AC12_NullCallbackHandler_Throws() =>
        Reject("mod", b => b.MapCallback("act", null!));

    [Fact]
    public void AC12_SecondMapStart_Throws()
    {
        var ex = Reject("mod", b =>
        {
            b.MapStart(NoopStart);
            b.MapStart(NoopStart);
        });

        Assert.Contains("mod", ex.Message);
    }

    [Fact]
    public void AC12_NullStartHandler_Throws() =>
        Reject("mod", b => b.MapStart(null!));

    [Fact]
    public void AC12_RouteBuilderKnowsModuleName()
    {
        string? seen = null;
        Accept(b => seen = b.ModuleName);

        Assert.Equal("mod", seen);
    }

    [Fact]
    public void AC12_ModuleWithoutMapBotOverride_RegistersWithoutErrors()
    {
        var builder = NewBuilder();

        builder.AddModule<MinLengthNameModule>();
    }

    [Fact]
    public void AC12_MapBotThrowsFromAddModuleNotFromLaterStage()
    {
        // The error must surface at AddModule<T>() itself, before Build().
        var builder = NewBuilder();
        using var _ = ConfigurableModule.Use("mod", b => b.MapCommand("start", "Описание", Noop));

        Assert.Throws<InvalidOperationException>(() => builder.AddModule<ConfigurableModule>());
    }

    // ---------------- AC-13 ----------------

    [Theory]
    [InlineData("demo", "act", "42", "demo:act:42")]
    [InlineData("demo", "act", null, "demo:act")]
    [InlineData("demo", "act", "", "demo:act")]
    [InlineData("demo", "act", "a:b", "demo:act:a:b")]
    [InlineData("ab", "a", "x", "ab:a:x")]
    public void AC13_Create_BuildsData(string module, string action, string? payload, string expected) =>
        Assert.Equal(expected, CallbackData.Create(module, action, payload));

    [Fact]
    public void AC13_Create_Exactly64BytesAscii_Ok()
    {
        var data = CallbackData.Create("demo", "act", new string('a', 55));   // "demo:act:" = 9

        Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(data));
    }

    [Fact]
    public void AC13_Create_65BytesAscii_ThrowsArgumentException() =>
        Assert.ThrowsAny<ArgumentException>(() => CallbackData.Create("demo", "act", new string('a', 56)));

    [Fact]
    public void AC13_Create_64BytesWithCyrillic_Ok()
    {
        var payload = new string('ж', 27) + "a";   // 54 + 1 = 55 bytes; 28 chars only

        var data = CallbackData.Create("demo", "act", payload);

        Assert.Equal(64, System.Text.Encoding.UTF8.GetByteCount(data));
        Assert.True(data.Length < 64);
    }

    [Fact]
    public void AC13_Create_65BytesWithCyrillicButUnder64Chars_ThrowsArgumentException()
    {
        var payload = new string('ж', 28);   // 56 bytes -> 65 total; only 37 chars

        Assert.ThrowsAny<ArgumentException>(() => CallbackData.Create("demo", "act", payload));
    }

    [Theory]
    [InlineData("Demo")]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("de_mo")]
    [InlineData("1demo")]
    [InlineData("demo:x")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void AC13_Create_InvalidModule_ThrowsArgumentException(string module) =>
        Assert.ThrowsAny<ArgumentException>(() => CallbackData.Create(module, "act", "1"));

    [Theory]
    [InlineData("Act")]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void AC13_Create_InvalidAction_ThrowsArgumentException(string action) =>
        Assert.ThrowsAny<ArgumentException>(() => CallbackData.Create("demo", action, "1"));

    [Theory]
    [InlineData("demo:act:42", "demo", "act", "42")]
    [InlineData("demo:act:a:b", "demo", "act", "a:b")]
    [InlineData("demo:act", "demo", "act", "")]
    [InlineData("demo:act:", "demo", "act", "")]
    public void AC13_TryParse_Valid(string data, string module, string action, string payload)
    {
        Assert.True(CallbackData.TryParse(data, out var m, out var a, out var p));
        Assert.Equal((module, action, payload), (m, a, p));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nocolon")]
    [InlineData(":act")]
    [InlineData("demo:")]
    [InlineData("::")]
    public void AC13_TryParse_Invalid_ReturnsFalse(string? data) =>
        Assert.False(CallbackData.TryParse(data, out _, out _, out _));

    [Theory]
    [InlineData("demo", "act", "42")]
    [InlineData("demo", "act", "a:b:c")]
    [InlineData("demo", "act", "")]
    [InlineData("movienight", "vote_yes", "жж-1")]
    public void AC13_CreateThenTryParse_RoundTrips(string module, string action, string payload)
    {
        var data = CallbackData.Create(module, action, payload);

        Assert.True(CallbackData.TryParse(data, out var m, out var a, out var p));
        Assert.Equal((module, action, payload), (m, a, p));
    }
}
