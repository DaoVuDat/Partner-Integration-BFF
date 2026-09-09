namespace Bff.Api.Contracts;

public sealed record PartnerTransactionRequest
{
    public string? PartnerId { get; init; }
    public string? TransactionReference { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? TimeStamp { get; init; }
};