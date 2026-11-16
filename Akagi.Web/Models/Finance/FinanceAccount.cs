using Akagi.Web.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Models.Finance;

/// <summary>
/// The kinds of financial record systems an account can be. Each kind has one <see cref="Services.Finance.IAccountSystem"/>
/// that knows how to value it.
/// </summary>
public enum AccountKind
{
    /// <summary>A collection of stocks and funds, bought and sold by ISIN.</summary>
    Stocks
}

/// <summary>
/// What every financial account has, whatever records it keeps. The overview works only through this and the account's
/// <see cref="Services.Finance.IAccountSystem"/>, so a new kind of account needs no change there.
/// </summary>
public interface IFinanceAccount
{
    public string? Id { get; }
    public string UserId { get; }
    public string Name { get; }
    public AccountKind Kind { get; }
    /// <summary>ISO code all of the account's amounts are in.</summary>
    public string Currency { get; }
    public DateTime CreatedAt { get; }
}

/// <summary>
/// The stored base of all accounts, in one collection; each kind is a subclass with its own records.
/// </summary>
[BsonDiscriminator(RootClass = true)]
[BsonKnownTypes(typeof(StockAccount))]
[BsonIgnoreExtraElements]
public abstract class FinanceAccount : Savable, IFinanceAccount
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [BsonIgnore]
    public abstract AccountKind Kind { get; }

    public string Currency { get; set; } = "EUR";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Creates an empty account of the given kind.</summary>
    public static FinanceAccount Create(AccountKind kind, string userId, string name) => kind switch
    {
        AccountKind.Stocks => new StockAccount { UserId = userId, Name = name },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
