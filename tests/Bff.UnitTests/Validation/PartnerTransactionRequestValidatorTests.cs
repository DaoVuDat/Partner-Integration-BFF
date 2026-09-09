using Bff.Api.Infrastructure.Options;
using Bff.UnitTests.TestSupport;

namespace Bff.UnitTests.Validation;

public class PartnerTransactionRequestValidatorTests
{
    private static PartnerTransactionRequestValidator CreateSut() =>
        new(new ConfiguredCurrencyCatalog(
            Options.Create(new PartnerOptions { SupportedCurrencies = ["USD", "VND", "EUR"] })));

    [Fact]
    public void Accepts_a_well_formed_request()
    {
        var result = CreateSut().Validate(PartnerTransactionRequestFactory.Valid());

        Assert.True(result.IsValid);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Rejects_non_positive_amounts(decimal amount) =>
        Assert.False(CreateSut().Validate(PartnerTransactionRequestFactory.Valid() with { Amount = amount }).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")]
    [InlineData("XYZ")] [InlineData("US")] [InlineData("DOLLAR")]
    public void Rejects_unsupported_currency(string? currency) =>
        Assert.False(CreateSut().Validate(PartnerTransactionRequestFactory.Valid() with { Currency = currency }).IsValid);

    [Fact]
    public void Accepts_currency_case_insensitively() =>
        Assert.True(CreateSut().Validate(PartnerTransactionRequestFactory.Valid() with { Currency = "usd" }).IsValid);

    [Fact]
    public void Rejects_missing_partner_id() =>
        Assert.False(CreateSut().Validate(PartnerTransactionRequestFactory.Valid()with { PartnerId = null }).IsValid);
    
}
