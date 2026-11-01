using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Communication.SocketComs;
using Akagi.Connectors;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.Flow;
using Akagi.Learning;
using Akagi.Life;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Receivers.Commands.Messages;
using Akagi.Scheduling;
using Akagi.TTSs;
using Akagi.Users;
using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Character = Akagi.Characters.Character;
using Command = Akagi.Receivers.Commands.Command;

namespace Akagi.Tests.Live;

/// <summary>
/// Runs only when AKAGI_LIVE_MONGO holds a connection string to a MongoDB that may be written to, e.g.
/// <c>docker run -d --rm -p 127.0.0.1:27099:27017 mongo:8.3</c> and <c>AKAGI_LIVE_MONGO=mongodb://127.0.0.1:27099</c>.
/// Each run uses its own database and drops it afterwards. To try a copy of a real database instead, restore it into
/// that server and set AKAGI_LIVE_DATABASE to its name and AKAGI_LIVE_USER_ID to the user to upload as; the copy is kept.
/// </summary>
public sealed class LiveMongoFactAttribute : FactAttribute
{
    public LiveMongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AKAGI_LIVE_MONGO")))
        {
            Skip = "Set AKAGI_LIVE_MONGO to a writable MongoDB to run live tests.";
        }
    }
}

/// <summary>
/// The companion bundle as Akagi.Web uses it: uploaded over the socket into a real MongoDB, listed as a group with
/// Akagi's picture, and driven with group commands. No LLM is called.
/// </summary>
public class LiveCompanionTests
{
    // The picture of Akagi the companion bundle's card points to
    private const string AkagiImageId = "6adcc2df4f351a3450e30ad3";
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [LiveMongoFact]
    public async Task CompanionBundle_UploadsOverTheSocketAndShowsAsAGroupWithAkagisPicture()
    {
        string? copiedDatabase = Environment.GetEnvironmentVariable("AKAGI_LIVE_DATABASE");
        string database = copiedDatabase ?? "AkagiLive_" + Guid.NewGuid().ToString("N")[..8];
        string connectionString = Environment.GetEnvironmentVariable("AKAGI_LIVE_MONGO")!.TrimEnd('/') + "/" + database;
        int port = FreePort();
        MongoClient mongo = new(connectionString);

        await using ServiceProvider services = BuildServices(connectionString, port);
        _ = services.GetRequiredService<Globals>();
        SocketService socketService = services.GetRequiredService<SocketService>();
        await socketService.StartAsync(CancellationToken.None);
        try
        {
            IDatabaseFactory databases = services.GetRequiredService<IDatabaseFactory>();
            User user = await LiveUser(databases.GetDatabase<IUserDatabase>());
            GridFSBucket files = new(mongo.GetDatabase(database));
            if (!await (await files.FindAsync(Builders<GridFSFileInfo>.Filter.Eq("_id", ObjectId.Parse(AkagiImageId)))).AnyAsync())
            {
                await files.UploadFromBytesAsync(ObjectId.Parse(AkagiImageId), "akagi.png", TinyPng);
            }
            byte[] akagiPicture = await files.DownloadAsBytesAsync(ObjectId.Parse(AkagiImageId));
            // A copied database may already hold a Companion group of this user
            HashSet<string> earlierGroups = [.. (await databases.GetDatabase<ICharacterGroupDatabase>().GetGroupsForUser(user)).Select(g => g.Id!)];

            using LiveClient client = await LiveClient.ConnectAsync(port);
            await LoginAs(socketService, user);

            // Upload the bundle exactly as the web's file upload sends it
            byte[] bundle = await File.ReadAllBytesAsync(Path.Combine(CompanionDirectory(), "Companion.bundle.json"));
            client.Send(new SendFileMessageRequestTransmission
            {
                CharacterId = "0",
                Text = "/uploadGraph create live",
                FileName = "Companion.bundle.json",
                FileType = "application/json",
                FileData = bundle
            });
            TextMessageResponseTransmission uploaded = await client.WaitFor<TextMessageResponseTransmission>(t => t.Message.Text.Contains("graphs") || t.Message.Text.Contains("Failed"));
            Assert.Contains("Successfully loaded graphs", uploaded.Message.Text);
            Assert.DoesNotContain("Failed to load", uploaded.Message.Text);

            // The group is listed next to its members, pictured with Akagi's card
            client.Send(new CharacterRequestTransmission { Ids = [] });
            CharacterResponseTransmission listed = await client.WaitFor<CharacterResponseTransmission>();
            Bridge.Chat.Models.Character group = Assert.Single(listed.Characters, c => c.IsGroup && c.Name == "Companion" && !earlierGroups.Contains(c.Id));
            Bridge.Chat.Models.Character akagi = Assert.Single(listed.Characters, c => c.Name == "Akagi" && group.MemberIds.Contains(c.Id));
            Bridge.Chat.Models.Character sensei = Assert.Single(listed.Characters, c => c.Name == "Sensei" && group.MemberIds.Contains(c.Id));
            Assert.Equal("Companion", group.Name);
            Assert.Equal(4, group.MemberIds.Length);
            Assert.Contains(akagi.Id, group.MemberIds);
            Assert.Equal(akagi.CardId, group.CardId);
            Assert.True(group.HasImage);
            Assert.False(sensei.HasImage);

            // The picture is served through the socket like /socket/cards/{id}/image.png
            client.Send(new FileRequestTransmission { FileUrl = $"cards/{group.CardId}/image.png" });
            FileResponseTransmission picture = await client.WaitFor<FileResponseTransmission>();
            Assert.Null(picture.Error);
            Assert.Equal(akagiPicture, picture.Data);
            client.Send(new FileRequestTransmission { FileUrl = $"cards/{sensei.CardId}/image.png" });
            Assert.Equal("Card has no image.", (await client.WaitFor<FileResponseTransmission>()).Error);

            // Commands sent in the group chat get the group, as on Telegram
            client.Send(new SendTextMessageRequestTransmission { CharacterId = group.Id, Text = "/groupInfo" });
            TextMessageResponseTransmission info = await client.WaitFor<TextMessageResponseTransmission>(t => t.Message.Text.Contains("Group:"));
            Assert.Contains("Group: Companion", info.Message.Text);

            // A plain chat message reaches the group; Akagi answers under her own id, which the group chat shows
            client.Send(new SendTextMessageRequestTransmission { CharacterId = group.Id, Text = "Hi Akagi, how was your day?" });
            TextMessageResponseTransmission reply = await client.WaitFor<TextMessageResponseTransmission>(t => t.Message.Text == ScriptedLLM.Reply);
            Assert.Equal(akagi.Id, reply.CharacterId);
            Assert.Contains(reply.CharacterId, group.MemberIds);

            client.Send(new ConversationRequestTransmission { CharacterId = group.Id });
            ConversationResponseTransmission history = await client.WaitFor<ConversationResponseTransmission>();
            Assert.Equal(group.Id, history.CharacterId);
            Bridge.Chat.Models.Conversation current = Assert.Single(history.Conversations);
            Assert.Contains(current.Messages, m => m.Text == "Hi Akagi, how was your day?");
            Assert.Contains(current.Messages, m => m.Text == ScriptedLLM.Reply);

            // A member's picture can be set from the group chat
            client.Send(new SendFileMessageRequestTransmission
            {
                CharacterId = group.Id,
                Text = "/cardImage Hana",
                FileName = "hana.png",
                FileType = "image/png",
                FileData = TinyPng
            });
            TextMessageResponseTransmission set = await client.WaitFor<TextMessageResponseTransmission>(t => t.Message.Text.Contains("picture"));
            Assert.Contains("Set the picture of Hana's card", set.Message.Text);
            Character hana = Assert.Single(await databases.GetDatabase<ICharacterDatabase>().GetCharactersForUser(user), c => c.Name == "Hana" && group.MemberIds.Contains(c.Id!));
            Assert.False(string.IsNullOrEmpty(hana.Card.ImageId));
            Assert.Single(await databases.GetDatabase<ICharacterGroupDatabase>().GetGroupsForUser(user), g => g.Id == group.Id);
        }
        finally
        {
            await socketService.StopAsync(CancellationToken.None);
            if (copiedDatabase == null)
            {
                await mongo.DropDatabaseAsync(database);
            }
        }
    }

