using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using DnsClient.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using static Akagi.LLMs.Gemini.GeminiPayload;

namespace Akagi.LLMs.Gemini;

internal interface IGeminiClient : ILLM;

internal class GeminiClient : LLM, IGeminiClient
{
    internal class Options
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    private readonly string _apiKey;
    private readonly ICommandFactory _commandFactory;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(ICommandFactory commandFactory, IOptionsMonitor<Options> options, ILogger<GeminiClient> logger)
    {
        _commandFactory = commandFactory;
        _apiKey = options.CurrentValue.ApiKey;
        _logger = logger;
    }

    private GeminiPayload GetPayload(SystemProcessor systemProcessor, Context context)
    {
        Message[] messages = systemProcessor.MessageCompiler.Compile(context);

        List<GeminiPayload.Content> contents = [];
        foreach (Message message in messages)
        {
            string role = message.From switch
            {
                Message.Type.User => "user",
                Message.Type.Character => "model",
                Message.Type.System => "user",
                _ => throw new Exception($"Unknown message type: {message.From}"),
            };

            switch (message)
            {
                case TextMessage textMessage:
                    contents.Add(new GeminiPayload.Content
                    {
                        Parts =
                        [
                            new TextPart
                            {
                                Text = message.From == Message.Type.System ? "SYSTEM MESSAGE: " + textMessage.Text : textMessage.Text
                            }
                        ],
                        Role = role
                    });
                    break;

                case CommandMessage commandMessage:
                    contents.Add(new GeminiPayload.Content
                    {
                        Parts =
                        [
                            new FunctionCallPart
                            {
                                FunctionCall = new GeminiPayload.FunctionCall
                                {
                                    Id = new DateTimeOffset(commandMessage.Time).ToUnixTimeMilliseconds().ToString(),
                                    Name = commandMessage.Command.Name,
                                    Args = commandMessage.Command.Arguments.ToDictionary(arg => arg.Name, arg => (object)new Dictionary<string, string>
                                    {
                                        { "Description", arg.Description },
                                        { "Type", arg.ArgumentType.ToString() },
                                        { "Value", arg.Value }
                                    })
                                }
                            }
                        ],
                        Role = role
                    });
                    contents.Add(new GeminiPayload.Content
                    {
                        Parts =
                        [
                            new FunctionResponsePart
                        {
                            FunctionResponse = new FunctionResponse
                            {
                                Id = new DateTimeOffset(commandMessage.Time).ToUnixTimeMilliseconds().ToString(),
                                Name = commandMessage.Command.Name,
                                Response = new Dictionary<string, object>
                                {
                                    ["result"] = commandMessage.Output
                                }
                            }
                        }
                        ],
                        Role = "user"
                    });
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                    break;
            }
        }

        FunctionCallingMode functionCallingMode = systemProcessor.RunMode.ToFunctionCallingMode();

        List<FunctionDeclaration> declarations = [];
        foreach (Command command in systemProcessor.Commands)
        {
            Dictionary<string, object> properties = [];
            List<string> required = [];
            Argument[] args = command.GetDefaultArguments();

            foreach (Argument argument in args)
            {
                Dictionary<string, object> propertySchema = new()
                {
                    ["type"] = argument.ArgumentType.ToString().ToLowerInvariant(),
                    ["description"] = argument.Description
                };

                // If the argument is an array, set type and items accordingly  
                switch (argument.ArgumentType)
                {
                    case Argument.Type.String:
                        propertySchema["type"] = "array";
                        propertySchema["items"] = new Dictionary<string, object> { ["type"] = "string" };
                        break;
                    case Argument.Type.Float:
                    case Argument.Type.Int:
                        propertySchema["type"] = "array";
                        propertySchema["items"] = new Dictionary<string, object> { ["type"] = "number" };
                        break;
                    case Argument.Type.Bool:
                        propertySchema["type"] = "array";
                        propertySchema["items"] = new Dictionary<string, object> { ["type"] = "boolean" };
                        break;
                    default:
                        throw new Exception($"Unsupported argument type: {argument.ArgumentType}");
                }

                properties[argument.Name] = propertySchema;

                if (argument.IsRequired)
                {
                    required.Add(argument.Name);
                }
            }

            Dictionary<string, object> parameters = new()
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };

            FunctionDeclaration declaration = new()
            {
                Name = command.Name,
                Description = command.Description,
                Parameters = parameters
            };
            declarations.Add(declaration);
        }

