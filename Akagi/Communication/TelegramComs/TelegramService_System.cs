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

    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An error occurred in the bot client.");

        for (int restartAttempts = 0; restartAttempts < MaxRestartAttempts; restartAttempts++)
        {
            int delay = (int)Math.Pow(2, restartAttempts);
            _logger.LogInformation("Attempting to restart Telegram bot in {Delay} seconds (attempt {Attempt}/{Max})", delay, restartAttempts, MaxRestartAttempts);
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);

            try
            {
                await StopAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart Telegram bot.");
            }
        }

        _logger.LogCritical("Max restart attempts reached. Stopping application.");
        _hostApplicationLifetime.StopApplication();
    }
}
