using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DeltaZulu.Forward;
using DeltaZulu.Agent.DoomDisplay.Inputs;
using DeltaZulu.Agent.DoomDisplay.Outputs;

namespace DeltaZulu.Agent.DoomDisplay;

internal static class Program
{
    private const int DefaultPort = 46000;
    private static readonly JsonSerializerOptions ReliabilityReportJsonOptions = new() {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var options = CommandLineOptions.Parse(args);
            return options.Mode switch {
                "collector" => await RunCollectorAsync(options, cancellation.Token).ConfigureAwait(false),
                "forwarder" => await RunForwarderAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => WriteUsage()
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Doom reliability demonstrator failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCollectorAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, options.Port);
        listener.Start();
        Console.Error.WriteLine($"Doom Forward collector listening on 127.0.0.1:{options.Port}.");

        var telemetry = new DoomReliabilityTelemetry();
        using var renderer = new AnsiDoomRenderer();
        using var outputSink = new DoomOutputSink(renderer, telemetry);
        using var collectorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var collectorToken = collectorCancellation.Token;
        var inputDecoder = new DoomInputDecoder();
        var renderLoop = outputSink.RenderUntilCanceledAsync(collectorToken);

        try
        {
            while (!collectorToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(collectorToken).ConfigureAwait(false);
                await HandleCollectorClientAsync(client, inputDecoder, outputSink, telemetry, collectorToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            listener.Stop();
            collectorCancellation.Cancel();
            try
            {
                await renderLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (collectorToken.IsCancellationRequested)
            {
                // Expected during orderly collector shutdown.
            }

            WriteReliabilityReport("collector", options, telemetry.Snapshot(), outputSink.Metrics);
        }

        return 0;
    }

    private static async Task HandleCollectorClientAsync(
        TcpClient client,
        IDoomInputDecoder inputDecoder,
        IDoomOutputSink outputSink,
        DoomReliabilityTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        await using var connection = ForwardConnection.FromAcceptedClient(client);
        var sessionOptions = new ForwardSessionOptions {
            CatalogVersion = "doom-frame-v1",
            RequestedWindowSize = 4,
            DedupWindowSize = 128,
            MaxFrameLength = DoomFrameCodec.MaximumPixelBytes + 8192,
            BatchHandler = (frameType, _, payload, _) =>
                Task.FromResult(HandleFrame(frameType, payload, inputDecoder, outputSink, telemetry))
        };

        await using var session = await ForwardSession.AcceptAsync(
            connection,
            offer => new ForwardHandshakeAck(
                Accepted: true,
                ProtocolVersion: offer.ProtocolVersion,
                SessionId: offer.SessionResumeToken == Guid.Empty ? Guid.NewGuid() : offer.SessionResumeToken,
                GrantedWindowSize: Math.Min(offer.RequestedWindowSize, sessionOptions.RequestedWindowSize),
                DedupWindowSize: Math.Min(offer.DedupWindowSize, sessionOptions.DedupWindowSize),
                CompressionSelected: ForwardCompression.None,
                UnknownSchemaFingerprints: [],
                RejectReason: string.Empty),
            sessionOptions,
            cancellationToken).ConfigureAwait(false);

        if (session.ReceiveLoopCompletion is not null)
        {
            await session.ReceiveLoopCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static ForwardAckOutcome HandleFrame(
        ForwardFrameType frameType,
        byte[] payload,
        IDoomInputDecoder inputDecoder,
        IDoomOutputSink outputSink,
        DoomReliabilityTelemetry telemetry)
    {
        if (frameType != ForwardFrameType.RawEnvelope)
        {
            telemetry.RecordCollectorRejected();
            return new ForwardAckOutcome(2, $"Expected RawEnvelope, received {frameType}.");
        }

        try
        {
            outputSink.Write(inputDecoder.Decode(payload));
            return new ForwardAckOutcome(0, null);
        }
        catch (DoomInputDecodeException exception)
        {
            telemetry.RecordCollectorRejected();
            return new ForwardAckOutcome(1, exception.Message);
        }
    }

    private static async Task<int> RunForwarderAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var source = new SyntheticDoomFrameSource(options.Width, options.Height);
        var telemetry = new DoomReliabilityTelemetry();
        var frameInterval = TimeSpan.FromSeconds(1d / options.FramesPerSecond);
        long sequence = 0;
        long framesStarted = 0;
        var connectionCount = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                (options.FrameCount == 0 || framesStarted < options.FrameCount))
            {
                try
                {
                    await using var connection = new ForwardConnection(options.Host, options.Port);
                    await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    await using var session = new ForwardSession(connection, new ForwardSessionOptions {
                        CatalogVersion = "doom-frame-v1",
                        RequestedWindowSize = 4,
                        DedupWindowSize = 128,
                        MaxFrameLength = DoomFrameCodec.MaximumPixelBytes + 8192
                    });

                    await session.OpenAsync(cancellationToken).ConfigureAwait(false);
                    if (connectionCount++ > 0)
                    {
                        telemetry.RecordReconnect();
                    }

                    Console.Error.WriteLine(
                        $"Doom Forward sender connected to {options.Host}:{options.Port}; " +
                        $"{options.Width}x{options.Height} at {options.FramesPerSecond} FPS, " +
                        $"maximum in-flight {options.MaximumInFlight}.");

                    var pending = new List<Task>(options.MaximumInFlight);
                    long framesInCurrentConnection = 0;
                    while (!cancellationToken.IsCancellationRequested &&
                        (options.FrameCount == 0 || framesStarted < options.FrameCount) &&
                        (options.DisconnectEveryFrames == 0 ||
                            framesInCurrentConnection < options.DisconnectEveryFrames))
                    {
                        var frameStarted = Stopwatch.GetTimestamp();
                        var packet = source.Capture(sequence++);
                        var payload = DoomFrameCodec.Encode(packet);
                        pending.Add(SendInstrumentedAsync(session, payload, telemetry, cancellationToken));
                        framesStarted++;
                        framesInCurrentConnection++;

                        if (pending.Count >= options.MaximumInFlight)
                        {
                            await AwaitOnePendingAsync(pending).ConfigureAwait(false);
                        }

                        var delay = frameInterval - Stopwatch.GetElapsedTime(frameStarted);
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await Task.WhenAll(pending).ConfigureAwait(false);
                    if (options.FrameCount != 0 && framesStarted >= options.FrameCount)
                    {
                        break;
                    }

                    if (options.DisconnectEveryFrames > 0)
                    {
                        Console.Error.WriteLine(
                            $"Controlled disruption after {framesStarted} frames; closing session before reconnecting.");
                    }
                }
                catch (Exception exception) when (IsRecoverableTransportFailure(exception, cancellationToken))
                {
                    telemetry.RecordReconnect();
                    Console.Error.WriteLine(
                        $"Forward session interrupted after {framesStarted} started frames: {exception.Message}. " +
                        $"Retrying in {options.RetryDelayMilliseconds} ms.");
                    await Task.Delay(options.RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            return 0;
        }
        finally
        {
            WriteReliabilityReport("forwarder", options, telemetry.Snapshot(), null);
        }
    }

    private static async Task SendInstrumentedAsync(
        ForwardSession session,
        byte[] payload,
        DoomReliabilityTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        telemetry.RecordSendStarted();
        var started = Stopwatch.GetTimestamp();
        try
        {
            _ = await session.SendRawEnvelopeAsync(payload, cancellationToken).ConfigureAwait(false);
            telemetry.RecordSendAcknowledged(Stopwatch.GetElapsedTime(started));
        }
        catch
        {
            telemetry.RecordSendFailed();
            throw;
        }
    }

    private static async Task AwaitOnePendingAsync(List<Task> pending)
    {
        var completed = await Task.WhenAny(pending).ConfigureAwait(false);
        pending.Remove(completed);
        await completed.ConfigureAwait(false);
    }

    private static bool IsRecoverableTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is IOException or SocketException or ObjectDisposedException;

    private static void WriteReliabilityReport(
        string role,
        CommandLineOptions options,
        DoomReliabilityMetrics telemetry,
        FrameBufferMetrics? frameBuffer)
    {
        var report = new {
            Role = role,
            GeneratedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Telemetry = telemetry,
            FrameBuffer = frameBuffer
        };
        var json = JsonSerializer.Serialize(report, ReliabilityReportJsonOptions);
        Console.Error.WriteLine($"Doom Forward reliability report ({role}):\n{json}");
        if (!string.IsNullOrWhiteSpace(options.MetricsJsonPath))
        {
            File.WriteAllText(options.MetricsJsonPath, json);
        }
    }

    private static int WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  collector [--port 46000] [--metrics-json collector.json]\n" +
            "  forwarder [--host 127.0.0.1] [--port 46000] [--width 160] [--height 100] [--fps 15]\n" +
            "            [--frames 0] [--max-in-flight 4] [--disconnect-every 0]\n" +
            "            [--retry-delay-ms 1000] [--metrics-json forwarder.json]\n\n" +
            "--frames creates a bounded benchmark. --disconnect-every deliberately closes and reopens\n" +
            "the Forward session after that many acknowledged frames. The sender reports application-level\n" +
            "ack latency and in-flight pressure; the collector reports continuity, display pressure, and age.\n" +
            "The forwarder generates a procedural Doom-style test scene. Replace SyntheticDoomFrameSource\n" +
            "with a licensed source-port adapter for real game frames.");
        return 2;
    }

    private sealed record CommandLineOptions(
        string Mode,
        string Host,
        int Port,
        int Width,
        int Height,
        int FramesPerSecond,
        long FrameCount,
        int MaximumInFlight,
        long DisconnectEveryFrames,
        int RetryDelayMilliseconds,
        string? MetricsJsonPath)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            if (args.Length == 0)
            {
                return CreateDefaults("help");
            }

            var options = CreateDefaults(args[0].ToLowerInvariant());
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for option {args[index]}.");
                }

                var value = args[index + 1];
                options = args[index] switch {
                    "--host" => options with { Host = value },
                    "--port" => options with { Port = ParseInteger(value, "port", 1, 65535) },
                    "--width" => options with { Width = ParseInteger(value, "width", 1, DoomFrameCodec.MaximumWidth) },
                    "--height" => options with { Height = ParseInteger(value, "height", 1, DoomFrameCodec.MaximumHeight) },
                    "--fps" => options with { FramesPerSecond = ParseInteger(value, "fps", 1, 30) },
                    "--frames" => options with { FrameCount = ParseLong(value, "frames", 0, 10_000_000) },
                    "--max-in-flight" => options with { MaximumInFlight = ParseInteger(value, "max-in-flight", 1, 64) },
                    "--disconnect-every" => options with { DisconnectEveryFrames = ParseLong(value, "disconnect-every", 0, 10_000_000) },
                    "--retry-delay-ms" => options with { RetryDelayMilliseconds = ParseInteger(value, "retry-delay-ms", 1, 60_000) },
                    "--metrics-json" => options with { MetricsJsonPath = value },
                    _ => throw new ArgumentException($"Unknown option {args[index]}.")
                };
            }

            if (options.Mode is not "collector" and not "forwarder")
            {
                throw new ArgumentException($"Unknown mode {options.Mode}.");
            }

            return options;
        }

        private static CommandLineOptions CreateDefaults(string mode) => new(
            mode,
            IPAddress.Loopback.ToString(),
            DefaultPort,
            160,
            100,
            15,
            FrameCount: 0,
            MaximumInFlight: 4,
            DisconnectEveryFrames: 0,
            RetryDelayMilliseconds: 1_000,
            MetricsJsonPath: null);

        private static int ParseInteger(string value, string name, int minimum, int maximum)
        {
            if (!int.TryParse(value, out var result) || result < minimum || result > maximum)
            {
                throw new ArgumentException($"{name} must be an integer from {minimum} through {maximum}.");
            }

            return result;
        }

        private static long ParseLong(string value, string name, long minimum, long maximum)
        {
            if (!long.TryParse(value, out var result) || result < minimum || result > maximum)
            {
                throw new ArgumentException($"{name} must be an integer from {minimum} through {maximum}.");
            }

            return result;
        }
    }
}
