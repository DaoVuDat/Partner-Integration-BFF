using Bff.Api.Contracts;
using FluentValidation;

namespace Bff.Api.Endpoints;

public static class PartnerTransactionEndpoints
{
    public static void MapPartnerTransaction(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/partner").WithTags("Partner");

        group.MapPost("/transactions", HandleAsync);

    }

    private static async Task<IResult> HandleAsync(
        PartnerTransactionRequest request,
        IValidator<PartnerTransactionRequest> validator,
        ILogger<PartnerTransactionRequest> logger,
        CancellationToken ct)
    {
        
        // Validate the payload
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        // TODO: verify the partnerId against a mock "Partner Verification API";.
        
        
        
        // TODO: send to the message broker
        
        
        // Response to client with status code 202
        return Results.Accepted(value: new
        {
            request.TransactionReference,
            status = "Accepted"
        });
    }
}