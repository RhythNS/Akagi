using Akagi.Characters;
using Akagi.Characters.Companions;
using Akagi.Characters.Conversations;
using Akagi.Characters.Routines;
using Akagi.Characters.Upgrades;
using Akagi.Life;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;

namespace Akagi.Tests.Life;

public class CompanionStateTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ChangeOutfit_RemembersCurrentAndPrevious()
    {
        LifeTestContext test = new();

        string first = await test.Run<ChangeOutfitCommand>(("Description", "cropped pink jumper, mid-thigh skirt, thigh-high socks"));
        string second = await test.Run<ChangeOutfitCommand>(("Description", "oversized hoodie and fluffy socks"), ("Reason", "movie night"));

        CompanionState state = test.Companions.Items.Single();
        Assert.Contains("Now wearing", first);
        Assert.Contains("Changed from 'cropped pink jumper", second);
        Assert.Equal("oversized hoodie and fluffy socks", state.Outfit!.Description);
        Assert.Equal("movie night", state.Outfit.Reason);
        Assert.Single(state.OutfitHistory);
        Assert.Equal(test.Character.Id, state.CharacterId);
    }

    [Theory]
    [InlineData("netflix", "Movie")]
    [InlineData("co-work", "CoWork")]
    [InlineData("Anime", "Anime")]
    [InlineData("sleep", "Bedtime")]
    [InlineData("karaoke", "Other")]
    public void ParseMode_UnderstandsCommonWords(string text, string expected)
    {
        Assert.Equal(expected, CompanionCommand.ParseMode(text)!.Value.ToString());
    }

    [Fact]
    public async Task Activities_StartReplaceAndEndWithSummary()
    {
        LifeTestContext test = new();

        string movie = await test.Run<StartActivityCommand>(("Mode", "anime"), ("Title", "Frieren episode 12"));
        string cowork = await test.Run<StartActivityCommand>(("Mode", "cowork"), ("Title", "thesis"));
        string ended = await test.Run<EndActivityCommand>(("Summary", "he finished chapter 3"));

        CompanionState state = test.Companions.Items.Single();
        Assert.Contains("no spoilers", movie);
        Assert.StartsWith("Ended anime.", cowork);
        Assert.Contains("quiet company", cowork);
        Assert.Contains("Ended cowork (thesis)", ended);
        Assert.Null(state.Activity);
        Assert.Equal(["Frieren episode 12", "thesis"], state.ActivityHistory.Select(a => a.Title));
        Assert.Equal("he finished chapter 3", state.ActivityHistory[^1].Summary);
        Assert.Contains("not doing anything", await test.Run<EndActivityCommand>());
    }

    [Fact]
    public void Activity_ForgottenOnesStopCounting()
    {
        CompanionState state = new();
        state.StartActivity(ActivityMode.Movie, "Your Name", string.Empty, Now);

        Assert.NotNull(state.RunningActivity(Now.AddHours(2)));
        Assert.Null(state.RunningActivity(Now.AddHours(5)));

        SharedActivity ended = state.EndActivity(string.Empty, Now.AddDays(1))!;
        Assert.Equal(Now.AddHours(4), ended.EndedAt);
    }

    [Fact]
    public async Task RememberPerson_MergesMentions()
    {
        LifeTestContext test = new();

        await test.Run<RememberPersonCommand>(("Name", "Yuki"), ("Relation", "Japanese friend from Osaka"), ("Gender", "female"), ("Note", "calls him every evening"));
        string again = await test.Run<RememberPersonCommand>(("Name", "yuki"), ("Note", "has an exam on Friday"));

        Person person = test.Companions.Items.Single().People.Single();
        Assert.Equal(2, person.Mentions);
        Assert.Equal(PersonGender.Female, person.Gender);
        Assert.Equal("Japanese friend from Osaka", person.Relation);
        Assert.Equal(["calls him every evening", "has an exam on Friday"], person.Notes);
        Assert.Contains("mentioned 2 times", again);
    }

    [Fact]
    public void Render_ShowsOutfitActivityPeopleAndUpgrades()
    {
        CompanionState state = new();
        state.ChangeOutfit("cropped pink jumper", string.Empty, Now.AddHours(-3));
        state.StartActivity(ActivityMode.CoWork, "thesis", string.Empty, Now.AddMinutes(-40));
        state.RememberPerson("Yuki", "Japanese friend", PersonGender.Female, "great at karaoke", Now.AddHours(-1));
        state.RememberPerson("Mika", "sister", PersonGender.Female, string.Empty, Now.AddHours(-1));
        state.RememberPerson("Kenta", "coworker", PersonGender.Male, string.Empty, Now.AddHours(-1));
        List<UpgradeProposal> upgrades =
        [
            new() { Title = "Wardrobe module", Status = UpgradeStatus.Done, UpdatedAt = Now.AddDays(-2) },
            new() { Title = "Movie sync", Status = UpgradeStatus.Proposed, Priority = UpgradePriority.High }
        ];

        string text = CompanionContextRenderer.Render(state, upgrades, null, Now, CompanionSections.All);

        Assert.Contains("You are wearing: cropped pink jumper", text);
        Assert.Contains("co-working (thesis) together, for 40m", text);
        Assert.Contains("- Yuki, Japanese friend (mentioned 1x", text);
        Assert.Contains("He mentioned Yuki recently", text);
        Assert.DoesNotContain("Mika recently", text);
        Assert.DoesNotContain("Kenta recently", text);
        Assert.Contains("never discourage real friendships", text);
        Assert.Contains("built into you recently: Wardrobe module", text);
        Assert.Contains("open upgrade requests (1): Movie sync", text);
    }

    [Fact]
    public void Render_RespectsSectionsAndOldMentions()
    {
        CompanionState state = new();
        state.RememberPerson("Yuki", "friend", PersonGender.Female, string.Empty, Now.AddDays(-3));
        state.ChangeOutfit("dress", string.Empty, Now);

        string text = CompanionContextRenderer.Render(state, [], null, Now, CompanionSections.People);

        Assert.DoesNotContain("wearing", text);
        Assert.Contains("Yuki", text);
        Assert.DoesNotContain("recently", text);
        Assert.Equal(string.Empty, CompanionContextRenderer.Render(null, [], null, Now, CompanionSections.All));
    }

    [Fact]
    public async Task Routines_StayQuietDuringImmersiveActivitiesExceptScheduledCheckIns()
    {
        InMemoryCharacterDatabase characters = new();
        InMemoryUserDatabase users = new();
        InMemoryRoutineDatabase routines = new();
        InMemoryCompanionStateDatabase companions = new();
        User user = new() { Id = ObjectId.GenerateNewId().ToString() };
        await users.SaveDocumentAsync(user);
        InactivityRoutine silence = new() { AfterHours = 1, SkipWhenUserActiveMinutes = 0, QuietHoursStart = "00:00", QuietHoursEnd = "00:00" };
        await routines.SaveDocumentAsync(silence);
        Character character = new() { Name = "Akagi", UserId = user.Id!, RoutineIds = [silence.Id!] };
        character.Conversations.GetCurrentConversation().AddMessage(new TextMessage { From = Message.Type.User, Text = "hi", Time = Now.AddHours(-3), CharacterId = null });
        await characters.SaveDocumentAsync(character);
        CompanionState state = await companions.GetOrCreate(user.Id!, character.Id!);
        state.StartActivity(ActivityMode.Movie, "Perfect Blue", string.Empty, Now.AddMinutes(-30));

        List<string> delivered = [];
        RoutineRunner runner = new(characters, users, routines, new InMemoryLifeProfileDatabase(),
            (_, _, m) => { delivered.Add(((TextMessage)m).Text); return Task.CompletedTask; }, NullLogger.Instance, companions);

        Assert.Equal(0, await runner.TickAsync(Now));

        OneTimeRoutine checkIn = new() { DueUtc = Now.AddMinutes(-1), OwnerCharacterId = character.Id!, Instruction = "remind him to take his medicine" };
        await routines.SaveDocumentAsync(checkIn);
        Assert.Equal(1, await runner.TickAsync(Now));
        Assert.Contains("medicine", Assert.Single(delivered));

        state.EndActivity(string.Empty, Now);
        Assert.Equal(1, await runner.TickAsync(Now.AddMinutes(1)));
    }
}
