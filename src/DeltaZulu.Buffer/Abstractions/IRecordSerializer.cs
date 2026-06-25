namespace DeltaZulu.Buffer.Abstractions;

public interface IRecordSerializer<in T>
{
    ReadOnlyMemory<byte> Serialize(T record);
}