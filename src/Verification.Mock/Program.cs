var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/partners/{partnerId}", (string partnerId, ILogger<Program> log) =>
{
    // throw a TimeoutException 30% of the time,
    // return valid response 70% of the time
    if (Random.Shared.NextDouble() < 0.30)
    {
        log.LogWarning("Simulated timeout for {PartnerId}", partnerId);
        throw new TimeoutException($"Simulated timeout for {partnerId}");
    }

    return Results.Ok(new {partnerId, isActive = true, verifiedAt = DateTimeOffset.UtcNow});
});

app.Run();
