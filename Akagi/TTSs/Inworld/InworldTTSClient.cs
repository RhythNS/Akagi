using Akagi.Characters.VoiceClips;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akagi.TTSs.Inworld;

internal interface IInworldTTSClient : ITTS;

internal class InworldTTSClient : TTS, IInworldTTSClient
{
    internal class Options
    {
        public required string ApiKey { init; get; }
        public required string BaseUrl { init; get; }
        public string AudioEncoding { get; init; } = "MP3";
        public int SampleRateHertz { get; init; } = 48000;
    }

    private readonly Options _options;
    private readonly AudioEncoding _audioEncoding = AudioEncoding.MP3;
    private readonly ILogger<InworldTTSClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public InworldTTSClient(IOptionsMonitor<Options> options, ILogger<InworldTTSClient> logger)
    {
        _options = options.CurrentValue;
        _logger = logger;

        _audioEncoding = Enum.Parse<AudioEncoding>(_options.AudioEncoding, ignoreCase: true);
    }

    private InworldPayload GetPayload(string text, string voiceId, string modelId)
    {
        return new InworldPayload
        {
            Text = text,
            VoiceId = voiceId,
            ModelId = modelId,
            AudioConfig = new InworldPayload.AudioConfiguration
            {
                AudioEncoding = _options.AudioEncoding,
                SampleRateHertz = _options.SampleRateHertz,
            },
            Temperature = 1.0f,
        };
    }

    private TTSResult HandleResponse(List<InworldStreamChunk> chunks)
    {
        List<byte> audioBytes = [];
        int processedCharacters = 0;
        string? usedModelId = null;

        foreach (InworldStreamChunk chunk in chunks)
        {
            if (chunk.Error != null)
            {
                throw new Exception($"Inworld TTS returned error: {chunk.Error.Code}: {chunk.Error.Message}");
            }

            if (chunk.Result == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk.Result.AudioContent))
            {
                byte[] decoded = Convert.FromBase64String(chunk.Result.AudioContent);
                audioBytes.AddRange(decoded);
            }

            if (chunk.Result.Usage != null)
            {
                processedCharacters = Math.Max(processedCharacters, chunk.Result.Usage.ProcessedCharactersCount);
                usedModelId ??= chunk.Result.Usage.ModelId;
            }
        }

        return new TTSResult
        {
            AudioContent = [.. audioBytes],
            UsedModelId = usedModelId,
            ProcessedCharactersCount = processedCharacters,
            AudioEncoding = _audioEncoding,
        };
    }

    public override async Task<TTSResult> SynthesizeSpeechAsync(string text, string voiceId, string modelId)
    {
        using HttpClient httpClient = new();

        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            throw new InvalidOperationException("Inworld API key is not set.");
        }

        InworldPayload payload = GetPayload(text, voiceId, modelId);

        HttpRequestMessage request = new(HttpMethod.Post,
            $"{_options.BaseUrl}/tts/v1/voice:stream");
        request.Headers.Add("Authorization", $"Basic {_options.ApiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Inworld TTS request failed with status code {response.StatusCode}: {errorContent}");
        }

        List<InworldStreamChunk> chunks = [];

        using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            InworldStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<InworldStreamChunk>(line, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize Inworld TTS stream chunk: {doLine}", line);
                continue;
            }

            if (chunk != null)
            {
                chunks.Add(chunk);
            }
        }

        if (chunks.Count == 0)
        {
            throw new Exception("No data received from Inworld TTS stream.");
        }

        return HandleResponse(chunks);
    }
}
