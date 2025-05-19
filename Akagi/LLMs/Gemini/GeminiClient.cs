using Akagi.Characters;
using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.Commands.Messages;
using Akagi.Puppeteers.SystemProcessors;
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

    private string _apiKey;
    private ICommandFactory _commandFactory;
    private ILogger<GeminiClient> _logger;

    public GeminiClient(ICommandFactory commandFactory, IOptionsMonitor<Options> options, ILogger<GeminiClient> logger)
    {
        _commandFactory = commandFactory;
        _apiKey = options.CurrentValue.ApiKey;
        _logger = logger;
    }

    private GeminiPayload GetPayload(SystemProcessor systemProcessor, Character character)
    {
        List<GeminiPayload.Content> contents = [];
        foreach (Conversation? conversation in character.Conversations.OrderBy(x => x.Time))
        {
            foreach (Message message in conversation.Messages)
            {
                if (message is TextMessage textMessage)
                {
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
                }
                else
                {
                    _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                }
            }
        }

        GeminiPayload payload = new()
        {
            Instruction = new GeminiPayload.SystemInstruction
            {
                Parts =
                [
                    new GeminiPayload.Part
                    {
                        Text = systemProcessor.SystemInstruction
                    }
                ]
            },
            Contents = [.. contents]
        };
        return payload;
    }

    public async Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Character character)
    {
        using HttpClient httpClient = new();

        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_apiKey}"
        );

        GeminiPayload payload = GetPayload(systemProcessor, character);

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
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
        command.SetMessage(geminiResponse.Candidates[0].Content.Parts[0].Text);
        return [command];
    }
}
