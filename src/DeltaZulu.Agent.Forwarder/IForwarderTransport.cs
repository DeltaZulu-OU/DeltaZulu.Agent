namespace DeltaZulu.Agent.Forwarder;

public interface IForwarderTransport
{
    ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default);
}
