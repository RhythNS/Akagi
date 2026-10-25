using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.VoiceClips;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
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
    private readonly IVoiceClipsDatabase _voiceClipsDatabase;
    private readonly ICommandFactory _commandFactory;
    private readonly ILogger<OpenRouterClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private sealed class ToolCallAccumulator
    {
        public int Index { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    public OpenRouterClient(ICommandFactory commandFactory,
        IVoiceClipsDatabase voiceClipsDatabase,
        IOptionsMonitor<Options> options,
        ILogger<OpenRouterClient> logger)
    {
        _commandFactory = commandFactory;
        _voiceClipsDatabase = voiceClipsDatabase;
        _options = options.CurrentValue;
        _logger = logger;
    }

    private async Task<OpenRouterPayload> GetPayload(SystemProcessor systemProcessor, Context context)
    {
        Characters.Conversations.Message[] messages = await systemProcessor.MessageCompiler.CompileAsync(context);

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

                case VoiceMessage voiceMessage:
                    {
                        VoiceClip? voiceClip = await _voiceClipsDatabase.GetDocumentByIdAsync(voiceMessage.VoiceId);
                        if (voiceClip == null)
                        {
                            _logger.LogWarning("Could not find voice clip with id {VoiceId} for message, skipping message", voiceMessage.VoiceId);
                            continue;
                        }
                        using Stream audioFile = await _voiceClipsDatabase.LoadFileAsync(voiceClip);
                        string audioFormat = voiceClip.AudioEncoding.ToExtension();

                        input.Add(new UserMessage
                        {
                            Role = messageRoleEnum,

                            Content = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["type"] = "input_audio",
                                    ["input_audio"] = new Dictionary<string, string>
                                    {
                                        ["data"] = await audioFile.ToBase64(),
                                        ["format"] = audioFormat
                                    }
                                }
                            }
                        });
                        break;
                    }

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

    private Command BuildCommandFromFunction(SystemProcessor systemProcessor, string functionName, string? argumentsJson)
    {
        Command command = systemProcessor.Commands
            .FirstOrDefault(x => x.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase))
            ?? throw new Exception($"Could not find {functionName} command!");

        if (string.IsNullOrEmpty(argumentsJson))
        {
            command.Arguments = command.GetDefaultArguments();
            return command;
        }

        Dictionary<string, object>? parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse arguments JSON for command {CommandName}: {ArgumentsJson}", functionName, argumentsJson);
            throw new Exception($"Could not pass parameters: {argumentsJson}", ex);
        }

        if (parameters == null)
        {
            throw new Exception($"Could not pass parameters: {argumentsJson}");
        }

        Argument[] args = command.GetDefaultArguments();
        foreach (KeyValuePair<string, object> partParameter in parameters)
        {
            Argument? argument = args.FirstOrDefault(x => x.Name == partParameter.Key);
            if (argument == null)
            {
                _logger.LogWarning("Command {CommandName} does not have an argument named {ArgumentName}", command.Name, partParameter.Key);
                continue;
            }

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
        return command;
    }

    private Command[] HandleResponse(SystemProcessor systemProcessor, OpenRouterResponse response, Context context)
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

            var message = choice.Message;

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
                    FunctionCall? function = call.Function;
                    if (function == null || string.IsNullOrEmpty(function.Name))
                    {
                        continue;
                    }

                    Command command = BuildCommandFromFunction(systemProcessor, function.Name, function.Arguments);
                    commands.Add(command);
                }
            }
        }
        else if (choice.IsStreamingChoice)
        {
            throw new NotImplementedException("StreamingChoice should be processed via GetNextStepsStreamAsync.");
        }
        else if (choice.IsNonChatChoice)
        {
            throw new Exception($"OpenRouter returned nonChatchoice: {choice.Text}");
        }
        else
        {
            throw new Exception("OpenRouter returned unknown choice type");
        }

        commands.ForEach(x => x.SetFrom(systemProcessor.Output, context.Character.Id!));

        return [.. commands];
    }

    public override async Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
    {
        using HttpClient httpClient = new();

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new Exception("OpenRouter API key is not set.");
        }
        if (string.IsNullOrEmpty(Model))
        {
            throw new Exception("OpenRouter model is not set.");
        }

        OpenRouterPayload payload = await GetPayload(systemProcessor, context);
        HttpRequestMessage request = new(HttpMethod.Post,
            $"{_options.BaseUrl}{_options.ResponsePath}");

        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending request to OpenRouter using model {Model}", Model);
        HttpResponseMessage response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception($"Request failed with status code {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        string content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Received response from OpenRouter using model {Model}", Model);

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

        return HandleResponse(systemProcessor, openRouterResponse, context);
    }

    public override async IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(
        SystemProcessor systemProcessor,
        Context context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using HttpClient httpClient = new();

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new Exception("OpenRouter API key is not set.");
        }
        if (string.IsNullOrEmpty(Model))
        {
            throw new Exception("OpenRouter model is not set.");
        }

        OpenRouterPayload payload = await GetPayload(systemProcessor, context);
        payload.Stream = true;

        HttpRequestMessage request = new(HttpMethod.Post,
            $"{_options.BaseUrl}{_options.ResponsePath}");

        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending streaming request to OpenRouter using model {Model}", Model);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OpenRouter streaming endpoint with model {Model}", Model);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"OpenRouter streaming request failed with status code {response.StatusCode}: {errorBody}");
        }

        using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using StreamReader reader = new(responseStream, Encoding.UTF8);

        StringBuilder fullTextBuilder = new();
        Dictionary<int, ToolCallAccumulator> toolCallAccumulators = [];

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(':')) // SSE comment / ping
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string data = line["data:".Length..].Trim();
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            OpenRouterResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenRouterResponse>(data, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize SSE chunk: {Data}", data);
                continue;
            }

            if (chunk?.Choices == null || chunk.Choices.Count == 0)
            {
                continue;
            }

            Choice choice = chunk.Choices[0];
            if (choice.Error != null)
            {
                throw new Exception($"OpenRouter SSE returned error response: {choice.Error.Code}:{choice.Error.Message}");
            }

            if (choice.Delta != null)
            {
                if (!string.IsNullOrEmpty(choice.Delta.Content))
                {
                    string textChunk = choice.Delta.Content;
                    fullTextBuilder.Append(textChunk);
                    yield return new TextDeltaEvent(textChunk);
                }

                if (choice.Delta.ToolCalls != null && choice.Delta.ToolCalls.Count > 0)
                {
                    foreach (ToolCall tc in choice.Delta.ToolCalls)
                    {
                        int tcIndex = tc.Index ?? 0;
                        if (!toolCallAccumulators.TryGetValue(tcIndex, out ToolCallAccumulator? acc))
                        {
                            acc = new ToolCallAccumulator { Index = tcIndex };
                            toolCallAccumulators[tcIndex] = acc;
                        }

                        if (!string.IsNullOrEmpty(tc.Id))
                        {
                            acc.Id = tc.Id;
                        }

                        if (tc.Function != null)
                        {
                            if (!string.IsNullOrEmpty(tc.Function.Name))
                            {
                                acc.Name = tc.Function.Name;
                            }

                            if (!string.IsNullOrEmpty(tc.Function.Arguments))
                            {
                                acc.ArgumentsBuilder.Append(tc.Function.Arguments);
                                yield return new ToolCallDeltaEvent(tcIndex, acc.Id, acc.Name, tc.Function.Arguments);
                            }
                        }
                    }
                }
            }
        }

        List<Command> finalCommands = [];
        string fullText = fullTextBuilder.ToString();

        if (!string.IsNullOrWhiteSpace(fullText))
        {
            TextMessageCommand textCommand = _commandFactory.Create<TextMessageCommand>();
            textCommand.SetMessage(fullText);
            finalCommands.Add(textCommand);
        }

        foreach (ToolCallAccumulator acc in toolCallAccumulators.Values.OrderBy(x => x.Index))
        {
            if (string.IsNullOrWhiteSpace(acc.Name))
            {
                continue;
            }

            Command toolCommand = BuildCommandFromFunction(systemProcessor, acc.Name, acc.ArgumentsBuilder.ToString());
            finalCommands.Add(toolCommand);
        }

        finalCommands.ForEach(x => x.SetFrom(systemProcessor.Output, context.Character.Id!));

        _logger.LogDebug("Completed streaming response from OpenRouter using model {Model}. Commands count: {Count}", Model, finalCommands.Count);

        yield return new StreamCompleteEvent([.. finalCommands], fullText);
    }
}
