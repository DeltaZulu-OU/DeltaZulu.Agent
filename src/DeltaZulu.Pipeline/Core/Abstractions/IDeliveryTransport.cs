using DeltaZulu.Pipeline.Core.Delivery;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IDeliveryTransport
{
    ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default);
}
