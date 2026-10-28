using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.Groups;
using Akagi.Communication;
using Akagi.Communication.SocketComs;
using Akagi.Connectors;
using Akagi.Data;
using Akagi.Embeddings;
using Akagi.Flow;
using Akagi.Life;
using Akagi.LLMs;
using Akagi.Receivers;
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

namespace Akagi.Tests.Live;

/// <summary>
/// Runs only when AKAGI_LIVE_MONGO holds a connection string to a MongoDB that may be written to, e.g.
/// <c>docker run -d --rm -p 127.0.0.1:27099:27017 mongo:8.3</c> and <c>AKAGI_LIVE_MONGO=mongodb://127.0.0.1:27099</c>.
/// Each run uses its own database and drops it afterwards.
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
            User user = new() { Name = "Live", Username = "live", Valid = true, Admin = true };
            await databases.GetDatabase<IUserDatabase>().SaveDocumentAsync(user);
            await new GridFSBucket(mongo.GetDatabase(database)).UploadFromBytesAsync(ObjectId.Parse(AkagiImageId), "akagi.png", TinyPng);

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
            Bridge.Chat.Models.Character group = Assert.Single(listed.Characters, c => c.IsGroup);
            Bridge.Chat.Models.Character akagi = Assert.Single(listed.Characters, c => c.Name == "Akagi");
            Bridge.Chat.Models.Character sensei = Assert.Single(listed.Characters, c => c.Name == "Sensei");
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
            Assert.Equal(TinyPng, picture.Data);
            client.Send(new FileRequestTransmission { FileUrl = $"cards/{sensei.CardId}/image.png" });
            Assert.Equal("Card has no image.", (await client.WaitFor<FileResponseTransmission>()).Error);

            // Commands sent in the group chat get the group, as on Telegram
            client.Send(new SendTextMessageRequestTransmission { CharacterId = group.Id, Text = "/groupInfo" });
            TextMessageResponseTransmission info = await client.WaitFor<TextMessageResponseTransmission>(t => t.Message.Text.Contains("Group:"));
            Assert.Contains("Group: Companion", info.Message.Text);

            client.Send(new ConversationRequestTransmission { CharacterId = group.Id });
            ConversationResponseTransmission history = await client.WaitFor<ConversationResponseTransmission>();
            Assert.Equal(group.Id, history.CharacterId);

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
            Character hana = Assert.Single(await databases.GetDatabase<ICharacterDatabase>().GetCharactersForUser(user), c => c.Name == "Hana");
            Assert.False(string.IsNullOrEmpty(hana.Card.ImageId));
            Assert.Single(await databases.GetDatabase<ICharacterGroupDatabase>().GetGroupsForUser(user));
        }
        finally
        {
            await socketService.StopAsync(CancellationToken.None);
            await mongo.DropDatabaseAsync(database);
        }
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

        // The registrations scan every loaded assembly, which includes this one with its in-memory fakes
        Assembly tests = typeof(LiveCompanionTests).Assembly;
        foreach (ServiceDescriptor descriptor in services.Where(d => d.ImplementationType?.Assembly == tests).ToList())
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
