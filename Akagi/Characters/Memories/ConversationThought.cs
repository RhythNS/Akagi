namespace Akagi.Characters.Memories;

internal class ConversationThought : Thought
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
}
