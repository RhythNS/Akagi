using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Learning;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace Akagi.Tests.Life;

public class ConversationMergerTests
{
    private const int Windows = 200;

    /// <summary>
    /// Characters as MAI-Transcribe returns them, 0.1 seconds each.
    /// </summary>
    private static List<TranscriptWord> Words(double start, string text) =>
        [.. text.Select((c, i) => new TranscriptWord(c.ToString(), start + i * 0.1, start + i * 0.1 + 0.08))];

    private static double[] Level(double value, double seconds = Windows * ConversationMerger.LoudnessWindowSeconds) =>
        [.. Enumerable.Repeat(value, (int)Math.Round(seconds / ConversationMerger.LoudnessWindowSeconds))];

    private static double[] Loud(double quiet, double loud, double from, double to)
    {
        double[] levels = Level(quiet);
        for (int i = (int)(from / ConversationMerger.LoudnessWindowSeconds); i < (int)(to / ConversationMerger.LoudnessWindowSeconds); i++)
        {
            levels[i] = loud;
        }
        return levels;
    }

    private static List<TranscriptSegment> Merge(ChannelTranscript me, ChannelTranscript partner, IReadOnlyList<TranscriptWord>? mix = null) =>
        ConversationMerger.Merge(me, partner, mix ?? [], "Me", "Yuki");

    [Fact]
    public void Merge_LabelsFromTheChannelAndOrdersByTime()
    {
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript(Words(4, "元気だよ。"), null),
            new ChannelTranscript(Words(1, "元気？").Concat(Words(7, "よかった。")).ToList(), null));

