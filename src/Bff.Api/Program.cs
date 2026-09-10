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
builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); // Adding Global Exception Handler
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();   // partners consume this BFF, so the contract is published

builder.Services.AddSingleton(TimeProvider.System);

// Loading Partner Options configuration.
builder.Services.AddOptions<PartnerOptions>()
    .Bind(builder.Configuration.GetSection(PartnerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ICurrencyCatalog, ConfiguredCurrencyCatalog>();
builder.Services.AddValidatorsFromAssemblyContaining<PartnerTransactionRequestValidator>();

// Verification Configuration
builder.Services.AddPartnerVerification(builder.Configuration);

// Message queue Configuration
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
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

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health/live");
app.MapPartnerTransaction();

app.Run();
