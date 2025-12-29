using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Akagi.LLMs.OpenRouter.OpenRouterPayload;
using TextMessage = Akagi.Characters.Conversations.TextMessage;

namespace Akagi.LLMs.OpenRouter;

internal interface IOpenRouterClient : ILLM;

internal class OpenRouterClient : LLM, IOpenRouterClient
{
    internal class Options
    {
        public required string ApiKey { init; get; }
        public required string BaseUrl { init; get; }
        public required string ResponsePath { init; get; }
    }

    private readonly Options _options;
    private readonly ICommandFactory _commandFactory;
    private readonly ILogger<OpenRouterClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public OpenRouterClient(ICommandFactory commandFactory, IOptionsMonitor<Options> options, ILogger<OpenRouterClient> logger)
    {
        _commandFactory = commandFactory;
        _options = options.CurrentValue;
        _logger = logger;
    }

    private OpenRouterPayload GetPayload(SystemProcessor systemProcessor, Context context)
    {
        Characters.Conversations.Message[] messages = systemProcessor.MessageCompiler.Compile(context);

        List<OpenRouterPayload.Message> input = [];

        input.Add(new UserMessage
        {
            Role = MessageRoleEnum.System,
            Content = systemProcessor.CompileSystemPrompt(context.User, context.Character)
        });

        for (int i = 0; i < messages.Length; i++)
        {
            Characters.Conversations.Message message = messages[i];
            MessageRoleEnum messageRoleEnum = message.From.ToMessageRoleEnum();
            switch (message)
            {
                case TextMessage textMessage:
                    input.Add(new UserMessage
                    {
                        Role = messageRoleEnum,
                        Content = textMessage.Text,
                    });
                    break;

                case CommandMessage commandMessage:
                    List<CommandMessage> commandMessages = [];
                    commandMessages.Add(commandMessage);
                    for (int j = i + 1; j < messages.Length; j++)
                    {
                        if (messages[j] is CommandMessage nextCommandMessage)
                        {
                            commandMessages.Add(nextCommandMessage);
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    commandMessages.RemoveAll(cm => systemProcessor.CommandNames.Contains(cm.Command.GetType().FullName
                        , StringComparer.Ordinal) == false);

                    if (commandMessages.Count == 0)
                    {
                        break;
                    }

                    List<ToolCall> toolCalls = [];
                    List<ToolResponseMessage> responseMessages = [];
                    foreach (CommandMessage cmdMsg in commandMessages)
                    {
                        toolCalls.Add(new ToolCall
                        {
                            Id = new DateTimeOffset(commandMessage.Time).ToUnixTimeMilliseconds().ToString(),
                            Type = "function",
                            Function = new FunctionCall
                            {
                                Name = cmdMsg.Command.Name,
                                Arguments = JsonSerializer.Serialize(cmdMsg.Command.Arguments.ToDictionary(kv => kv.Name, kv => kv.Value)),
                            },
                        });

                        responseMessages.Add(new ToolResponseMessage
                        {
                            Role = MessageRoleEnum.System,
                            ToolCallId = new DateTimeOffset(commandMessage.Time).ToUnixTimeMilliseconds().ToString(),
                            Content = cmdMsg.Output,
                        });
                    }
                    input.Add(new ToolRequestMessage()
                    {
                        Role = messageRoleEnum,
                        Content = null,
                        ToolCalls = [.. toolCalls],
                    });
                    input.AddRange(responseMessages);

                    break;

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                    break;
            }
        }

        List<Tool> tools = [];
        foreach (Command command in systemProcessor.Commands)
        {
            Dictionary<string, object> properties = [];
            List<string> required = [];
            Argument[] args = command.GetDefaultArguments();

            foreach (Argument argument in args)
            {
                string jsonType = argument.ArgumentType switch
                {
                    Argument.Type.String => "string",
                    Argument.Type.Int => "integer",
                    Argument.Type.Float => "number",
                    Argument.Type.Bool => "boolean",
                    _ => "string"
                };

                Dictionary<string, object> propertySchema = new()
                {
                    ["type"] = jsonType,
                    ["description"] = argument.Description
                };

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

            tools.Add(new Tool()
            {
                Type = "function",
                Function = new FunctionDescription
                {
                    Name = command.Name,
                    Description = command.Description,
                    Parameters = parameters
                }
            });
        }

        OpenRouterPayload payload = new()
        {
            Messages = [.. input],
            Tools = [.. tools],
            ToolChoice = systemProcessor.RunMode.ToToolChoiceEnum(),
            Model = Model
        };

        return payload;
    }

    private Command[] HandleResponse(SystemProcessor systemProcessor, OpenRouterResponse response)
    {
        if (response.Choices.Count == 0)
        {
            throw new Exception("No candidates found in OpenRouter response.");
        }

        List<Command> commands = [];
        Choice choice = response.Choices[0];
        if (choice.Error != null)
        {
            throw new Exception($"OpenRouter returned error response: {choice.Error.Code}:{choice.Error.Message} with {choice.Error.Metadata?.ToString()}");
        }

        if (choice.IsNonStreamingChoice)
        {
            if (choice.Message == null)
            {
                throw new Exception("OpenRouter returned NonStreamingChoice with no message.");
            }

            Message message = choice.Message;

            if (message.Content != null && string.IsNullOrWhiteSpace(message.Content) == false)
            {
                TextMessageCommand textCommand = _commandFactory.Create<TextMessageCommand>();
                textCommand.SetMessage(message.Content);
                commands.Add(textCommand);
            }
            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                foreach (ToolCall call in message.ToolCalls)
                {
                    FunctionCall function = call.Function;
                    Command command = systemProcessor.Commands
                        .FirstOrDefault(x => x.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception($"Could not find {function.Name} command!");

                    if (string.IsNullOrEmpty(function.Arguments))
                    {
                        command.Arguments = command.GetDefaultArguments();
                        commands.Add(command);
                        continue;
                    }

                    Dictionary<string, object>? parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(function.Arguments);
                    if (parameters == null)
                    {
                        throw new Exception($"Could not pass parameters: {function.Arguments}");
                    }

                    Argument[] args = command.GetDefaultArguments();
                    foreach (KeyValuePair<string, object> partParameter in parameters)
                    {
                        Argument argument = args.First(x => x.Name == partParameter.Key);

                        if (partParameter.Value is not JsonElement jsonElement)
                        {
                            _logger.LogWarning("Expected a JSON element for argument {ArgumentName}, but got {Value}", argument.Name, partParameter.Value);
                            continue;
                        }

                        switch (jsonElement.ValueKind)
                        {
                            case JsonValueKind.String:
                                argument.Value = jsonElement.GetString()!;
                                break;
                            case JsonValueKind.Number:
                                if (argument.ArgumentType == Argument.Type.Int)
                                    argument.IntValue = jsonElement.GetInt32();
                                else
                                    argument.FloatValue = jsonElement.GetSingle();
                                break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:
                                argument.BoolValue = jsonElement.GetBoolean();
                                break;
                            default:
                                _logger.LogWarning("Unknown argument type for {ArgumentName}: {Value}", argument.Name, jsonElement.GetRawText());
                                argument.Value = jsonElement.GetRawText();
                                continue;
                        }
                    }
                    command.Arguments = args;
                    commands.Add(command);
                }
            }
        }
        else if (choice.IsStreamingChoice)
        {
            throw new NotImplementedException("StreamingChoice not implemented");
        }
        else if (choice.IsNonChatChoice)
        {
            throw new Exception($"OpenRouter returned nonChatchoice: {choice.Text}");
        }
        else
        {
            throw new Exception("OpenRouter returned unknown choice type");
        }

        commands.ForEach(x => x.From = systemProcessor.Output);

        return [.. commands];
    }

    public override async Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
    {
        using HttpClient httpClient = new();

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new Exception("Gemini API key is not set.");
        }
        if (string.IsNullOrEmpty(Model))
        {
            throw new Exception("Gemini model is not set.");
        }

        OpenRouterPayload payload = GetPayload(systemProcessor, context);
        HttpRequestMessage request = new(HttpMethod.Post,
            $"{_options.BaseUrl}{_options.ResponsePath}");

        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception($"Request failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        string content = await response.Content.ReadAsStringAsync();

        OpenRouterResponse? openRouterResponse;
        try
        {
            openRouterResponse = JsonSerializer.Deserialize<OpenRouterResponse>(content, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize OpenRouter response: {Content}", content);
            throw new Exception("Failed to deserialize OpenRouter response.", ex);
        }
        if (openRouterResponse == null)
        {
            throw new Exception("Failed to deserialize OpenRouter response.");
        }

        return HandleResponse(systemProcessor, openRouterResponse);
    }
}
