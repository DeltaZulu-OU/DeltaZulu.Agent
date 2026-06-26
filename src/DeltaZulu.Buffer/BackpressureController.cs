using DeltaZulu.Buffer.Abstractions;
using DeltaZulu.Buffer.Configuration;

namespace DeltaZulu.Buffer;

internal sealed class BackpressureController
{
    private readonly DeltaZuluBufferOptions _options;
    private const double PressureThreshold = 0.85;

    public BackpressureController(DeltaZuluBufferOptions options)
    {
        _options = options;
    }

    public (BufferState State, bool ShouldAccept) Evaluate(
        long diskBytesUsed,
        long memoryBytesUsed,
        int retryQueueDepth)
    {
        if (diskBytesUsed >= _options.MaxDiskBytes || memoryBytesUsed >= _options.MaxMemoryBytes)
        {
            return (BufferState.Full, false);
        }

        var diskRatio = _options.MaxDiskBytes > 0
            ? (double)diskBytesUsed / _options.MaxDiskBytes
            : 0;
        var memRatio = _options.MaxMemoryBytes > 0
            ? (double)memoryBytesUsed / _options.MaxMemoryBytes
            : 0;

        if (diskRatio > PressureThreshold || memRatio > PressureThreshold)
        {
            return (BufferState.Pressured, true);
        }

        if (retryQueueDepth > 0)
        {
            return (BufferState.Degraded, true);
        }

        return (BufferState.Healthy, true);
    }
}