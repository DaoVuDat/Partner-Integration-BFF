using System.Net;
using Bff.Api.Infrastructure.Options;
using Bff.Api.Partners;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace Bff.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPartnerVerification(this IServiceCollection services, IConfiguration config)
    {   
        services.AddOptions<VerificationOptions>()
            .Bind(config.GetSection(VerificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPartnerVerificationClient, HttpPartnerVerificationClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<VerificationOptions>>().Value;
            client.BaseAddress = new Uri(o.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;   // use timeouts, not HttpClient
        })
        .AddResilienceHandler("partner-verification", (pipeline, context) =>
        {
            var o = context.ServiceProvider.GetRequiredService<IOptions<VerificationOptions>>().Value;
            var log = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                             .CreateLogger("PartnerVerification.Resilience");

            pipeline
                // 1. OUTERMOST: total for the whole operation, retries included.
                .AddTimeout(o.TotalTimeout)

                // 2. Retry transient failures.
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = o.MaxRetryAttempts,
                    Delay           = o.BaseDelay,
                    BackoffType     = DelayBackoffType.Exponential,
                    UseJitter       = true,
                    ShouldHandle    = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TimeoutRejectedException>()
                        .HandleResult(r => (int)r.StatusCode >= 500
                                           || r.StatusCode == HttpStatusCode.RequestTimeout),
                    OnRetry = args =>
                    {
                        log.LogWarning("Retry {Attempt} after {Delay}", args.AttemptNumber + 1, args.RetryDelay);
                        return default;
                    }
                })

                // 3. INNERMOST: per-attempt timeout
                .AddTimeout(o.AttemptTimeout);
        });

        return services;
    }
}