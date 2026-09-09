using System.ComponentModel.DataAnnotations;

namespace Bff.Api.Infrastructure.Options;

public sealed class PartnerOptions
{
    public const string SectionName = "Partner";
    
    [Required, MinLength(1)]
    public string[] SupportedCurrencies { get; set; } = [];
}