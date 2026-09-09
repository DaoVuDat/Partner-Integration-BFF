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
}
