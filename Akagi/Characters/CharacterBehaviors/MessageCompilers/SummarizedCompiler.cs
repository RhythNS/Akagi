using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class SummarizedCompiler : MessageCompiler
{
    private enum State
    {
        Recent,
        LongSummary,
        ShortSummary
    }

    private int _recentWordLimit;
    private int _longSummaryWordLimit;
    private int _shortSummaryWordLimit;
    private Message.Type _summaryFrom;

    public int RecentWordLimit
    {
        get => _recentWordLimit;
        set => SetProperty(ref _recentWordLimit, value);
    }
    public int LongSummaryWordLimit
    {
        get => _longSummaryWordLimit;
        set => SetProperty(ref _longSummaryWordLimit, value);
    }
    public int ShortSummaryWordLimit
    {
        get => _shortSummaryWordLimit;
        set => SetProperty(ref _shortSummaryWordLimit, value);
    }
    public Message.Type SummaryFrom
    {
        get => _summaryFrom;
        set => SetProperty(ref _summaryFrom, value);
    }

    private ILogger<SummarizedCompiler> _logger = null!;

    public override Task Init(Message.Type readableMessages, IMessageCompilerDatabase messageCompilerDatabase)
    {
        _logger = Globals.Instance.GetLogger<SummarizedCompiler>();

        return base.Init(readableMessages, messageCompilerDatabase);
    }

    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        List<Conversation> conversations = BuildSummarizedConversations(context, filteredConversations);

        conversations.Reverse();
        filteredConversations = conversations;
    }

    private List<Conversation> BuildSummarizedConversations(Context context, List<Conversation> filteredConversations)
    {
        State state = State.Recent;
        int currentCount = 0;
        int nextLimit = RecentWordLimit;

        List<Conversation> conversations = [];

        for (int i = filteredConversations.Count - 1; i >= 0; i--)
        {
            int words = filteredConversations[i].CountWords(ReadableMessages);
            if (nextLimit - words >= 0)
            {
                currentCount += words;
            }
            else
            {
                switch (state)
                {
                    case State.Recent:
                        state = State.LongSummary;
                        nextLimit = LongSummaryWordLimit;
                        break;

                    case State.LongSummary:
                        state = State.ShortSummary;
                        nextLimit = ShortSummaryWordLimit;
                        break;

                    case State.ShortSummary:
                        return conversations;

                    default:
                        throw new Exception($"Unknown state: {state}");
                }

                currentCount = 0;
            }

            if (state == State.Recent)
            {
                conversations.Add(filteredConversations[i]);
                continue;
            }

            int conversationId = filteredConversations[i].Id;
            ConversationThought? thought = context.Character.Memory.Conversations.Thoughts.FirstOrDefault(
                c => c.ConversationId == conversationId);

            if (thought == null)
            {
                _logger.LogWarning("No thought found for conversation ID {ConversationId}", conversationId);
                continue;
            }

            string? summary = null;
            switch (state)
            {
                case State.LongSummary:
                    summary = thought.LongSummary;
                    break;

                case State.ShortSummary:
                    summary = thought.ShortSummary;
                    break;
            }

            if (summary == null)
            {
                _logger.LogWarning("No summary found for conversation ID {ConversationId} in state {State}", conversationId, state);
                continue;
            }

            Conversation longConv = new()
            {
                Id = filteredConversations[i].Id,
                Time = filteredConversations[i].Time,
                IsCompleted = filteredConversations[i].IsCompleted,
                Messages =
                [
                    new TextMessage
                    {
                        From = SummaryFrom,
                        Text = summary,
                        Time = filteredConversations[i].Time
                    }
                ]
            };

            conversations.Add(longConv);
        }

        return conversations;
    }
}
