using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Receivers.SystemProcessors;
using Akagi.Users;
using DnsClient.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Akagi.LLMs.Gemini;

internal class GeminiClient : IGeminiClient
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

    private GeminiPayload GetPayload(SystemProcessor systemProcessor, Character character, User user)
    {
        Message[] messages = systemProcessor.CompileMessages(user, character);
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
                            new GeminiPayload.Part
                            {
                                Text = message.From == Message.Type.System ? "SYSTEM MESSAGE: " + textMessage.Text : textMessage.Text
                            }
                        ],
                        Role = role
                    });
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                    break;
            }
        }

        List<GeminiPayload.FunctionDeclaration> declarations = [];
        foreach (Command command in systemProcessor.Commands)
        {
            Dictionary<string, object> properties = [];
            List<string> required = [];

            foreach (Argument argument in command.Arguments)
            {
                Dictionary<string, object> propertySchema = new()
                {
                    ["type"] = argument.ArgumentType.ToString().ToLowerInvariant(),
                    ["description"] = argument.Description
                };

                // If the argument is an array, set type and items accordingly  
                if (argument.ArgumentType == Argument.Type.String)
                {
                    propertySchema["type"] = "array";
                    propertySchema["items"] = new Dictionary<string, object> { ["type"] = "string" };
                }
                else if (argument.ArgumentType == Argument.Type.Float || argument.ArgumentType == Argument.Type.Int)
                {
                    propertySchema["type"] = "array";
                    propertySchema["items"] = new Dictionary<string, object> { ["type"] = "number" };
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

            GeminiPayload.FunctionDeclaration declaration = new()
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
            payload = new()
            {
                Instruction = new GeminiPayload.SystemInstruction
                {
                    Parts =
                    [
                        new GeminiPayload.Part
                    {
                        Text = systemProcessor.CompileSystemPrompt(user, character)
                    }
                    ]
                },
                Contents = [.. contents]
            };
        }
        else
        {
            payload = new()
            {
                Instruction = new GeminiPayload.SystemInstruction
                {
                    Parts =
                    [
                        new GeminiPayload.Part
                        {
                            Text = systemProcessor.CompileSystemPrompt(user, character)
                        }
                    ]
                },
                Contents = [.. contents],
                Tools =
                [
                    new GeminiPayload.Tool
                    {
                        FunctionDeclarations = [.. declarations]
                    }
                ]
            };
        }
        return payload;
    }

    public async Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Character character, User user)
    {
        using HttpClient httpClient = new();

        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_apiKey}"
        );

        GeminiPayload payload = GetPayload(systemProcessor, character, user);

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        int retries = 2;
    retry:
        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            if (retries-- > 0 && response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                goto retry;
            }

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
            if (part.Text != null)
            {
                TextMessageCommand command = _commandFactory.Create<TextMessageCommand>();
                command.SetMessage(part.Text, systemProcessor.Output);
                commands.Add(command);
            }
            else if (part.FunctionCall != null)
            {
                try
                {
                    Command command = systemProcessor.Commands
                        .FirstOrDefault(x => x.Name.Equals(part.FunctionCall.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception($"Could not find {part.FunctionCall.Name} command!");
                    // TODO: maybe copy command instead of using it directly?

                    foreach (KeyValuePair<string, object> partParameter in part.FunctionCall.Args)
                    {
                        Argument argument = command.Arguments.First(x => x.Name == partParameter.Key);

                        if (partParameter.Value is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Array)
                        {
                            _logger.LogWarning("Expected a JSON array for argument {ArgumentName}, but got {Value}", argument.Name, partParameter.Value);
                            continue;
                        }

                        JsonElement firstElement = jsonElement.EnumerateArray().FirstOrDefault();

                        switch (firstElement.ValueKind)
                        {
                            case JsonValueKind.String:
                                argument.Value = firstElement.GetString();
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
                    commands.Add(command);
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
        }

        return [.. commands];
    }
}
