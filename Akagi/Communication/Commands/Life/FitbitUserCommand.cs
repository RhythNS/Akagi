using Akagi.Connectors.Fitbit;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Health;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Akagi.Communication.Commands.Life;

internal class FitbitCommand : TextCommand
{
    public override string Name => "/fitbit";

    public override string Description =>
        "Links Fitbit so sleep, steps and resting heart rate sync automatically. " +
        "Usage: /fitbit link | /fitbit code <code or the full address after approving> | /fitbit sync | /fitbit status | /fitbit unlink";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        IServiceProvider services = Globals.Instance.ServiceProvider;
        IFitbitClient client = services.GetRequiredService<IFitbitClient>();
        if (!client.IsConfigured)
        {
            await Communicator.SendMessage(context.User, "Fitbit is not configured on the server (Fitbit:ClientId).");
            return CommandResult.Fail("Fitbit not configured.");
        }

        IFitbitAccountDatabase accounts = context.DatabaseFactory.GetDatabase<IFitbitAccountDatabase>();
        FitbitAccount account = await accounts.GetOrCreate(context.User.Id!);
        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string message;

        switch (verb)
        {
            case "link":
                string url = client.StartAuthorization(account);
                await accounts.SaveDocumentAsync(account);
                message = "Open this link, approve access, then copy the address your browser ends up on (even if the page does not load) " +
                          $"and send it with /fitbit code <address>:{Environment.NewLine}{url}";
                break;

            case "code":
                if (args.Length < 2)
                {
                    message = "Send the code or the full address: /fitbit code <address>.";
                    break;
                }
                try
                {
                    await client.CompleteAuthorizationAsync(account, string.Join(string.Empty, args.Skip(1)), CancellationToken.None);
                    await accounts.SaveDocumentAsync(account);
                    FitbitSyncResult? result = await services.GetRequiredService<IFitbitSync>().SyncUserAsync(context.User.Id!);
                    message = $"Fitbit linked. {result}";
                }
                catch (Exception ex) when (ex is FitbitAuthException or HttpRequestException)
                {
                    message = $"Linking failed: {ex.Message}";
                }
                break;

            case "sync":
                try
                {
                    FitbitSyncResult? result = await services.GetRequiredService<IFitbitSync>().SyncUserAsync(context.User.Id!);
                    message = result?.ToString() ?? "Fitbit is not linked. Use /fitbit link.";
                    LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(context.User.Id!);
                    HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
                    if (result != null && summary.HasData)
                    {
                        message += Environment.NewLine + summary.Describe();
                    }
                }
                catch (Exception ex) when (ex is FitbitAuthException or HttpRequestException)
                {
                    message = $"Sync failed: {ex.Message}";
                }
                break;

            case "unlink":
                account.AccessToken = string.Empty;
                account.RefreshToken = string.Empty;
                account.PendingState = string.Empty;
                account.PendingVerifier = string.Empty;
                await accounts.SaveDocumentAsync(account);
                message = "Fitbit unlinked. Already imported days stay in your profile. You can also revoke access in your Fitbit account settings.";
                break;

            default:
                message = Status(account);
                break;
        }

        await Communicator.SendMessage(context.User, message);
        return CommandResult.Ok;
    }

    internal static string Status(FitbitAccount account)
    {
        if (!account.IsConnected)
        {
            return string.IsNullOrEmpty(account.LastError) ? "Fitbit is not linked. Use /fitbit link." : $"Fitbit is not linked: {account.LastError}";
        }
        string lastSync = account.LastSyncUtc.HasValue
            ? account.LastSyncUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "never";
        string error = string.IsNullOrEmpty(account.LastError) ? string.Empty : $" Last error: {account.LastError}";
        return $"Fitbit linked since {account.ConnectedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, last sync {lastSync}.{error}";
    }
}
