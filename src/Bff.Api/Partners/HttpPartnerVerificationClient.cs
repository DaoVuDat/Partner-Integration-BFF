using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Bff.Api.Partners;

public sealed class HttpPartnerVerificationClient(HttpClient http, ILogger<HttpPartnerVerificationClient> log): IPartnerVerificationClient
{
    
    
    public async Task<PartnerVerificationResult> VerifyAsync(string partnerId, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync($"partners/{Uri.EscapeDataString(partnerId)}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return PartnerVerificationResult.NotVerified;
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Verification failed with {Status} for {PartnerId}", response.StatusCode, partnerId);
                return PartnerVerificationResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<PartnerVerificationResponse>(ct);
            return body?.IsActive == true
                ? PartnerVerificationResult.Verified
                : PartnerVerificationResult.NotVerified;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelled by something other than the caller — treat as a timeout, not a crash.
            return PartnerVerificationResult.Unavailable;
        }
        // TimeoutRejectedException is how the Polly pipeline surfaces both the per-attempt and
        // the total timeout; it is NOT an OperationCanceledException, so the filter above misses it.
        catch (Exception ex) when (ex is HttpRequestException or BrokenCircuitException or TimeoutRejectedException)
        {
            log.LogWarning(ex, "Verification unavailable for {PartnerId}", partnerId);
            return PartnerVerificationResult.Unavailable;
        }
    }
}