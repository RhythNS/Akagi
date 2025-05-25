using Akagi.Characters;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Users;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text.Json;

namespace Akagi.Receivers.SystemProcessors;

internal class SystemProcessorDatabase : Database<SystemProcessor>, ISystemProcessorDatabase
{
    private readonly ICommandFactory _commandFactory;

    public SystemProcessorDatabase(IOptionsMonitor<DatabaseOptions> options,
        ICommandFactory commandFactory) : base(options, "SystemProcessor")
    {
        _commandFactory = commandFactory;
    }

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
        return [.. systemProcessors];
    }

    public async Task<bool> SaveSystemProcessorFromFile(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
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

/*
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

    public async Task<bool> SaveSystemProcessorFromFile(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
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

 */
