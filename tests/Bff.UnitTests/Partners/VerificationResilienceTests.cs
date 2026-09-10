using System.Net;
using System.Text;
using Bff.Api.Infrastructure;
using Bff.Api.Partners;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bff.UnitTests.Partners;


public class VerificationResilienceTests
{
    // A stub transport: no network, full control, counts attempts.
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _index;
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Attempts++;
            var status = statuses[Math.Min(_index++, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"partnerId":"P-1001","isActive":true}""",
                                            Encoding.UTF8, "application/json")
            });
        }
    }

    // A transport that fails the way a network fails, rather than answering with a status.
    private sealed class FaultingHandler(Func<Exception>? fault = null, TimeSpan? delay = null) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Attempts++;
            if (delay is { } d) await Task.Delay(d, ct);       // honours ct, so a timeout can cut it short
            throw fault?.Invoke() ?? new HttpRequestException("connection refused");
        }
    }

    // Answers 200 with a caller-supplied payload, to exercise how the body is read.
    private sealed class BodyHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private static IPartnerVerificationClient BuildWith(HttpMessageHandler handler, string attemptTimeout = "00:00:01",
                                                        string totalTimeout = "00:00:05")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Verification:BaseUrl"]          = "http://verification.test",
            ["Verification:MaxRetryAttempts"] = "1",             // keep the failure tests quick
            ["Verification:BaseDelay"]        = "00:00:00.001",
            ["Verification:AttemptTimeout"]   = attemptTimeout,
            ["Verification:TotalTimeout"]     = totalTimeout,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPartnerVerification(config)
                .ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        return services.BuildServiceProvider().GetRequiredService<IPartnerVerificationClient>();
    }

    private static (IPartnerVerificationClient Client, SequenceHandler Handler) Build(params HttpStatusCode[] s)
    {
        var handler = new SequenceHandler(s);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Verification:BaseUrl"]          = "http://verification.test",
            ["Verification:MaxRetryAttempts"] = "3",
            ["Verification:BaseDelay"]        = "00:00:00.001",  // keep the test fast
            ["Verification:AttemptTimeout"]   = "00:00:01",
            ["Verification:TotalTimeout"]     = "00:00:05",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPartnerVerification(config)            // ← the PRODUCTION pipeline
                .ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IPartnerVerificationClient>(), handler);
    }

    [Fact]
    public async Task Retries_transient_failures_and_then_succeeds()
    {
        var (client, handler) = Build(HttpStatusCode.InternalServerError,
                                      HttpStatusCode.InternalServerError,
                                      HttpStatusCode.OK);

        var result = await client.VerifyAsync("P-1001");

        Assert.Equal(PartnerVerificationResult.Verified, result);
        Assert.Equal(3, handler.Attempts);   // 1 initial + 2 retries
    }

    [Fact]
    public async Task Gives_up_after_the_configured_attempts_and_reports_unavailable()
    {
        var (client, handler) = Build(HttpStatusCode.InternalServerError);

        var result = await client.VerifyAsync("P-1001");

        Assert.Equal(PartnerVerificationResult.Unavailable, result);
        Assert.Equal(4, handler.Attempts);   // 1 initial + 3 retries — and no exception escaped
    }

    [Fact]
    public async Task Does_not_retry_a_business_rejection()
    {
        var (client, handler) = Build(HttpStatusCode.NotFound);

        var result = await client.VerifyAsync("P-9999");

        Assert.Equal(PartnerVerificationResult.NotVerified, result);
        Assert.Equal(1, handler.Attempts);   // 404 is an answer, not a failure
    }

    [Fact]
    public async Task Reports_unavailable_when_the_connection_never_succeeds()
    {
        var handler = new FaultingHandler(() => new HttpRequestException("connection refused"));

        var result = await BuildWith(handler).VerifyAsync("P-1001");

        // The exception must not escape: an unreachable partner is a 503, not a 500.
        Assert.Equal(PartnerVerificationResult.Unavailable, result);
        Assert.Equal(2, handler.Attempts);   // 1 initial + 1 retry
    }

    [Fact]
    public async Task Reports_unavailable_when_every_attempt_times_out()
    {
        // Each attempt hangs well past AttemptTimeout, and the retry runs the total past TotalTimeout.
        var handler = new FaultingHandler(delay: TimeSpan.FromSeconds(30));

        var result = await BuildWith(handler, attemptTimeout: "00:00:00.100", totalTimeout: "00:00:00.400")
                        .VerifyAsync("P-1001");

        Assert.Equal(PartnerVerificationResult.Unavailable, result);
    }

    [Fact]
    public async Task Propagates_caller_cancellation_instead_of_reporting_it_as_unavailable()
    {
        var handler = new FaultingHandler(delay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // The `when (!ct.IsCancellationRequested)` filter exists for exactly this: a client that
        // hung up is not a partner outage, and must not be logged or surfaced as one.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildWith(handler, totalTimeout: "00:00:30").VerifyAsync("P-1001", cts.Token));
    }

    [Theory]
    // A 200 is not a yes. Only isActive:true is a yes — anything else is a business rejection.
    [InlineData("{\"partnerId\":\"P-1001\",\"isActive\":false}")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Treats_a_200_that_is_not_an_active_partner_as_not_verified(string payload)
    {
        var result = await BuildWith(new BodyHandler(payload)).VerifyAsync("P-1001");

        Assert.Equal(PartnerVerificationResult.NotVerified, result);
    }

    [Fact]
    public async Task Reports_unavailable_when_something_other_than_the_caller_cancels()
    {
        // The transport itself throws OperationCanceledException while the caller's token is
        // still live — an inner cancellation nobody asked for. It must land as Unavailable,
        // not escape as a 500. This is what the `when (!ct.IsCancellationRequested)` filter is for.
        var handler = new FaultingHandler(() => new OperationCanceledException());

        var result = await BuildWith(handler).VerifyAsync("P-1001", CancellationToken.None);

        Assert.Equal(PartnerVerificationResult.Unavailable, result);
    }
}