        GeminiPayload payload;

        if (declarations.Count == 0)
        {
            if (functionCallingMode == FunctionCallingMode.ANY)
            {
                throw new Exception("Function calling mode is ANY but there are no function declarations.");
            }
            payload = new()
            {
                Instruction = new SystemInstruction
                {
                    Parts =
                    [
                        new TextPart
                    {
                        Text = systemProcessor.CompileSystemPrompt(context.User, context.Character)
                    }
                    ]
                },
                Contents = [.. contents],
                ToolConf = new ()
                {
                     FunctionCalling = new ()
                     {
                         Mode = FunctionCallingMode.NONE
                     }
                },
                Tools = null
            };
        }
        else
        {
            payload = new()
            {
                Instruction = new SystemInstruction
                {
                    Parts =
                    [
                        new TextPart
                        {
                            Text = systemProcessor.CompileSystemPrompt(context.User, context.Character)
                        }
                    ]
                },
                Contents = [.. contents],
                ToolConf = new ToolConfig
                {
                    FunctionCalling = new FunctionCallingConfig
                    {
                        Mode = functionCallingMode
                    }
                },
                Tools =
                [
                    new Tool
                    {
                        FunctionDeclarations = [.. declarations]
                    }
                ]
            };
        }
        return payload;
    }

    public override async Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
    {
        using HttpClient httpClient = new();

        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new Exception("Gemini API key is not set.");
        }
        if (string.IsNullOrEmpty(Model))
        {
            throw new Exception("Gemini model is not set.");
        }

        GeminiPayload payload = GetPayload(systemProcessor, context);
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={_apiKey}"
        )
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception($"Request failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        string content = await response.Content.ReadAsStringAsync();

        GeminiResponse? geminiResponse;
        try
        {
            geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(content);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Gemini response: {Content}", content);
            throw new Exception("Failed to deserialize Gemini response.", ex);
        }
        if (geminiResponse == null)
        {
            throw new Exception("Failed to deserialize Gemini response.");
        }
        if (geminiResponse.Candidates.Count == 0)
        {
            throw new Exception("No candidates found in Gemini response.");
        }
        if (geminiResponse.Candidates[0].Content.Parts.Count == 0)
        {
            throw new Exception("No content parts found in Gemini response.");
        }

        List<Command> commands = [];
        foreach (Part part in geminiResponse.Candidates[0].Content.Parts)
        {
            Command command;
            if (part.Text != null)
            {
                TextMessageCommand textCommand = _commandFactory.Create<TextMessageCommand>();
                textCommand.SetMessage(part.Text);
                command = textCommand;
            }
            else if (part.FunctionCall != null)
            {
                try
                {
                    command = systemProcessor.Commands
                        .FirstOrDefault(x => x.Name.Equals(part.FunctionCall.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception($"Could not find {part.FunctionCall.Name} command!");

                    Argument[] args = command.GetDefaultArguments();
                    foreach (KeyValuePair<string, object> partParameter in part.FunctionCall.Args)
                    {
                        Argument argument = args.First(x => x.Name == partParameter.Key);

                        if (partParameter.Value is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Array)
                        {
                            _logger.LogWarning("Expected a JSON array for argument {ArgumentName}, but got {Value}", argument.Name, partParameter.Value);
                            continue;
                        }

                        JsonElement firstElement = jsonElement.EnumerateArray().FirstOrDefault();

                        switch (firstElement.ValueKind)
                        {
                            case JsonValueKind.String:
                                argument.Value = firstElement.GetString()!;
                                break;
                            case JsonValueKind.Number:
                                if (argument.ArgumentType == Argument.Type.Int)
                                    argument.IntValue = firstElement.GetInt32();
                                else
                                    argument.FloatValue = firstElement.GetSingle();
                                break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:
                                argument.BoolValue = firstElement.GetBoolean();
                                break;
                            default:
                                _logger.LogWarning("Unknown argument type for {ArgumentName}: {Value}", argument.Name, firstElement.GetRawText());
                                argument.Value = firstElement.GetRawText();
                                continue;
                        }
                    }
                    command.Arguments = args;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create command from Gemini response: {content}", content);
                    continue;
                }
            }
            else
            {
                _logger.LogWarning("Unknown part type in Gemini response: {content}", content);
                continue;
            }

            command.From = systemProcessor.Output;
            commands.Add(command);
        }

        return [.. commands];
    }
}
