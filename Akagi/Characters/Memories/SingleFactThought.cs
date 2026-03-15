namespace Akagi.Characters.Memories;

internal class SingleFactThought : CopyableThought<SingleFactThought>
{
    public string _fact = string.Empty;

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
            _fact = _fact,
        };
    }

    public override string ToString()
    {
        return $"SingleFactThought(Timestamp={Timestamp}, Fact={Fact})";
    }
}
