using System.ComponentModel.DataAnnotations;

namespace Bff.Api.Infrastructure.Options;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";
    
    [Required, Url]
    public string BaseUrl { get; init; } = "";

    public TimeSpan TotalTimeout   { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(1);

    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
}