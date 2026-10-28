using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.Groups;
using Akagi.Data;

namespace Akagi.Communication.Commands.ActiveCharacters;

/// <summary>
/// Sets the picture of a character's card from an attached image, so cards made in the editor (or embedded in a
/// graph bundle) get a picture without replacing the card itself.
/// </summary>
internal class CardImageCommand : DocumentCommand
{
    public override string Name => "/cardImage";

    public override string Description =>
        "Sets the picture of a character's card from an attached PNG, JPEG, GIF or WebP image. Without a name it changes the " +
        "current character, or the speaking character of the current group. Usage: /cardImage <image file> [character name...]";

    private readonly IFileDatabase _fileDatabase;

    public CardImageCommand(IFileDatabase fileDatabase)
    {
        _fileDatabase = fileDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, Document[] documents, string[] args)
    {
        // Telegram sends a photo in several sizes; the largest one is the picture
        (MemoryStream Stream, string ContentType)? image = null;
        foreach (Document document in documents)
        {
            MemoryStream? stream = await document.GetStream();
            string? contentType = stream == null ? null : ImageContentType(stream);
            if (stream == null || contentType == null || (image != null && image.Value.Stream.Length >= stream.Length))
            {
                stream?.Dispose();
                continue;
            }
            image?.Stream.Dispose();
            image = (stream, contentType);
        }
        if (image == null)
        {
            await Communicator.SendMessage(context.User, "Attach a PNG, JPEG, GIF or WebP image. Usage: /cardImage <image file> [character name]");
            return CommandResult.Fail("No image attached.");
        }

        using MemoryStream imageStream = image.Value.Stream;
        string name = string.Join(' ', args).Trim();
        (Character? character, string? problem) = await FindTarget(context, name);
        if (character == null)
        {
            await Communicator.SendMessage(context.User, problem!);
            return CommandResult.Fail(problem!);
        }

        ICardDatabase cards = context.DatabaseFactory.GetDatabase<ICardDatabase>();
        Card? card = await cards.GetDocumentByIdAsync(character.CardId);
        if (card == null)
        {
            await Communicator.SendMessage(context.User, $"{character.Name} has no card to put the picture on.");
            return CommandResult.Fail("Card not found.");
        }

        imageStream.Position = 0;
        string extension = image.Value.ContentType.Split('/')[1];
        // The old picture stays in the file store: graph cards share their placeholder picture
        card.ImageId = (await _fileDatabase.UploadFileAsync(imageStream, $"{Guid.NewGuid()}.{extension}", image.Value.ContentType)).ToString();
        await cards.SaveDocumentAsync(card);

        await Communicator.SendMessage(context.User, $"Set the picture of {character.Name}'s card ({card.Name}).");
        return CommandResult.Ok;
    }

    private static async Task<(Character? Character, string? Problem)> FindTarget(Context context, string name)
    {
        ICharacterDatabase characters = context.DatabaseFactory.GetDatabase<ICharacterDatabase>();
        List<Character> nearby = [];
        if (context.Character != null)
        {
            nearby.Add(context.Character);
        }
        string? speakerId = null;
        if (context.Group != null)
        {
            speakerId = await GroupSpeaker.GetIdAsync(context.Group, context.DatabaseFactory);
            foreach (string memberId in context.Group.MemberCharacterIds)
            {
                Character? member = await characters.GetCharacter(memberId);
                if (member != null)
                {
                    nearby.Add(member);
                }
            }
        }

        if (name.Length == 0)
        {
            Character? target = context.Character ?? nearby.FirstOrDefault(c => c.Id == speakerId) ?? nearby.FirstOrDefault();
            return target == null
                ? (null, "Open a character or group first, or name the character: /cardImage <image file> <character name>.")
                : (target, null);
        }

        Character? match = nearby.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return (match, null);
        }

        // Several uploads of a graph create characters with the same name, so only an unambiguous name is used
        List<Character> named = [.. (await characters.GetCharactersForUser(context.User))
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))];
        return named.Count switch
        {
            1 => (named[0], null),
            0 => (null, $"No character named '{name}'."),
            _ => (null, $"There are {named.Count} characters named '{name}'. Open the one you mean (or its group) and send /cardImage from there.")
        };
    }

    internal static string? ImageContentType(Stream stream)
    {
        byte[] header = new byte[12];
        stream.Position = 0;
        int read = stream.Read(header, 0, header.Length);
        stream.Position = 0;

        if (read >= 8 && header[0] == 0x89 && header[1] == 'P' && header[2] == 'N' && header[3] == 'G')
        {
            return "image/png";
        }
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (read >= 6 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8')
        {
            return "image/gif";
        }
        if (read >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return "image/webp";
        }
        return null;
    }
}
