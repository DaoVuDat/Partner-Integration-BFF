using Bff.Api.Options;
using Microsoft.Extensions.Options;

namespace Bff.Api.Validation;

public interface ICurrencyCatalog
{
    bool IsSupport(string currency);
}

public sealed class ConfiguredCurrencyCatalog(IOptions<PartnerOptions> options) : ICurrencyCatalog
{
    private readonly HashSet<string> _supported = new(options.Value.SupportedCurrencies, StringComparer.OrdinalIgnoreCase);

    public bool IsSupport(string currency) => currency.Length == 3 && _supported.Contains(currency);
}