using Akagi.Web.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Models.Finance;

public enum TradeSide
{
    Buy,
    Sell
}

public enum TradeStatus
{
    /// <summary>Planned and maybe placed with the broker, not yet gone through; does not count for the holdings.</summary>
    Pending,
    /// <summary>Went through with the recorded time, shares and amount.</summary>
    Done,
    /// <summary>A pending trade that was dropped; kept for the history.</summary>
    Cancelled
}

/// <summary>
/// One buy or sell of a security in a <see cref="StockAccount"/>. A pending trade carries the plan (shares and the price
/// it was planned with); recording it fills in what went through.
/// </summary>
[BsonIgnoreExtraElements]
public class StockTrade : Savable
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string AccountId { get; set; } = string.Empty;

    public string Isin { get; set; } = string.Empty;

    public TradeSide Side { get; set; }

    public TradeStatus Status { get; set; } = TradeStatus.Done;

    /// <summary>Shares as planned; for trades entered directly the same as <see cref="Shares"/>.</summary>
    public decimal PlannedShares { get; set; }

    /// <summary>Price per share the plan expected.</summary>
    public decimal PlannedPrice { get; set; }

    /// <summary>Shares that went through.</summary>
    public decimal Shares { get; set; }

    /// <summary>Price per share that went through, without fees.</summary>
    public decimal Price { get; set; }

    /// <summary>Broker fees and taxes of this trade.</summary>
    public decimal Fees { get; set; }

    /// <summary>When it went through (UTC); null while pending.</summary>
    public DateTime? ExecutedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Trades saved together from one plan share this id.</summary>
    public string? PlanId { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>The broker's id for trades read from an export, so importing the same file twice adds nothing.</summary>
    public string? Reference { get; set; }

    /// <summary>Shares × price, without fees.</summary>
    [BsonIgnore]
    public decimal Amount => Shares * Price;

    /// <summary>Money that left the account for a buy (positive) or came back from a sale (negative), fees included.</summary>
    [BsonIgnore]
    public decimal CashFlow => Side == TradeSide.Buy ? Amount + Fees : -(Amount - Fees);

    public StockTrade Copy() => (StockTrade)MemberwiseClone();
}
