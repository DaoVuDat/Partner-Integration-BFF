using Bff.Api.Contracts;

namespace Bff.Api.Message;

public interface ITransactionPublisher
{
    Task PublishAsync(PartnerTransactionAccepted message, CancellationToken ct = default);
}
