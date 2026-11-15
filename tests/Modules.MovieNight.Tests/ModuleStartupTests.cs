using System.Net;
using System.Reflection;
using Bot.Platform.Modules;
using Bot.Platform.Tests.Helpers;
using Modules.MovieNight.Tests.Helpers;

namespace Modules.MovieNight.Tests;

// TEST-2: AC-1, AC-1a
public class ModuleStartupTests
{
    [Fact]
    public async Task AC1_StartOnEmptyDb_MigrationAppliedTablesCreatedNoDefaultHistoryTable()
    {
        await using var factory = new MovieNightFactory();
        factory.CreateClient();

        foreach (var table in new[]
                 {
                     "movienight_groups", "movienight_group_members", "movienight_revoked_invite_codes",
                     "__ef_migrations_movienight",
                 })
        {
            Assert.Equal(1, factory.Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'"));
        }

        Assert.True(factory.Scalar("SELECT COUNT(*) FROM __ef_migrations_movienight") >= 1);
        Assert.Equal(0, factory.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'"));
    }

    [Fact]
    public async Task AC1_StartOnEmptyDb_MembersTableUsesAutoincrementAndUniqueIndexes()
    {
        await using var factory = new MovieNightFactory();
        factory.CreateClient();

        var ddl = factory.ScalarString("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'movienight_group_members'");
        Assert.Contains("AUTOINCREMENT", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.True(factory.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'movienight_groups' AND sql LIKE '%UNIQUE%' AND sql LIKE '%InviteCode%'") >= 1);
        Assert.True(factory.Scalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'movienight_group_members' AND sql LIKE '%UNIQUE%' AND sql LIKE '%GroupId%' AND sql LIKE '%UserId%'") >= 1);
    }

    [Fact]
    public async Task AC1_GroupsWithoutAuthorization_Returns401AuthMissing()
    {
        await using var factory = new MovieNightFactory();

        var response = await factory.GetAsync(null, "/api/movienight/groups");

        await ApiCall.AssertProblemAsync(response, 401, "auth_missing");
    }

    [Fact]
    public void AC1_ModuleAssembly_DoesNotReferenceHostOrOtherModules()
    {
        var self = typeof(MovieNightModule).Assembly.GetName().Name;
        var references = typeof(MovieNightModule).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain("Bot.Host", references);
        Assert.DoesNotContain(references, r => r.StartsWith("Modules.", StringComparison.Ordinal) && r != self);
    }

    [Fact]
    public void AC1a_ModuleName_IsMovienight()
    {
        Assert.Equal("movienight", new MovieNightModule().Name);
    }

    [Fact]
    public void AC1a_MapBot_IsNotOverriddenByModule()
    {
        var map = typeof(MovieNightModule).GetInterfaceMap(typeof(IBotModule));
        var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == nameof(IBotModule.MapBot));

        Assert.True(index >= 0);
        Assert.Equal(typeof(IBotModule), map.TargetMethods[index].DeclaringType);
    }
}
