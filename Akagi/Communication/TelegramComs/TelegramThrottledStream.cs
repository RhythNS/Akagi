using Microsoft.Extensions.Logging;
using System.Text;
using Telegram.Bot;

namespace Akagi.Communication.TelegramComs;

internal class TelegramThrottledStream : IMessageStream
{
    private readonly TelegramBotClient _client;
    private readonly long _chatId;
    private readonly ILogger _logger;
    private readonly TimeSpan _throttleInterval = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly StringBuilder _accumulated = new();

    private int? _sentMessageId;
    private DateTime _lastEditTime = DateTime.MinValue;
    private string _lastEditedText = string.Empty;
    private bool _isCompleted = false;

    public TelegramThrottledStream(TelegramBotClient client, long chatId, ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _chatId = chatId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task WriteChunkAsync(string chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(chunk) || _isCompleted)
        {
            return;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            _accumulated.Append(chunk);

            if (_sentMessageId == null)
            {
                string initialText = _accumulated.ToString();
                if (!string.IsNullOrWhiteSpace(initialText))
                {
                    try
                    {
                        var sentMessage = await _client.SendMessage(
                            chatId: _chatId,
                            text: initialText,
                            cancellationToken: ct);

                        _sentMessageId = sentMessage.Id;
                        _lastEditedText = initialText;
                        _lastEditTime = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send initial streaming message to Telegram chat {ChatId}.", _chatId);
                    }
                }
                return;
            }

            if (DateTime.UtcNow - _lastEditTime >= _throttleInterval)
            {
                await FlushEditInternalAsync(isFinal: false, ct);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task CompleteAsync(string fullMessage, CancellationToken ct = default)
    {
        if (_isCompleted)
        {
            return;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            _isCompleted = true;

            string finalText = !string.IsNullOrEmpty(fullMessage) ? fullMessage : _accumulated.ToString();

            if (string.IsNullOrWhiteSpace(finalText))
            {
                return;
            }

            if (_sentMessageId == null)
            {
                try
                {
                    await _client.SendMessage(
                        chatId: _chatId,
                        text: finalText,
                        cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send complete message to Telegram chat {ChatId}.", _chatId);
                }
                return;
            }

            _accumulated.Clear();
            _accumulated.Append(finalText);
            await FlushEditInternalAsync(isFinal: true, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AbortAsync(Exception ex, CancellationToken ct = default)
    {
        if (_isCompleted)
        {
            return;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            _isCompleted = true;
            _logger.LogWarning(ex, "Telegram message stream aborted for chat {ChatId}.", _chatId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task FlushEditInternalAsync(bool isFinal, CancellationToken ct)
    {
        if (_sentMessageId == null)
        {
            return;
        }

        string textToEdit = _accumulated.ToString();
        if (string.IsNullOrWhiteSpace(textToEdit) || textToEdit == _lastEditedText)
        {
            return;
        }

        if (textToEdit.Length > 4000)
        {
            textToEdit = textToEdit[..4000];
        }

        try
        {
            await _client.EditMessageText(
                chatId: _chatId,
                messageId: _sentMessageId.Value,
                text: textToEdit,
                cancellationToken: ct);

            _lastEditedText = textToEdit;
            _lastEditTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit Telegram message {MessageId} in chat {ChatId}.", _sentMessageId, _chatId);
        }
    }

    public ValueTask DisposeAsync()
    {
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }
}
