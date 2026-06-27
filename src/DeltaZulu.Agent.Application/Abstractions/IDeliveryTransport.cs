using DeltaZulu.Agent.Domain.Delivery;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface IDeliveryTransport
{
    ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default);
}
