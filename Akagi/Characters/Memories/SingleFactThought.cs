namespace Akagi.Characters.Memories;

internal class SingleFactThought : Thought
{
    public string _fact = string.Empty;

    public string Fact
    {
        get => _fact;
        set => SetProperty(ref _fact, value);
    }
}