    /// <summary>
    /// A lesson as the Learn page drives it: the tutor appears on first use, lessons are stored in MongoDB and run over
    /// the socket, and the tutor can be chatted with afterwards. The questions come from the scripted LLM through the
    /// real tools.
    /// </summary>
    [LiveMongoFact]
    public async Task Lesson_RunsOverTheSocketWithATutorCreatedOnFirstUse()
    {
        string database = "AkagiLive_" + Guid.NewGuid().ToString("N")[..8];
        string connectionString = Environment.GetEnvironmentVariable("AKAGI_LIVE_MONGO")!.TrimEnd('/') + "/" + database;
        int port = FreePort();
        MongoClient mongo = new(connectionString);

        await using ServiceProvider services = BuildServices(connectionString, port);
        _ = services.GetRequiredService<Globals>();
        SocketService socketService = services.GetRequiredService<SocketService>();
        await socketService.StartAsync(CancellationToken.None);
        try
        {
            IDatabaseFactory databases = services.GetRequiredService<IDatabaseFactory>();
            User user = new() { Name = "Live", Username = "live", Valid = true };
            await databases.GetDatabase<IUserDatabase>().SaveDocumentAsync(user);

            using LiveClient client = await LiveClient.ConnectAsync(port);
            await LoginAs(socketService, user);

            async Task<LearningResponseTransmission> Ask(LearningRequestTransmission request)
            {
                request.RequestId = Guid.NewGuid().ToString("N");
                client.Send(request);
                LearningResponseTransmission response = await client.WaitFor<LearningResponseTransmission>(r => r.RequestId == request.RequestId);
                Assert.Null(response.Error);
                return response;
            }

            LearningOverview overview = (await Ask(new LearningRequestTransmission { Action = LearningAction.Overview })).Overview!;
            Assert.Equal("Mio", overview.Tutor.Name);
            Assert.Empty(overview.Struggles);
            Assert.Equal(overview.Tutor.Id, (await Ask(new LearningRequestTransmission { Action = LearningAction.Overview })).Overview!.Tutor.Id);

            client.Send(new CharacterRequestTransmission { Ids = [] });
            Bridge.Chat.Models.Character listed = Assert.Single((await client.WaitFor<CharacterResponseTransmission>()).Characters);
            Assert.True(listed.IsTutor);
            Assert.Equal(overview.Tutor.Id, listed.Id);

            // Four answers: graded on the server, answers hidden until then
            LearningLesson lesson = (await Ask(new LearningRequestTransmission
            {
                Action = LearningAction.Start,
                Mode = LessonMode.MultipleChoice,
                Topic = "Rust lifetimes",
                QuestionCount = 2
            })).Lesson!;
            Assert.Equal(LessonStatus.Active, lesson.Status);
            Assert.Equal(ScriptedLLM.LessonIntro, lesson.Intro);
            Assert.Equal(["First?", "Second?"], lesson.Questions.Select(q => q.Prompt));
            Assert.All(lesson.Questions, q => Assert.Equal(4, q.Options.Length));
            Assert.All(lesson.Questions, q => Assert.Null(q.CorrectOption));

            int right = Array.IndexOf(lesson.Questions[0].Options, "right");
            lesson = (await Ask(new LearningRequestTransmission { Action = LearningAction.Answer, LessonId = lesson.Id, QuestionIndex = 0, ChosenOption = right })).Lesson!;
            Assert.Equal(LessonVerdict.Correct, lesson.Questions[0].Verdict);
            int wrong = Array.FindIndex(lesson.Questions[1].Options, o => o != "right");
            lesson = (await Ask(new LearningRequestTransmission { Action = LearningAction.Answer, LessonId = lesson.Id, QuestionIndex = 1, ChosenOption = wrong })).Lesson!;
            Assert.Equal(LessonVerdict.Wrong, lesson.Questions[1].Verdict);

            // The running lesson survives a page reload
            Assert.Equal(lesson.Id, (await Ask(new LearningRequestTransmission { Action = LearningAction.Overview })).Overview!.Current!.Id);

            lesson = (await Ask(new LearningRequestTransmission { Action = LearningAction.Finish, LessonId = lesson.Id })).Lesson!;
            Assert.Equal(LessonStatus.Finished, lesson.Status);
            Assert.Equal(1, lesson.Score);
            Assert.Equal(ScriptedLLM.LessonClosing, lesson.Closing);

            Lesson stored = await databases.GetDatabase<ILessonDatabase>().GetDocumentByIdAsync(lesson.Id) ?? throw new InvalidOperationException("Lesson was not stored.");
            Assert.Equal(LessonStatus.Finished, stored.Status);
            Assert.Equal(LessonVerdict.Wrong, stored.Questions[1].Verdict);
            Assert.Equal(2, (await databases.GetDatabase<ILifeProfileDatabase>().GetOrCreate(user.Id!)).Study.Days.Single().Reviews);

            // What was missed comes back as a weak spot, and a typed lesson can work on it
            overview = (await Ask(new LearningRequestTransmission { Action = LearningAction.Overview })).Overview!;
            LearningStruggle struggle = Assert.Single(overview.Struggles);
            Assert.Equal(StruggleKind.Lesson, struggle.Kind);
            Assert.Single(overview.Recent);

            lesson = (await Ask(new LearningRequestTransmission
            {
                Action = LearningAction.Start,
                Mode = LessonMode.Typed,
                FocusIds = [struggle.Id],
                QuestionCount = 1
            })).Lesson!;
            lesson = (await Ask(new LearningRequestTransmission { Action = LearningAction.Answer, LessonId = lesson.Id, AnswerText = "my own words" })).Lesson!;
            Assert.Equal(LessonVerdict.Partial, lesson.Questions[0].Verdict);
            Assert.Equal(ScriptedLLM.LessonFeedback, lesson.Questions[0].Feedback);

            // The tutor is a normal chat partner, on a graph with the lesson injection
            client.Send(new SendTextMessageRequestTransmission { CharacterId = overview.Tutor.Id, Text = "Why was that wrong?" });
            StreamTextMessageResponseTransmission reply = await client.WaitFor<StreamTextMessageResponseTransmission>(t => t.IsComplete);
            Assert.Equal(overview.Tutor.Id, reply.CharacterId);
            Assert.Equal(ScriptedLLM.Reply, reply.FullText);
        }
        finally
        {
            await socketService.StopAsync(CancellationToken.None);
            await mongo.DropDatabaseAsync(database);
        }
    }

