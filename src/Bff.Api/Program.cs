using Bff.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// Loading Partner Options configuration
builder.Services.Configure<PartnerOptions>(builder.Configuration.GetSection("Partner"));


var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
