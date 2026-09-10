using System.ComponentModel.DataAnnotations;

namespace Bff.Api.Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string ConnectionString { get; init; } = "amqp://guest:guest@localhost:5672";

    [Required]
    public string Exchange { get; init; } = "partner.transactions";

    [Required]
    public string Queue { get; init; } = "partner.transactions.accepted";

    [Required]
    public string RoutingKey { get; init; } = "transaction.accepted";
}
