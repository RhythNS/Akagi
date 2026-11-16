namespace Akagi.Web.Pages.Finance.Components;

public enum TradeFormMode
{
    /// <summary>A trade not in the log yet: a past one, or an order placed without the planner.</summary>
    New,
    /// <summary>A pending trade that went through: what it went through with.</summary>
    Record,
    /// <summary>Fixing a trade in the log.</summary>
    Edit
}
