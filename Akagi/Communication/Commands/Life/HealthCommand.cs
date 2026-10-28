using Akagi.Connectors.GoogleHealth;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Health;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Akagi.Communication.Commands.Life;

internal class HealthCommand : TextCommand
{
    public override string Name => "/health";

    public override string Description =>
        "Links Google Health (Fitbit data included) so sleep, steps and resting heart rate sync automatically. " +
        "Usage: /health link | /health code <code or the full address after approving> | /health sync | /health status | /health unlink";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        IServiceProvider services = Globals.Instance.ServiceProvider;
        IGoogleHealthClient client = services.GetRequiredService<IGoogleHealthClient>();
        if (!client.IsConfigured)
        {
            await Communicator.SendMessage(context.User, "Google Health is not configured on the server (GoogleHealth:ClientId and GoogleHealth:ClientSecret).");
            return CommandResult.Fail("Google Health not configured.");
        }

        IGoogleHealthAccountDatabase accounts = context.DatabaseFactory.GetDatabase<IGoogleHealthAccountDatabase>();
        GoogleHealthAccount account = await accounts.GetOrCreate(context.User.Id!);
        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string message;

        switch (verb)
        {
            case "link":
                string url = client.StartAuthorization(account);
                await accounts.SaveDocumentAsync(account);
                message = "Your Google account needs a Google Health profile first: sign in once in the Google Health app " +
                          $"(moving your Fitbit account over if asked).{Environment.NewLine}" +
                          "Then open this link, allow access, copy the address your browser ends up on (even if the page shows something else) " +
                          $"and send it with /health code <address>:{Environment.NewLine}{url}";
                break;

            case "code":
                if (args.Length < 2)
                {
                    message = "Send the code or the full address: /health code <address>.";
                    break;
                }
                try
                {
                    await client.CompleteAuthorizationAsync(account, string.Join(string.Empty, args.Skip(1)), CancellationToken.None);
                    await accounts.SaveDocumentAsync(account);
                    if (account.LastError == GoogleHealthClient.NotLinkedMessage)
                    {
                        message = $"Google access granted, but no data yet. {GoogleHealthClient.NotLinkedMessage}";
                        break;
                    }
                    GoogleHealthSyncResult? result = await services.GetRequiredService<IGoogleHealthSync>().SyncUserAsync(context.User.Id!);
                    message = $"Google Health linked. {result}";
                }
                catch (Exception ex) when (ex is GoogleHealthAuthException or HttpRequestException)
                {
                    message = $"Linking failed: {ex.Message}";
                }
                break;

            case "sync":
                try
                {
                    GoogleHealthSyncResult? result = await services.GetRequiredService<IGoogleHealthSync>().SyncUserAsync(context.User.Id!);
                    message = result?.ToString() ?? "Google Health is not linked. Use /health link.";
                    LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(context.User.Id!);
                    HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
                    if (result != null && summary.HasData)
                    {
                        message += Environment.NewLine + summary.Describe();
                    }
                }
                catch (Exception ex) when (ex is GoogleHealthAuthException or HttpRequestException)
                {
                    message = $"Sync failed: {ex.Message}";
                }
                break;

            case "unlink":
                await client.RevokeAsync(account, CancellationToken.None);
                account.AccessToken = string.Empty;
                account.RefreshToken = string.Empty;
                account.PendingState = string.Empty;
                account.PendingVerifier = string.Empty;
                await accounts.SaveDocumentAsync(account);
                message = "Google Health unlinked and access revoked. Already imported days stay in your profile.";
                break;

            default:
                message = Status(account);
                break;
        }

        await Communicator.SendMessage(context.User, message);
        return CommandResult.Ok;
    }

    internal static string Status(GoogleHealthAccount account)
    {
        if (!account.IsConnected)
        {
            return string.IsNullOrEmpty(account.LastError) ? "Google Health is not linked. Use /health link." : $"Google Health is not linked: {account.LastError}";
        }
        string lastSync = account.LastSyncUtc.HasValue
            ? account.LastSyncUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "never";
        string error = string.IsNullOrEmpty(account.LastError) ? string.Empty : $" Last error: {account.LastError}";
        return $"Google Health linked since {account.ConnectedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, last sync {lastSync}.{error}";
    }
}
