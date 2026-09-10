namespace Bff.Api.Contracts;

public sealed record PartnerTransactionAccepted
{
    // Partner-scoped: transactionReference is unique only within one partner, so two
    // partners can legitimately both send "TXN-1". Consumers dedupe on this key.
    public required string EventId { get; init; }
    public required string TransactionReference { get; init; }
    public required string PartnerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset OccurredAt { get; init; } // partner's timestamp
    public required DateTimeOffset AcceptedAt { get; init; } // ours timestamp
    public string SchemaVersion { get; init; } = "1.0";

    public static PartnerTransactionAccepted From(PartnerTransactionRequest r, DateTimeOffset now) => new()
    {
        EventId = $"{r.PartnerId}:{r.TransactionReference}",
        TransactionReference = r.TransactionReference!,
        PartnerId = r.PartnerId!,
        Amount = r.Amount!.Value,
        Currency = r.Currency!.ToUpperInvariant(),
        OccurredAt = r.Timestamp!.Value,
        AcceptedAt = now
    };
}