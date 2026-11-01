using Akagi.Characters;
using Akagi.Data;
using Akagi.Graphs;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Akagi.Learning;

internal interface ITutorProvider
{
    /// <summary>
    /// The character teaching this user. A user without one gets Mio.
    /// </summary>
    public Task<Character> GetAsync(User user);
}

/// <summary>
/// A tutor is any character whose card carries the <see cref="Tag"/> tag, so another character can take the role by
/// uploading a card with it.
/// </summary>
internal class TutorProvider : ITutorProvider
{
    public const string Tag = "learning";
    public const string DefaultGraphName = "Mio";
    internal const string DefaultGraphResource = "Akagi.Learning.Mio.json";

    private readonly IDatabaseFactory _databaseFactory;
    private readonly ICharacterDatabase _characters;
    private readonly ILogger<TutorProvider> _logger;
    private readonly SemaphoreSlim _creating = new(1, 1);

    public TutorProvider(IDatabaseFactory databaseFactory, ICharacterDatabase characters, ILogger<TutorProvider> logger)
    {
        _databaseFactory = databaseFactory;
        _characters = characters;
        _logger = logger;
    }

    public static bool IsTutor(Character character) =>
        character.Card.Tags.Contains(Tag, StringComparer.OrdinalIgnoreCase);

    public async Task<Character> GetAsync(User user)
    {
        Character? tutor = await FindAsync(user);
        if (tutor != null)
        {
            return tutor;
        }

        // Two open pages must not create two tutors
        await _creating.WaitAsync();
        try
        {
            tutor = await FindAsync(user);
            if (tutor != null)
            {
                return tutor;
            }

            GraphLoader loader = new(_databaseFactory);
            loader.LoadGraph(GraphData.Parse(ReadDefaultGraph()));
            await loader.Create(user.Id!, DefaultGraphName);
            _logger.LogInformation("Created the default tutor for user {UserId}", user.Id);
        }
        finally
        {
            _creating.Release();
        }

        return await FindAsync(user) ?? throw new LearningException("The tutor could not be set up.");
    }

    internal static string ReadDefaultGraph()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultGraphResource)
            ?? throw new InvalidOperationException($"Embedded graph {DefaultGraphResource} is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private async Task<Character?> FindAsync(User user) =>
        (await _characters.GetCharactersForUser(user)).FirstOrDefault(IsTutor);
}
