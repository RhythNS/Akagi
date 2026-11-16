using Akagi.Web.Models.Finance;
using Akagi.Web.Services.Finance;
using System.Globalization;

namespace Akagi.Web.Pages.Finance;

/// <summary>Small display helpers shared by the finance pages.</summary>
public static class FinanceText
{
    public static string GainClass(decimal value) => value > 0.004m ? "text-success" : value < -0.004m ? "text-danger" : "text-secondary";

    public static string GainClass(decimal? value) => value is decimal v ? GainClass(v) : "text-secondary";

    public static string When(DateTime? utc) => utc is DateTime value ? Local(value).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "–";

    public static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string ShortDay(DateOnly day) => day.ToString("dd.MM.yy", CultureInfo.InvariantCulture);

    /// <summary>For datetime-local inputs.</summary>
    public static string InputTime(DateTime utc) => Local(utc).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    /// <summary>Reads a datetime-local input (local time) as UTC.</summary>
    public static DateTime? ParseInputTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime local) ? local.ToUniversalTime() : null;

    public static string Side(TradeSide side) => side == TradeSide.Buy ? "Buy" : "Sell";

    public static string SideClass(TradeSide side) => side == TradeSide.Buy ? "bg-green-lt" : "bg-orange-lt";

    public static string StatusClass(TradeStatus status) => status switch
    {
        TradeStatus.Pending => "bg-yellow-lt",
        TradeStatus.Done => "bg-blue-lt",
        _ => "bg-secondary-lt"
    };

    public static string Source(PriceSource source) => source switch
    {
        PriceSource.Quote => "close",
        PriceSource.Trade => "trade price",
        _ => "typed in"
    };

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
}
