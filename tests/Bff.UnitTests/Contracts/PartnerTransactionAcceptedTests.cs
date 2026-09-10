using Bff.UnitTests.TestSupport;

namespace Bff.UnitTests.Contracts;

// From() is the only place the outbound event is built in production. It is six field
// mappings and a null-forgiving operator each — the kind of code a reviewer's eye slides
// over and a swapped pair (PartnerId into EventId) survives every other test in the suite.
public class PartnerTransactionAcceptedTests
{
    private static readonly DateTimeOffset AcceptedAt = new(2026, 5, 10, 14, 30, 1, TimeSpan.Zero);

    [Fact]
    public void Carries_every_field_of_the_request_onto_the_event()
    {
        var request = PartnerTransactionRequestFactory.Valid();

        var evt = PartnerTransactionAccepted.From(request, AcceptedAt);

        Assert.Equal(request.TransactionReference, evt.TransactionReference);
        Assert.Equal(request.PartnerId, evt.PartnerId);
        Assert.Equal(request.Amount, evt.Amount);
        Assert.Equal(request.Currency, evt.Currency);
    }

    [Fact]
    public void Scopes_the_event_id_to_the_partner_so_consumers_can_deduplicate()
    {
        var evt = PartnerTransactionAccepted.From(
            PartnerTransactionRequestFactory.Valid() with { PartnerId = "P-1001", TransactionReference = "TXN-42" },
            AcceptedAt);

        // Delivery is at-least-once; a per-call GUID here would make dedupe impossible.
        Assert.Equal("P-1001:TXN-42", evt.EventId);
    }

    [Fact]
    public void Gives_two_partners_distinct_event_ids_for_the_same_transaction_reference()
    {
        var request = PartnerTransactionRequestFactory.Valid() with { TransactionReference = "TXN-1" };

        var first  = PartnerTransactionAccepted.From(request with { PartnerId = "P-1001" }, AcceptedAt);
        var second = PartnerTransactionAccepted.From(request with { PartnerId = "P-2002" }, AcceptedAt);

        // transactionReference is unique per partner, not globally. Collapsing these would make
        // a consumer deduping on EventId silently drop the second partner's transaction.
        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData("USD")]
    public void Normalises_currency_to_upper_case_so_consumers_need_no_case_handling(string currency)
    {
        var evt = PartnerTransactionAccepted.From(
            PartnerTransactionRequestFactory.Valid() with { Currency = currency }, AcceptedAt);

        Assert.Equal("USD", evt.Currency);
    }

    [Fact]
    public void Keeps_the_partners_timestamp_and_ours_apart()
    {
        var occurred = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.FromHours(7));
        var request  = PartnerTransactionRequestFactory.Valid() with { Timestamp = occurred };

        var evt = PartnerTransactionAccepted.From(request, AcceptedAt);

        // Collapsing these two loses the partner-side latency that reconciliation depends on.
        Assert.Equal(occurred, evt.OccurredAt);
        Assert.Equal(AcceptedAt, evt.AcceptedAt);
        Assert.NotEqual(evt.OccurredAt, evt.AcceptedAt);
    }

    [Fact]
    public void Stamps_the_schema_version_consumers_branch_on()
    {
        var evt = PartnerTransactionAccepted.From(PartnerTransactionRequestFactory.Valid(), AcceptedAt);

        Assert.Equal("1.0", evt.SchemaVersion);
    }
}
