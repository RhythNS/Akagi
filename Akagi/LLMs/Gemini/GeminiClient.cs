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
            switch (message)
            {
                case TextMessage textMessage:
                    contents.Add(new GeminiPayload.Content
                    {
                        Parts =
                            [
                                new GeminiPayload.Part
                                {
                                    Text = textMessage.Text
                                }
                            ],
                        Role = message.From == Message.Type.User ? "user" : "assistant"
                    });
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                    break;
            }
        }

        List<GeminiPayload.FunctionDecleration> declerations = [];
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

            GeminiPayload.FunctionDecleration decleration = new()
            {
                Name = command.Name,
                Description = command.Description,
                Parameters = parameters
            };
            declerations.Add(decleration);
        }

        GeminiPayload payload;

        if (declerations.Count == 0)
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
                    FunctionDeclerations = [.. declerations]
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
        GeminiResponse? geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(content);

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

        TextMessageCommand command = _commandFactory.Create<TextMessageCommand>();
        command.SetMessage(geminiResponse.Candidates[0].Content.Parts[0].Text, systemProcessor.Output);
        return [command];
    }
}
