using Bff.Api.Endpoints;
using Bff.Api.Infrastructure;
using Bff.Api.Infrastructure.Options;
using Bff.Api.Message;
using Bff.Api.Validation;
using FluentValidation;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton(TimeProvider.System);

// Loading Partner Options configuration
builder.Services.Configure<PartnerOptions>(builder.Configuration.GetSection("Partner"));
builder.Services.AddSingleton<ICurrencyCatalog, ConfiguredCurrencyCatalog>();
builder.Services.AddValidatorsFromAssemblyContaining<PartnerTransactionRequestValidator>();

// Verification Configuration
builder.Services.AddPartnerVerification(builder.Configuration);

// Message queue Configuration
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var o = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    return new ConnectionFactory
    {
        Uri = new Uri(o.ConnectionString),
        AutomaticRecoveryEnabled = true
    };
});
builder.Services.AddSingleton<ITransactionPublisher, RabbitMqTransactionPublisher>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health/live");
app.MapPartnerTransaction();

app.Run();
