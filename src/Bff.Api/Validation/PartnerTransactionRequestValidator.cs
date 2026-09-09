using Bff.Api.Contracts;
using FluentValidation;

namespace Bff.Api.Validation;

public sealed class PartnerTransactionRequestValidator: AbstractValidator<PartnerTransactionRequest>
{
    public PartnerTransactionRequestValidator(ICurrencyCatalog currencies)
    {
        RuleFor(x => x.PartnerId)
            .NotEmpty().WithMessage("partnerId is required")
            .MaximumLength(10);
        
        RuleFor(x => x.TransactionReference)
            .NotEmpty().WithMessage("transactionReference is required")
            .MaximumLength(10);

        RuleFor(x => x.Amount)
            .NotNull().WithMessage("amount is required")
            .GreaterThan(0m).WithMessage("amount must be greater than zero")
            .Must(a => a is null || decimal.Round(a.Value, 2) == a.Value)
            .WithMessage("amount may not have more than 2 decimal places");
        
        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("currency is required")
            .Must(c => c is not null && currencies.IsSupport(c)).WithMessage("currency must be support");

        RuleFor(x => x.Timestamp)
            .NotNull().WithMessage("timestamp is required");
    }
}