    private static async Task<User> LiveUser(IUserDatabase users)
    {
        string? id = Environment.GetEnvironmentVariable("AKAGI_LIVE_USER_ID");
        if (id != null)
        {
            return await users.GetDocumentByIdAsync(id) ?? throw new InvalidOperationException($"User {id} is not in the database.");
        }
        User user = new() { Name = "Live", Username = "live", Valid = true, Admin = true };
        await users.SaveDocumentAsync(user);
        return user;
    }

    private static ServiceProvider BuildServices(string connectionString, int port)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDB"] = connectionString,
                ["Socket:Ip"] = "127.0.0.1",
                ["Socket:Port"] = port.ToString(),
            })
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddUtils();
        services.AddConnectors(configuration);
        services.AddData(configuration);
        services.AddReceivers(configuration);
        services.AddCharacter();
        services.AddUsers();
        services.AddLLMs();
        services.AddEmbeddings(configuration);
        services.AddTTSs();
        services.AddCommunications(configuration);
        services.AddScheduling();
        services.AddLife(configuration);
        services.AddLearning();

        // Every model call gets the same scripted answer, so no paid API is used
        services.AddSingleton<ILLMFactory, ScriptedLLMFactory>();

        // The registrations scan every loaded assembly, which includes this one with its in-memory fakes
        Assembly tests = typeof(LiveCompanionTests).Assembly;
        foreach (ServiceDescriptor descriptor in services.Where(d => d.ImplementationType?.Assembly == tests && d.ServiceType != typeof(ILLMFactory)).ToList())
        {
            services.Remove(descriptor);
        }
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Stands in for the Google sign-in: the session that just connected belongs to <paramref name="user"/>.
    /// </summary>
    private static async Task LoginAs(SocketService service, User user)
    {
        FieldInfo field = typeof(SocketService).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ConcurrentBag<SocketSession> sessions = (ConcurrentBag<SocketSession>)field.GetValue(service)!;
        for (int i = 0; i < 50 && sessions.IsEmpty; i++)
        {
            await Task.Delay(100);
        }
        Assert.Single(sessions).User = user;
    }

    private static int FreePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CompanionDirectory()
    {
        // The test may be built outside the repository; AKAGI_REPO (or the working directory) points to it then
        DirectoryInfo? directory = null;
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.GetEnvironmentVariable("AKAGI_REPO") ?? Environment.CurrentDirectory })
        {
            directory = new(start);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Akagi.sln")))
            {
                directory = directory.Parent;
            }
            if (directory != null)
            {
                break;
            }
        }
        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Akagi.CharacterEditor", "Characters", "Companion");
    }

    private sealed class ScriptedLLMFactory : ILLMFactory
    {
        public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType) => Task.FromResult<ILLM>(new ScriptedLLM());
    }

    /// <summary>
    /// Answers every step with one text message, the way a model's plain reply is turned into a command.
    /// </summary>
    private sealed class ScriptedLLM : ILLM
    {
        public const string Reply = "Scripted reply from Akagi.";
        public const string LessonIntro = "Scripted intro.";
        public const string LessonFeedback = "Scripted feedback.";
        public const string LessonClosing = "Scripted closing.";

        public void SetModel(string model)
        {
        }

        public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context) =>
            Task.FromResult(Steps(systemProcessor, context));

        public async IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(SystemProcessor systemProcessor, Context context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new StreamCompleteEvent(Steps(systemProcessor, context), Reply);
        }

        private static T Call<T>(T command, SystemProcessor systemProcessor, Context context, params (string Name, string Value)[] values) where T : Command
        {
            Argument[] arguments = command.GetDefaultArguments();
            foreach ((string name, string value) in values)
            {
                arguments.Single(a => a.Name == name).Value = value;
            }
            command.Arguments = arguments;
            command.SetFrom(systemProcessor.Output, context.Character.Id!);
            return command;
        }

        private static AddLessonQuestionCommand Question(string prompt, SystemProcessor systemProcessor, Context context) =>
            Call(new AddLessonQuestionCommand(), systemProcessor, context, ("Concept", prompt), ("Question", prompt), ("Answer", "right"),
                ("Wrong1", "a"), ("Wrong2", "b"), ("Wrong3", "c"), ("Explanation", "Because."));

        private static Command[] Steps(SystemProcessor systemProcessor, Context context)
        {
            if (systemProcessor.CommandNames.Contains("AddLessonQuestion"))
            {
                return
                [
                    Question("First?", systemProcessor, context),
                    Question("Second?", systemProcessor, context),
                    Call(new SetLessonIntroCommand(), systemProcessor, context, ("Intro", LessonIntro))
                ];
            }
            if (systemProcessor.CommandNames.Contains("GradeLessonAnswer"))
            {
                return [Call(new GradeLessonAnswerCommand(), systemProcessor, context, ("Verdict", "Partial"), ("Feedback", LessonFeedback))];
            }
            if (systemProcessor.CommandNames.Contains("CloseLesson"))
            {
                return [Call(new CloseLessonCommand(), systemProcessor, context, ("Message", LessonClosing))];
            }

            TextMessageCommand text = new();
            text.SetMessage(Reply);
            text.SetFrom(systemProcessor.Output, context.Character.Id!);
            return [text];
        }
    }

    /// <summary>
    /// Speaks the socket protocol Akagi.Web uses: MessagePack wrappers split into packages.
    /// </summary>
    private sealed class LiveClient : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly TransmissionPackageBuilder _packages = new(8192);
        private readonly List<TransmissionWrapper> _received = [];
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _stop = new();

        private LiveClient(TcpClient tcp)
        {
            _tcp = tcp;
            _stream = tcp.GetStream();
            _ = Task.Run(ReadLoop);
        }

        public static async Task<LiveClient> ConnectAsync(int port)
        {
            TcpClient tcp = new();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            return new LiveClient(tcp);
        }

        public void Send<T>(T transmission) where T : Transmission
        {
            TransmissionWrapper wrapper = new()
            {
                MessageType = transmission.MessageType,
                Version = 1,
                Payload = MessagePackSerializer.Serialize(transmission)
            };
            foreach (byte[] package in _packages.Send(MessagePackSerializer.Serialize(wrapper)))
            {
                _stream.Write(package);
            }
        }

        public async Task<T> WaitFor<T>(Func<T, bool>? match = null) where T : Transmission
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                lock (_received)
                {
                    for (int i = 0; i < _received.Count; i++)
                    {
                        if (_received[i].MessageType != typeof(T).Name)
                        {
                            continue;
                        }
                        T transmission = MessagePackSerializer.Deserialize<T>(_received[i].Payload);
                        if (match == null || match(transmission))
                        {
                            _received.RemoveAt(i);
                            return transmission;
                        }
                    }
                }
                await _signal.WaitAsync(TimeSpan.FromMilliseconds(250));
            }
            string seen;
            lock (_received)
            {
                seen = string.Join(", ", _received.Select(r => r.MessageType));
            }
            throw new TimeoutException($"No {typeof(T).Name} arrived. Received: {seen}");
        }

        private async Task ReadLoop()
        {
            byte[] buffer = new byte[65536];
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        return;
                    }
                    (bool complete, byte[]? data) = _packages.Receive(buffer, 0, read);
                    while (complete)
                    {
                        lock (_received)
                        {
                            _received.Add(MessagePackSerializer.Deserialize<TransmissionWrapper>(data));
                        }
                        _signal.Release();
                        if (_packages.IsEmpty)
                        {
                            break;
                        }
                        (complete, data) = _packages.Receive([], 0, 0);
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                // Closed by Dispose
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _tcp.Dispose();
            _packages.Dispose();
        }
    }
}
