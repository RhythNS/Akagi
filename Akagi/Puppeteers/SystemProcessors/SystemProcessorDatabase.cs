using Akagi.Characters;
using Akagi.Data;
using Akagi.Puppeteers.Commands;
using Akagi.Users;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Akagi.Puppeteers.SystemProcessors;

internal class SystemProcessorDatabase : Database<SystemProcessor>, ISystemProcessorDatabase
{
    private readonly ICommandFactory _commandFactory;

    public SystemProcessorDatabase(IOptionsMonitor<DatabaseOptions> options,
        ICommandFactory commandFactory) : base(options, "SystemProcessor")
    {
        _commandFactory = commandFactory;
    }

    public async Task<SystemProcessor> GetSystemProcessor(User user, Character character)
    {
        SystemProcessor? systemProcessor = await GetDocumentByIdAsync(character.SystemProcessorId);

        if (systemProcessor == null)
        {
            throw new Exception($"SystemProcessor with ID {character.SystemProcessorId} not found.");
        }

        List<Command> commands = [];
        for (int i = 0; i < systemProcessor.CommandNames.Length; i++)
        {
            commands.Add(_commandFactory.Create(systemProcessor.CommandNames[i]));
        }
        systemProcessor.InitCommands([.. commands]);

        return systemProcessor;
    }

    public async Task<bool> SaveSystemProcessorFromFile(MemoryStream stream)
    {
        using StreamReader reader = new(stream);
        string json = reader.ReadToEnd();

        SystemProcessor? systemProcessor = null;
        try
        {
            systemProcessor = JsonSerializer.Deserialize<SystemProcessor>(json);
        }
        catch (Exception)
        {
            return false;
        }
        if (systemProcessor == null)
        {
            return false;
        }
        try
        {
            await SaveDocumentAsync(systemProcessor);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }
}
