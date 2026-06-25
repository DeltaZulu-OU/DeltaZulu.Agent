namespace DeltaZulu.Buffer.Abstractions;

public readonly record struct BufferWriteResult(BufferWriteStatus Status)
{
    public bool IsAccepted => Status is BufferWriteStatus.Accepted or BufferWriteStatus.DroppedOldestAndAccepted;
}