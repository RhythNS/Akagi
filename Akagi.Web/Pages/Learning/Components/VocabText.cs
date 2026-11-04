using Akagi.Bridge.Chat.Models;
using System.Globalization;

namespace Akagi.Web.Pages.Learning.Components;

/// <summary>
/// How vocabulary values are written on the page.
/// </summary>
public static class VocabText
{
    public static string Value(double? value, VocabUnit unit) => value is not double known ? "-" : unit switch
    {
        VocabUnit.Count => known.ToString("0", CultureInfo.InvariantCulture),
        VocabUnit.Ratio => (known * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
        VocabUnit.Seconds => known.ToString("0.0", CultureInfo.InvariantCulture) + " s",
        _ => known.ToString("0.0", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// The series color class for the n-th series: you first, your partners second.
    /// </summary>
    public static string SeriesClass(int index) => index == 0 ? "viz-you" : "viz-partner";

    public static string Css(double percent) => Math.Clamp(percent, 0, 100).ToString("0.##", CultureInfo.InvariantCulture);

    public static string CallUri(string callId, double? time) =>
        $"Learn/Calls/{callId}" + (time is double seconds ? $"?at={seconds.ToString("0", CultureInfo.InvariantCulture)}" : string.Empty);

    /// <summary>
    /// A table as CSV, with every cell quoted.
    /// </summary>
    public static string Csv(VocabTable table)
    {
        static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        IEnumerable<string> lines = new[] { string.Join(",", table.Columns.Select(c => Quote(c.Label))) }
            .Concat(table.Rows.Select(r => string.Join(",", r.Cells.Select(c => Quote(c.Text)))));
        return string.Join("\r\n", lines);
    }
}
