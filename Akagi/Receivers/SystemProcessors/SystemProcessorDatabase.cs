using Akagi.Data;
using Akagi.Flow;
using Akagi.Receivers.Commands;
using Akagi.Receivers.MessageCompilers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Receivers.SystemProcessors;

internal class SystemProcessorDatabase : Database<SystemProcessor>, ISystemProcessorDatabase
{
    private readonly ICommandFactory _commandFactory;
    private readonly IMessageCompilerDatabase _compilerDatabase;

    public SystemProcessorDatabase(IOptionsMonitor<DatabaseOptions> options,
                                   ICommandFactory commandFactory,
                                   IMessageCompilerDatabase compilerDatabase) : base(options, "system_processor")
    {
        _commandFactory = commandFactory;
        _compilerDatabase = compilerDatabase;
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

        MessageCompiler? messageCompiler = await _compilerDatabase.GetDocumentByIdAsync(systemProcessor.MessageCompilerId);

        if (messageCompiler == null)
        {
            throw new Exception($"MessageCompiler with ID {systemProcessor.MessageCompilerId} not found for SystemProcessor {id}.");
        }
        systemProcessor.MessageCompiler = messageCompiler;

        List<Command> commands = [];
        for (int i = 0; i < systemProcessor.CommandNames.Length; i++)
        {
            commands.Add(_commandFactory.Create(systemProcessor.CommandNames[i]));
        }

        IDatabaseFactory databaseFactory = Globals.Instance.ServiceProvider.GetService<IDatabaseFactory>()
            ?? throw new Exception("DatabaseFactory service not found.");

        await systemProcessor.Init([.. commands], databaseFactory);
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

        IDatabaseFactory databaseFactory = Globals.Instance.ServiceProvider.GetService<IDatabaseFactory>()
            ?? throw new Exception("DatabaseFactory service not found.");

        foreach (SystemProcessor systemProcessor in systemProcessors)
        {
            List<Command> commands = [];
            for (int i = 0; i < systemProcessor.CommandNames.Length; i++)
            {
                commands.Add(_commandFactory.Create(systemProcessor.CommandNames[i]));
            }

            await systemProcessor.Init([.. commands], databaseFactory);
        }

        await Task.WhenAll(systemProcessors.Select(sp => sp.AfterLoad()));

        return [.. systemProcessors];
    }
}
