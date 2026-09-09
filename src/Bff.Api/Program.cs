using Bff.Api.Endpoints;
using Bff.Api.Options;
using Bff.Api.Validation;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// Loading Partner Options configuration
builder.Services.Configure<PartnerOptions>(builder.Configuration.GetSection("Partner"));
builder.Services.AddSingleton<ICurrencyCatalog, ConfiguredCurrencyCatalog>();
builder.Services.AddValidatorsFromAssemblyContaining<PartnerTransactionRequestValidator>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health/live");
app.MapPartnerTransaction();

app.Run();
