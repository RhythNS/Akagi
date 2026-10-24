namespace Akagi.Characters.Memories;

internal class SingleFactThought : CopyableThought<SingleFactThought>
{
    private string _fact = string.Empty;

    public string Fact
    {
        get => _fact;
        set => SetProperty(ref _fact, value);
    }

    public override SingleFactThought Copy()
    {
        return new SingleFactThought()
        {
            Timestamp = Timestamp,
            Fact = Fact,
        };
    }

    public override string ToString()
    {
        return Fact;
    }
}
