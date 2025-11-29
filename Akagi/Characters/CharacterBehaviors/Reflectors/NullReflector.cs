namespace Akagi.Characters.CharacterBehaviors.Reflectors;

internal class NullReflector : Reflector
{
    public override Task ProcessAsync()
    {
        return Task.CompletedTask;
    }
}
