using System.Text.Json;
using Bff.Api.Endpoints;
using Bff.Api.Infrastructure.Options;
using Bff.Api.Message;
using Bff.Api.Partners;
using Bff.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bff.UnitTests.Endpoints;

// The handler is the composition of validator + verification + publisher. Those three
// are covered on their own elsewhere; what is only testable here is the ORDER and the
// short-circuits — above all, that nothing reaches the broker unless the partner verified.
public class PartnerTransactionEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 10, 14, 30, 1, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Harness(IPartnerVerificationClient Verifier, ITransactionPublisher Publisher);

    // The real validator, not a substitute: a stubbed validator would let a change in the
    // rules pass this suite while the endpoint rejects every request in production.
    private static Harness Build(PartnerVerificationResult verification = PartnerVerificationResult.Verified)
    {
        var verifier = Substitute.For<IPartnerVerificationClient>();
        verifier.VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(verification);

        return new Harness(verifier, Substitute.For<ITransactionPublisher>());
    }

    private static Task<IResult> Invoke(Harness h, PartnerTransactionRequest request, CancellationToken ct = default) =>
        PartnerTransactionEndpoints.HandleAsync(
            request,
            new PartnerTransactionRequestValidator(new ConfiguredCurrencyCatalog(
                Options.Create(new PartnerOptions { SupportedCurrencies = ["USD", "VND", "EUR"] }))),
            h.Verifier,
            h.Publisher,
            new FixedClock(),
            NullLogger<PartnerTransactionRequest>.Instance,
            ct);

    private static int StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
        ?? throw new InvalidOperationException("result carried no status code");

    // Serialised the way ASP.NET serialises it, so the assertion is about the wire, not the anonymous type.
    private static JsonElement BodyOf(IResult result) =>
        JsonSerializer.SerializeToElement(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // Any publish the handler made, or null if it stayed off the broker entirely.
    private static PartnerTransactionAccepted? Published(Harness h) =>
        h.Publisher.ReceivedCalls()
                   .Where(c => c.GetMethodInfo().Name == nameof(ITransactionPublisher.PublishAsync))
                   .Select(c => (PartnerTransactionAccepted)c.GetArguments()[0]!)
                   .SingleOrDefault();

    [Fact]
    public async Task Accepts_a_valid_request_from_a_verified_partner()
    {
        var h = Build();

        var result = await Invoke(h, PartnerTransactionRequestFactory.Valid());

        Assert.Equal(StatusCodes.Status202Accepted, StatusOf(result));
    }

    [Fact]
    public async Task Echoes_the_transaction_reference_so_the_partner_can_correlate_the_202()
    {
        var h = Build();

        var body = BodyOf(await Invoke(h, PartnerTransactionRequestFactory.Valid()));

        Assert.Equal("TXN-99823", body.GetProperty("transactionReference").GetString());
        Assert.Equal("Accepted", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Publishes_exactly_one_event_built_from_the_request_and_the_clock()
    {
        var h = Build();
        var request = PartnerTransactionRequestFactory.Valid();

        await Invoke(h, request);

        var published = Assert.IsType<PartnerTransactionAccepted>(Published(h));   // SingleOrDefault: never twice
        Assert.Equal(request.TransactionReference, published.TransactionReference);
        Assert.Equal(request.PartnerId, published.PartnerId);
        Assert.Equal(request.Amount, published.Amount);
        Assert.Equal(request.Timestamp, published.OccurredAt);   // the partner's clock
        Assert.Equal(Now, published.AcceptedAt);                 // ours, injected — never DateTimeOffset.UtcNow
    }

    [Fact]
    public async Task Verifies_the_partner_named_in_the_request()
    {
        var h = Build();

        await Invoke(h, PartnerTransactionRequestFactory.Valid() with { PartnerId = "P-2002" });

        await h.Verifier.Received(1).VerifyAsync("P-2002", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_an_invalid_payload_without_calling_the_partner_or_the_broker()
    {
        var h = Build();

        var result = await Invoke(h, PartnerTransactionRequestFactory.Valid() with { Amount = -5m });

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        await h.Verifier.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default);
        Assert.Null(Published(h));   // a bad request must not cost a downstream call
    }

    [Fact]
    public async Task Reports_422_and_publishes_nothing_when_the_partner_is_not_verified()
    {
        var h = Build(PartnerVerificationResult.NotVerified);

        var result = await Invoke(h, PartnerTransactionRequestFactory.Valid());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusOf(result));
        Assert.Null(Published(h));   // the whole point of the endpoint
    }

    [Fact]
    public async Task Reports_503_and_publishes_nothing_when_verification_is_unavailable()
    {
        var h = Build(PartnerVerificationResult.Unavailable);

        var result = await Invoke(h, PartnerTransactionRequestFactory.Valid());

        // 503, not 422: the partner may well be fine — we simply could not ask. Retryable.
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusOf(result));
        Assert.Null(Published(h));
    }

    [Fact]
    public async Task Does_not_answer_202_when_the_broker_refused_the_message()
    {
        var h = Build();
        h.Publisher.PublishAsync(Arg.Any<PartnerTransactionAccepted>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromException(new Exception("broker nacked")));

        // Accepting work that never entered the queue is silent data loss: the partner
        // is told 202 and will never retry.
        await Assert.ThrowsAsync<Exception>(() => Invoke(h, PartnerTransactionRequestFactory.Valid()));
    }

    [Fact]
    public async Task Forwards_the_request_cancellation_token_to_both_downstreams()
    {
        var h = Build();
        using var cts = new CancellationTokenSource();

        await Invoke(h, PartnerTransactionRequestFactory.Valid(), cts.Token);

        // Without this, a disconnected client leaves the verification call and the publish running.
        await h.Verifier.Received(1).VerifyAsync(Arg.Any<string>(), cts.Token);
        await h.Publisher.Received(1).PublishAsync(Arg.Any<PartnerTransactionAccepted>(), cts.Token);
    }
}
