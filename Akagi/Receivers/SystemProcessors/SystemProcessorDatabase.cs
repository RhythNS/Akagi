using Akagi.Data;
using Akagi.Receivers.Commands;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Receivers.SystemProcessors;

internal class SystemProcessorDatabase : Database<SystemProcessor>, ISystemProcessorDatabase
{
    private readonly ICommandFactory _commandFactory;

    public SystemProcessorDatabase(IOptionsMonitor<DatabaseOptions> options,
                                   ICommandFactory commandFactory) : base(options, "system_processor")
    {
        _commandFactory = commandFactory;
    }

    public override bool CanSave(Savable savable) => savable is SystemProcessor;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((SystemProcessor)savable);

    public async Task<SystemProcessor> GetSystemProcessor(string id)
    {
        SystemProcessor? systemProcessor = await GetDocumentByIdAsync(id);

        if (systemProcessor == null)
        {
            throw new Exception($"SystemProcessor with ID {id} not found.");
        }

        List<Command> commands = [];
        for (int i = 0; i < systemProcessor.CommandNames.Length; i++)
        {
            commands.Add(_commandFactory.Create(systemProcessor.CommandNames[i]));
        }
        systemProcessor.InitCommands([.. commands]);

        await systemProcessor.AfterLoad();

        return systemProcessor;
    }

    public async Task<SystemProcessor[]> GetSystemProcessor(string[] ids)
    {
        FilterDefinition<SystemProcessor> filter = Builders<SystemProcessor>.Filter.In(sp => sp.Id, ids);
        List<SystemProcessor> systemProcessors = await GetDocumentsByPredicateAsync(filter);

        if (systemProcessors.Count != ids.Length)
        {
            throw new Exception($"Not all SystemProcessors with IDs {string.Join(", ", ids)} were found.");
        }

        foreach (SystemProcessor systemProcessor in systemProcessors)
        {
            List<Command> commands = [];
            for (int i = 0; i < systemProcessor.CommandNames.Length; i++)
            {
                commands.Add(_commandFactory.Create(systemProcessor.CommandNames[i]));
            }
            systemProcessor.InitCommands([.. commands]);
        }

        await Task.WhenAll(systemProcessors.Select(sp => sp.AfterLoad()));

        return [.. systemProcessors];
    }
}
