using Akagi.Bridge.Attributes;
using Akagi.Characters.Conversations;

namespace Akagi.Characters.CharacterBehaviors.Filters;

[GraphNode]
internal class CharacterOnlyFilterCompiler : FilterMessageCompiler
{
    protected override bool IsAllowed(Message message)
    {
        return message.CharacterId != null;
    }
}
