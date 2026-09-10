namespace Bff.Api.Contracts;

public sealed record PartnerTransactionAccepted
{
    public required string EventId { get; init; } // duplicate key + must be idempotent
    public required string TransactionReference { get; init; }
    public required string PartnerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset OccurredAt { get; init; } // partner's timestamp
    public required DateTimeOffset AcceptedAt { get; init; } // ours timestamp
    public string SchemaVersion { get; init; } = "1.0";

    public static PartnerTransactionAccepted From(PartnerTransactionRequest r, DateTimeOffset now) => new()
    {
        EventId = r.TransactionReference!, 
        TransactionReference = r.TransactionReference!,
        PartnerId = r.PartnerId!,
        Amount = r.Amount!.Value,
        Currency = r.Currency!.ToUpperInvariant(),
        OccurredAt = r.Timestamp!.Value,
        AcceptedAt = now
    };
}