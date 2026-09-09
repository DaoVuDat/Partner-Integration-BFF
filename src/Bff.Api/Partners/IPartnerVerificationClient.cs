namespace Bff.Api.Partners;

public enum PartnerVerificationResult { Verified, NotVerified, Unavailable }


public interface IPartnerVerificationClient
{
    Task<PartnerVerificationResult> VerifyAsync(string partnerId, CancellationToken ct = default);
}