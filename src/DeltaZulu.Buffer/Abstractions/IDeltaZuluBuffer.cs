namespace DeltaZulu.Buffer.Abstractions;

public interface IDeltaZuluBuffer<T>
{
    ValueTask<BufferWriteResult> WriteAsync(
        T record,
        CancellationToken cancellationToken = default);

    ValueTask FlushAsync(
        CancellationToken cancellationToken = default);

    BufferSnapshot GetSnapshot();
}