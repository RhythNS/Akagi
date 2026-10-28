using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;

namespace Akagi.Tests.Characters;

public class GroupSpeakerTests
{
    [Fact]
    public void PersonaGroup_IsShownAsItsPersona()
    {
        CharacterGroup group = new() { MemberCharacterIds = ["sensei", "akagi"] };

        string? speaker = GroupSpeaker.Select(group,
            new PersonaStrategy { PersonaCharacterId = "akagi" });

        Assert.Equal("akagi", speaker);
    }

    [Fact]
    public void OtherGroups_AreShownAsTheirFirstMember()
    {
        CharacterGroup group = new() { MemberCharacterIds = ["manager", "writer"] };

        Assert.Equal("manager", GroupSpeaker.Select(group, null));
        Assert.Equal("manager", GroupSpeaker.Select(group,
            new PersonaStrategy { PersonaCharacterId = "not-a-member" }));
    }
}
