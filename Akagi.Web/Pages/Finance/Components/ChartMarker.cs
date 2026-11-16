namespace Akagi.Web.Pages.Finance.Components;

/// <summary>A trade drawn on a price chart.</summary>
public sealed record ChartMarker(DateOnly Day, decimal Price, bool Buy, string Label);
