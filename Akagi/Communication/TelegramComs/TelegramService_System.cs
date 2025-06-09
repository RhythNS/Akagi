using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Telegram bot client!");

        _client = new TelegramBotClient(_token);
        _me = await _client.GetMe(cancellationToken);

        _client.StartReceiving(
            HandleUpdate,
            HandleErrorAsync,
            new ReceiverOptions(),
            cancellationToken
        );

        _logger.LogInformation("Bot client started with username {Username}", _me.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Telegram bot client!");
        _client?.Close(cancellationToken);
        return Task.CompletedTask;
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An error occurred in the bot client.");
        return Task.CompletedTask;
    }
}
