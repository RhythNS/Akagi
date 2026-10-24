using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Receivers;
using System.Text;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;

internal class MemoryInjectionCompiler : InjectionCompiler
{
    [Flags]
    public enum MemoryCollections
    {
        Short = 1,
        Long = 2,
        Goals = 4,
    }

    public enum RenderMode
    {
        Single,
        PerCollection,
        PerThought,
    }

    private RenderMode _renderMode;
    private MemoryCollections _memoryCollections;
    private bool _indexed;

    public RenderMode Mode
    {
        get => _renderMode;
        set => SetProperty(ref _renderMode, value);
    }
    public MemoryCollections Collections
    {
        get => _memoryCollections;
        set => SetProperty(ref _memoryCollections, value);
    }
    public bool Indexed
    {
        get => _indexed;
        set => SetProperty(ref _indexed, value);
    }

    protected override Task<Message[]> GetInjectionMessagesAsync(Context context)
    {
        Memory memory = context.Character.Memory;
        switch (Mode)
        {
            case RenderMode.Single:
                StringBuilder sb = new();

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddCollectionToSb(sb, memory.LongTerm, Indexed, "Long-Term Memories");
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddCollectionToSb(sb, memory.ShortTerm, Indexed, "Short-Term Memories");
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddCollectionToSb(sb, memory.Goals, Indexed, "Current Goals");
                }

                if (sb.Length == 0)
                {
                    return Task.FromResult<Message[]>([]);
                }

                TextMessage text = new()
                {
                    From = MessageType,
                    Time = DateTime.UtcNow,
                    Text = sb.ToString().TrimEnd(),
                    CharacterId = context.Character.Id!,
                };

                return Task.FromResult<Message[]>([text]);

            case RenderMode.PerCollection:
                List<Message> messages = [];

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddCollectionToList(messages, memory.LongTerm, Indexed, context.Character.Id!, "Long-Term Memories");
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddCollectionToList(messages, memory.ShortTerm, Indexed, context.Character.Id!, "Short-Term Memories");
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddCollectionToList(messages, memory.Goals, Indexed, context.Character.Id!, "Current Goals");
                }

                return Task.FromResult<Message[]>([.. messages]);

            case RenderMode.PerThought:
                List<Message> thoughtMessages = [];

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddThoughtsToList(thoughtMessages, memory.LongTerm, Indexed, context.Character.Id!);
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddThoughtsToList(thoughtMessages, memory.ShortTerm, Indexed, context.Character.Id!);
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddThoughtsToList(thoughtMessages, memory.Goals, Indexed, context.Character.Id!);
                }

                return Task.FromResult<Message[]>([.. thoughtMessages]);

            default:
                throw new ArgumentException($"Unknown Render mode: {Mode}");
        }
    }

    private static string FormatThought(SingleFactThought thought, int? index)
    {
        string datePart = thought.Timestamp != DateTime.MinValue && thought.Timestamp != default
            ? $"({thought.Timestamp:yyyy-MM-dd}) "
            : string.Empty;

        return index.HasValue
            ? $"[{index.Value}] {datePart}{thought.Fact}"
            : $"- {datePart}{thought.Fact}";
    }

    private void AddCollectionToSb(StringBuilder sb, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed, string header)
    {
        if (thoughtCollection.Thoughts.Count == 0)
        {
            return;
        }

        sb.AppendLine($"### {header}:");
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            sb.AppendLine(FormatThought(thought, indexed ? i : null));
        }
        sb.AppendLine();
    }

    private void AddCollectionToList(List<Message> messages, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed, string characterId, string header)
    {
        if (thoughtCollection.Thoughts.Count == 0)
        {
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine($"### {header}:");
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            sb.AppendLine(FormatThought(thought, indexed ? i : null));
        }

        messages.Add(new TextMessage()
        {
            From = MessageType,
            Time = DateTime.UtcNow,
            Text = sb.ToString().TrimEnd(),
            CharacterId = characterId,
        });
    }

    private void AddThoughtsToList(List<Message> messages, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed, string characterId)
    {
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            messages.Add(new TextMessage()
            {
                From = MessageType,
                Time = DateTime.UtcNow,
                Text = FormatThought(thought, indexed ? i : null),
                CharacterId = characterId,
            });
        }
    }
}
