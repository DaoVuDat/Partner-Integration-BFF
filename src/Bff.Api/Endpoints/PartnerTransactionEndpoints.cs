using Bff.Api.Contracts;
using Bff.Api.Partners;
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
        IPartnerVerificationClient verificationClient,
        ILogger<PartnerTransactionRequest> logger,
        CancellationToken ct)
    {
        
        // Validate the payload
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }
        
        // Verify this partnerId
        var verification = await verificationClient.VerifyAsync(request.PartnerId!, ct);
        
        if (verification is PartnerVerificationResult.Unavailable)
        {
            logger.LogWarning("Verification unavailable for {PartnerId}", request.PartnerId);
            return Results.Problem(
                title: "Partner verification is temporarily unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (verification is PartnerVerificationResult.NotVerified)
            return Results.Problem(
                title: "Partner is not verified.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        
        
        // TODO: send to the message broker
        
        
        // Response to client with status code 202
        return Results.Accepted(value: new
        {
            request.TransactionReference,
            status = "Accepted"
        });
    }
}