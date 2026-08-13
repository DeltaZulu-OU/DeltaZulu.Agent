using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Inputs.Auditd;
using MessagePack;
using SharpFuzz;

if (args.Length != 1)
{
    throw new ArgumentException("Specify exactly one fuzz target: Auditd or Forward.");
}

switch (args[0])
{
    case "Auditd":
        Fuzzer.Run(FuzzAuditd);
        break;
    case "Forward":
        Fuzzer.Run(stream =>
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            FuzzForwardBatch(buffer.ToArray());
        });
        break;
    default:
        throw new ArgumentException($"Unknown fuzz target '{args[0]}'. Expected Auditd or Forward.");
}

static void FuzzAuditd(string line)
{
    try
    {
        _ = new AuditdRecordParser().Parse(line);
    }
    catch (FormatException exception)
    {
        Console.Error.WriteLine($"Expected Auditd format rejection: {exception.Message}");
    }
}

static void FuzzForwardBatch(ReadOnlyMemory<byte> payload)
{
    try
    {
        _ = ForwardLogBatchCodec.Decode(payload);
    }
    catch (Exception exception) when (
        exception is InvalidDataException or FormatException or MessagePackSerializationException)
    {
        Console.Error.WriteLine(
            $"Expected Forward format rejection ({exception.GetType().Name}): {exception.Message}");
    }
}