        Assert.Equal(["Yuki: 元気？", "Me: 元気だよ。", "Yuki: よかった。"], segments.Select(s => $"{s.Speaker}: {s.Text}"));
        Assert.Equal(4, segments[1].Start);
        Assert.Equal(4.48, segments[1].End, 2);
    }

    [Fact]
    public void Merge_APauseOrSentenceEndStartsANewPhrase()
    {
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript([.. Words(1, "まあ"), .. Words(3, "それで。"), .. Words(3.5, "次")], null),
            new ChannelTranscript([], null));

        Assert.Equal(["まあ", "それで。", "次"], segments.Select(s => s.Text));
    }

    [Fact]
    public void Merge_DropsTheOtherVoiceLeakingIntoASilentChannel()
    {
        // She speaks from 5 to 8; the left channel only has a trace of it at its noise floor
        double[] me = Loud(24, 1800, 1, 3);
        double[] partner = Loud(6, 550, 5, 8);

        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript([.. Words(1, "そうだね。"), .. Words(5.5, "お酒飲むから。")], me),
            new ChannelTranscript(Words(5.5, "お酒飲んでるから。"), partner));

        Assert.Equal(["Me: そうだね。", "Yuki: お酒飲んでるから。"], segments.Select(s => $"{s.Speaker}: {s.Text}"));
    }

    [Fact]
    public void Merge_DropsLeaksEvenWhenBothChannelsAreQuietThere()
    {
        // 家 at 5 s: nobody is loud at that moment, but it is far below how this channel normally sounds
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript([.. Words(1, "そうだね。"), .. Words(5.5, "家")], Loud(24, 1800, 1, 3)),
            new ChannelTranscript([], Level(50)));

        Assert.Equal("そうだね。", Assert.Single(segments).Text);
    }

    [Fact]
    public void Merge_KeepsQuietRealSpeechAndOverlaps()
    {
        // A soft backchannel while she talks over it
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript(Words(5.5, "うん。"), Loud(24, 300, 5, 6)),
            new ChannelTranscript(Words(5, "そうなんだよね。"), Loud(6, 900, 5, 6)));

        Assert.Equal(["Yuki", "Me"], segments.Select(s => s.Speaker));
    }

    [Fact]
    public void Merge_DropsPunctuationOnlyPhrases()
    {
        Assert.Empty(Merge(new ChannelTranscript(Words(1, "。"), null), new ChannelTranscript([], null)));
    }

    [Fact]
    public void Merge_WithoutLoudnessKeepsEverything()
    {
        Assert.Equal(2, Merge(new ChannelTranscript(Words(1, "一緒だ。"), null), new ChannelTranscript(Words(1, "一緒だ。"), null)).Count);
    }

    [Fact]
    public void SecondHearing_NotesWhatDiffers_WithAnAnchor()
    {
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript(Words(1, "ちょっと調べてみないね。"), null),
            new ChannelTranscript([], null),
            Words(1, "ちょっと調べてみるね。"));

        Assert.Equal("みない -> みる", Assert.Single(segments).Disputed);
        Assert.Equal("[00:00:01] Me: ちょっと調べてみないね。 (second hearing: みない -> みる)", TranscriptChunker.Format(segments[0]));
    }

    [Fact]
    public void SecondHearing_IsSilentWhenTheHearingsAgree()
    {
        TranscriptSegment segment = Assert.Single(Merge(
            new ChannelTranscript(Words(1, "ちょっと調べてみる。"), null),
            new ChannelTranscript([], null),
            Words(1, "ちょっと、調べてみる")));

        Assert.Equal("", segment.Disputed);
    }

    [Fact]
    public void SecondHearing_IgnoresTheOtherVoiceAndBackchannels()
    {
        List<TranscriptSegment> segments = Merge(
            new ChannelTranscript([.. Words(1, "調べてみる。"), .. Words(8, "うん。")], null),
            new ChannelTranscript([], null),
            [.. Words(0.8, "ねえ調べてみるよ"), .. Words(8, "ふーん。")]);

        Assert.All(segments, s => Assert.Equal("", s.Disputed));
    }

    [Fact]
    public void SecondHearing_SaysWhenTheMixHeardNothing()
    {
        TranscriptSegment segment = Assert.Single(Merge(
            new ChannelTranscript(Words(1, "全然聞こえない。"), null),
            new ChannelTranscript([], null),
            Words(50, "別の話。")));

        Assert.Equal("全然聞こえない -> (nothing)", segment.Disputed);
    }

    [Fact]
    public void Differences_ReadsLongRunsAndSkipsHeardOnlyWords()
    {
        Assert.Equal([("の地下は", "のちいかわ")], ConversationMerger.Differences("この地下は", "このちいかわ"));
        Assert.Empty(ConversationMerger.Differences("はい", "ねえはいそう"));
    }

    [Fact]
    public void Envelope_ReadsLittleEndianSamplesPerWindow()
    {
        byte[] samples = [0xE8, 0x03, 0xE8, 0x03, 0x18, 0xFC, 0x18, 0xFC, 0, 0, 0, 0, 7];

        Assert.Equal([1000.0, 1000.0, 0.0], AudioSplitter.Envelope(samples, 2));
    }

    [Fact]
    public async Task AudioSplitter_WithoutFfmpegThereIsNoLoudness()
    {
        Assert.Null(await new AudioSplitter("no-ffmpeg-here.exe").MeasureLoudnessAsync("x.mp3", CancellationToken.None));
    }

    [Fact]
    public void Instruction_ForBothVoices_AsksForComprehensionAndHedging()
    {
        CallJob job = new() { Partner = "Yuki", LearnerChannel = LearnerChannel.Left, LearnerSpeaker = "Me" };
        job.Segments = [new TranscriptSegment { Speaker = "Yuki", Text = "元気？" }];

        string text = ToolCallReviewer.BuildInstruction(job, null, DateTime.UtcNow);

        Assert.Contains("Lines labeled 'Me' are {{user}} and lines labeled 'Yuki' are Yuki", text);
        Assert.Contains("Category Comprehension", text);
        Assert.Contains("'She asked X and you answered as if she had said Y'", text);
        Assert.Contains("seems to have", text);
        Assert.Contains("'Possibly:'", text);
        Assert.Contains("in {{user}}'s lines only", text);
    }

    private sealed class OneResponseLLM : ILLM, ILLMFactory, ICommandFactory
    {
        public int Requests { get; private set; }
        public ILLM.LLMUsage? Usage { get; private set; }

        public void SetModel(string model)
        {
        }

        public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType)
        {
            Usage = usageType;
            return Task.FromResult<ILLM>(this);
        }

        public T Create<T>() where T : Command => throw new NotSupportedException();

        public Command Create(string commandName) => commandName switch
        {
            "NoteCallMistake" => new NoteCallMistakeCommand(),
            "NoteCallCorrectUse" => new NoteCallCorrectUseCommand(),
            "NoteCallStrength" => new NoteCallStrengthCommand(),
            "NoteCallPhrase" => new NoteCallPhraseCommand(),
            "NoteCallSummary" => new NoteCallSummaryCommand(),
            _ => new StopExecutionCommand()
        };

        private static T With<T>(T command, params (string Name, string Value)[] values) where T : Command
        {
            Argument[] arguments = command.GetDefaultArguments();
            foreach ((string name, string value) in values)
            {
                arguments.Single(a => a.Name == name).Value = value;
            }
            command.Arguments = arguments;
            return command;
        }

        public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
        {
            Requests++;
            return Task.FromResult<Command[]>(
            [
                With(new NoteCallMistakeCommand(), ("Category", "Comprehension"), ("Pattern", "heard ちいかわ as ちかわ"), ("Said", "ちかわ")),
                With(new NoteCallMistakeCommand(), ("Category", "misheard"), ("Pattern", "answered a different question"), ("Said", "ちかわ"), ("Severity", "3")),
                With(new NoteCallSummaryCommand(), ("Summary", "Talking about anime."))
            ]);
        }

        public IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(SystemProcessor systemProcessor, Context context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Analyzer_RecordsEveryFindingFromOneRequest()
    {
        InMemoryDatabaseFactory database = new();
        InMemoryUserDatabase users = new();
        InMemoryCharacterDatabase characters = new();
        InMemoryJapaneseDiagnosisDatabase diagnoses = new();
        database.Register(users);
        database.Register(characters);
        database.Register(diagnoses);
        database.Register(new InMemoryMessageCompilerDatabase());
        User user = new() { Name = "Tester" };
        await users.SaveDocumentAsync(user);
        Character sensei = new() { Name = "Sensei" };
        await characters.SaveDocumentAsync(sensei);
        OneResponseLLM llm = new();
        ToolCallReviewer analyzer = new(database, llm, llm, NullLogger.Instance);
        CallJob job = new() { UserId = user.Id!, AnalysisCharacterId = sensei.Id!, Partner = "Yuki" };

        ChunkReview review = await analyzer.AnalyzeAsync(job, new CallChunk { Text = "[00:00:01] Me: ちかわ。" }, null, CancellationToken.None);

        Assert.Equal(2, review.Findings.Count);
        Assert.All(review.Findings, f => Assert.Equal(IssueCategory.Comprehension, f.Category));
        Assert.Equal(3, review.Findings[1].Severity);
        Assert.Equal("Talking about anime.", review.Summary);
        Assert.Equal(1, llm.Requests);
        // Call reviews run on the model picked for Learning
        Assert.Equal(ILLM.LLMUsage.Learning, llm.Usage);
        // Nothing reaches the diagnosis before the quotes are checked and the call is consolidated
        Assert.Empty(diagnoses.Items);
    }

    [Fact]
    public void Instruction_KeepsDisputedWordsOutOfComprehensionEvidence()
    {
        CallJob job = new() { Partner = "Yuki", LearnerChannel = LearnerChannel.Left, LearnerSpeaker = "Me", Segments = [new TranscriptSegment { Speaker = "Yuki", Text = "元気？" }] };

        string text = ToolCallReviewer.BuildInstruction(job, null, DateTime.UtcNow);

        Assert.Contains("A disputed word alone never shows", text);
        Assert.Contains("first look for comprehension breakdowns", text);
        Assert.Contains("on top of the limit above", text);
        Assert.Contains("NoteCallPhrase", text);
        Assert.Contains("Make all your calls in this one response", text);
    }

    [Fact]
    public void Instruction_ForASingleVoice_StaysAsItWas()
    {
        CallJob stored = new() { LearnerChannel = LearnerChannel.Left, Segments = [new TranscriptSegment { Text = "はい" }] };

        string text = ToolCallReviewer.BuildInstruction(stored, null, DateTime.UtcNow);

        Assert.Contains("Only {{user}}'s own audio channel", text);
        Assert.DoesNotContain("Comprehension", text);
    }

    [Theory]
    [InlineData("Comprehension")]
    [InlineData("misheard")]
    [InlineData("listening")]
    public void Category_ReadsComprehension(string text)
    {
        Assert.Equal(IssueCategory.Comprehension, JapaneseCommand.ParseCategory(text));
    }

    [Fact]
    public void StoredCategories_KeepTheirNumbers()
    {
        Assert.Equal(11, (int)IssueCategory.Other);
        Assert.Equal(12, (int)IssueCategory.Comprehension);
    }

    [Fact]
    public void Labels_DefaultWithoutAPartnerName()
    {
        Assert.Equal("Partner", new CallJob().PartnerLabel);
    }
}
