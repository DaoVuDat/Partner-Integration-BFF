using System.Text.Json.Serialization;

namespace Bff.Api.Partners;

public sealed record PartnerVerificationResponse(
    [property: JsonPropertyName("partnerId")]  string PartnerId,
    [property: JsonPropertyName("isActive")]   bool IsActive,
    [property: JsonPropertyName("verifiedAt")] DateTimeOffset VerifiedAt);