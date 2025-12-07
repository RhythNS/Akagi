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
        Conversations = 4,
        Goals = 8,
    }

    public enum RenderMode
    {
        Single,
        Indexed
    }

    private RenderMode _renderMode;
    private MemoryCollections _memoryCollections;

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

    protected override Message[] GetInjectionMessages(Context context)
    {
        Memory memory = context.Character.Memory;
        switch (Mode)
        {
            case RenderMode.Single:
                StringBuilder sb = new();

                if (Collections.HasFlag(MemoryCollections.Conversations))
                {
                    Add(sb, memory.Conversations);
                }
                if (Collections.HasFlag(MemoryCollections.Long))
                {
                    Add(sb, memory.LongTerm);
                }
                if (Collections.HasFlag(MemoryCollections.Short))
                {
                    Add(sb, memory.ShortTerm);
                }
                if (Collections.HasFlag(MemoryCollections.Goals))
                {
                    Add(sb, memory.Goals);
                }

                TextMessage text = new()
                {
                    From = MessageType,
                    Time = DateTime.Now,
                    Text = sb.ToString(),
                };
                break;
            case RenderMode.Indexed:
                break;
            default:
                throw new ArgumentException($"Unknown Render mode: {Mode}");
        }
    }

    private void Add(StringBuilder sb, ThoughtCollection<SingleFactThought> thoughtCollection)
    {
        sb.AppendLine(string.Join(Environment.NewLine, thoughtCollection.Thoughts.Select(x => x.Fact)));
    }
}
