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

    protected override Message[] GetInjectionMessages(Context context)
    {
        Memory memory = context.Character.Memory;
        switch (Mode)
        {
            case RenderMode.Single:
                StringBuilder sb = new();

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddCollectionToSb(sb, memory.LongTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddCollectionToSb(sb, memory.ShortTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddCollectionToSb(sb, memory.Goals, Indexed);
                }

                TextMessage text = new()
                {
                    From = MessageType,
                    Time = DateTime.Now,
                    Text = sb.ToString(),
                };

                return [text];

            case RenderMode.PerCollection:
                List<Message> messages = [];

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddCollectionToList(messages, memory.LongTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddCollectionToList(messages, memory.ShortTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddCollectionToList(messages, memory.Goals, Indexed);
                }

                return [.. messages];

            case RenderMode.PerThought:
                List<Message> thoughtMessages = [];

                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    AddThoughtsToList(thoughtMessages, memory.LongTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    AddThoughtsToList(thoughtMessages, memory.ShortTerm, Indexed);
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    AddThoughtsToList(thoughtMessages, memory.Goals, Indexed);
                }

                return [.. thoughtMessages];

            default:
                throw new ArgumentException($"Unknown Render mode: {Mode}");
        }
    }

    private void AddCollectionToSb(StringBuilder sb, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed)
    {
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            if (indexed)
            {
                sb.AppendLine($"{i}: {thought}");
            }
            else
            {
                sb.AppendLine(thought.ToString());
            }
        }
    }

    private void AddCollectionToList(List<Message> messages, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed)
    {
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            StringBuilder sb = new();
            if (indexed)
            {
                sb.AppendLine($"{i}: {thought}");
            }
            else
            {
                sb.AppendLine(thought.ToString());
            }
            messages.Add(new TextMessage()
            {
                From = MessageType,
                Time = DateTime.Now,
                Text = sb.ToString(),
            });
        }
    }

    private void AddThoughtsToList(List<Message> messages, ThoughtCollection<SingleFactThought> thoughtCollection, bool indexed)
    {
        for (int i = 0; i < thoughtCollection.Thoughts.Count; i++)
        {
            SingleFactThought thought = thoughtCollection.Thoughts[i];
            StringBuilder sb = new();
            if (indexed)
            {
                sb.AppendLine($"{i}: {thought}");
            }
            else
            {
                sb.AppendLine(thought.ToString());
            }
            messages.Add(new TextMessage()
            {
                From = MessageType,
                Time = DateTime.Now,
                Text = sb.ToString(),
            });
        }
    }
}
