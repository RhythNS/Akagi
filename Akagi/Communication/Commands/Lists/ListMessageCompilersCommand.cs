using Akagi.Characters.CharacterBehaviors.MessageCompilers;

namespace Akagi.Communication.Commands.Lists;

internal class ListMessageCompilersCommand : ListCommand
{
    public override string Name => "/listMessageCompilers";

    public override string Description => "Lists all message compilers. Usage: /listMessageCompilers";

    private readonly IMessageCompilerDatabase _messageCompilerDatabase;

    public ListMessageCompilersCommand(IMessageCompilerDatabase messageCompilerDatabase)
    {
        _messageCompilerDatabase = messageCompilerDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        List<MessageCompiler> compilers = await _messageCompilerDatabase.GetDocumentsAsync();
        if (compilers.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No message compilers found.");
            return;
        }
        string[] ids = [.. compilers.Select(c => c.Id!)];
        string[] names = [.. compilers.Select(c => c.Name)];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available compilers:\n{choices}");
    }
}
