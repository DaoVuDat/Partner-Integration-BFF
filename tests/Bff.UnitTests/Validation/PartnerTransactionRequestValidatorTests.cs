using Bff.UnitTests.TestSupport;

namespace Bff.UnitTests.Validation;

public class PartnerTransactionRequestValidatorTests
{
    private static PartnerTransactionRequestValidator CreateSut() =>
        new(new ConfiguredCurrencyCatalog(
            Options.Create(new PartnerOptions { SupportedCurrencies = ["USD", "EUR"] })));

    [Fact]
    public void Accepts_a_well_formed_request()
    {
        var result = CreateSut().Validate(PartnerTransactionRequestFactory.Valid());

        Assert.True(result.IsValid);
    }
}
