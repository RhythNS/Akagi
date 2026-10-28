using Akagi.Web.Models.Chat;
using Akagi.Web.Pages.Chat.Components;
using Bunit;
using TabBlazor;

namespace Akagi.Web.Tests;

/// <summary>
/// Character and group pictures in the chat: groups show their speaker's card, and cards without a picture show an icon.
/// </summary>
public class CharacterAvatarTests : BunitContext
{
    public CharacterAvatarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static Character Companion(bool hasImage) => new()
    {
        Id = "group1",
        Name = "Companion",
        CardId = "card-akagi",
        IsGroup = true,
        MemberIds = ["akagi", "sensei"],
        HasImage = hasImage
    };

    [Fact]
    public void CardWithPicture_LoadsItThroughTheSocket()
    {
        IRenderedComponent<CharacterAvatar> avatar = Render<CharacterAvatar>(p => p
            .Add(c => c.Character, Companion(hasImage: true))
            .Add(c => c.CircuitId, "circuit"));

        Assert.Contains("/socket/cards/card-akagi/image.png?circuitId=circuit", avatar.Markup);
    }

    [Fact]
    public void CardWithoutPicture_ShowsAnIconInsteadOfABrokenImage()
    {
        IRenderedComponent<CharacterAvatar> avatar = Render<CharacterAvatar>(p => p
            .Add(c => c.Character, Companion(hasImage: false))
            .Add(c => c.Size, AvatarSize.Small));

        Assert.DoesNotContain("/socket/cards/", avatar.Markup);
        Assert.Contains("<svg", avatar.Markup);
    }

    [Fact]
    public void Group_OwnsMessagesFromItsMembers()
    {
        Character group = Companion(hasImage: true);

        Assert.True(group.Owns("group1"));
        Assert.True(group.Owns("AKAGI"));
        Assert.False(group.Owns("someone-else"));
    }

    [Fact]
    public void Character_OwnsOnlyItsOwnMessages()
    {
        Character akagi = new() { Id = "akagi", MemberIds = ["ignored"] };

        Assert.True(akagi.Owns("akagi"));
        Assert.False(akagi.Owns("ignored"));
    }

    [Fact]
    public void BridgeGroup_KeepsMembersAndPictureFlag()
    {
        Character group = Character.FromBridge(new Bridge.Chat.Models.Character
        {
            Id = "group1",
            Name = "Companion",
            CardId = "card-akagi",
            IsGroup = true,
            MemberIds = ["akagi"],
            HasImage = true
        });

        Assert.True(group.IsGroup);
        Assert.Equal(["akagi"], group.MemberIds);
        Assert.True(group.HasImage);
    }
}
