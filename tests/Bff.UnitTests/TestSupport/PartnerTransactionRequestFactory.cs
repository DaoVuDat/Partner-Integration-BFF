using Bff.Api.Contracts;

namespace Bff.UnitTests.TestSupport;

public static class PartnerTransactionRequestFactory
{
    public static PartnerTransactionRequest Valid() => new()
    {
        PartnerId = "P-1001",
        TransactionReference = "TXN-99823",
        Amount = 250.00m,
        Currency = "USD",
        Timestamp = DateTimeOffset.Parse("2024-05-10T14:30:00Z")
    };
}