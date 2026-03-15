namespace Akagi.Characters.Memories;

internal class ConversationThought : CopyableThought<ConversationThought>
{
    private int _conversationId = -1;
    private string _shortSummary = string.Empty;
    private string _longSummary = string.Empty;

    public int ConversationId
    {
        get => _conversationId;
        set => SetProperty(ref _conversationId, value);
    }

    public string ShortSummary
    {
        get => _shortSummary;
        set => SetProperty(ref _shortSummary, value);
    }

    public string LongSummary
    {
        get => _longSummary;
        set => SetProperty(ref _longSummary, value);
    }

    public override ConversationThought Copy()
    {
        return new ConversationThought()
        {
            Timestamp = Timestamp,
            _conversationId = _conversationId,
            _shortSummary = _shortSummary,
            _longSummary = _longSummary,
        };
    }

    public override string ToString()
    {
        return $"ConversationThought(Timestamp={Timestamp}, ConversationId={ConversationId}, ShortSummary={ShortSummary}, LongSummary={LongSummary})";
    }
}
