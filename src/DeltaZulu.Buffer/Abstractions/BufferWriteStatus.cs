namespace DeltaZulu.Buffer.Abstractions;

public enum BufferWriteStatus
{
    Accepted,
    RejectedBufferFull,
    RejectedRecordTooLarge,
    RejectedStopping,
    DroppedOldestAndAccepted
}