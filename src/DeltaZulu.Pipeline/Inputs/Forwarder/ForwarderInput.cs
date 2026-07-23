using System.Net;
using System.Net.Sockets;
using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.Common;

namespace DeltaZulu.Pipeline.Inputs.Forwarder;

public sealed record ForwarderInputConfiguration
{
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 2514;
    public bool UseTls { get; init; }
    public string? ServerCertificatePath { get; init; }
    public string? ServerCertificatePassword { get; init; }
}

public sealed class ForwarderInput : ISourceInput
{
    private readonly ForwarderInputConfiguration _configuration;

    public ForwarderInput(ForwarderInputConfiguration configuration, string name = "forwarder-input")
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Name = name;
        Validate(configuration);
    }

    public string Name { get; }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
        TcpListenerSourceInput.Create(
            createListener: () => {
                var listener = new TcpListener(IPAddress.Parse(_configuration.Address), _configuration.Port);
                try
                {
                    listener.Start();
                }
                catch (SocketException ex)
                {
                    listener.Stop();
                    throw CreateListenerStartException(ex);
                }

                return listener;
            },
            handleClientAsync: HandleClientAsync,
            cancellationToken);

    private static string SanitizeErrorMessage(string message) => message.Replace('\r', ' ').Replace('\n', ' ');

    private static void Validate(ForwarderInputConfiguration configuration)
    {
        if (configuration.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Port));
        }

        if (configuration.UseTls)
        {
            throw new InvalidDataException("DeltaZulu.Forward input does not support the legacy TLS stream wrapper in this Agent integration path.");
        }
    }

    private InvalidOperationException CreateListenerStartException(SocketException ex)
    {
        var endpoint = $"{_configuration.Address}:{_configuration.Port}";
        var message = ex.SocketErrorCode == SocketError.AccessDenied
            ? $"FORWARDER input could not bind to {endpoint} because the operating system denied access to the socket. "
                + "Choose another forwarderInput.port/address or remove the OS port reservation/firewall policy that blocks this endpoint. "
                + "On Windows, check excluded TCP port ranges with: netsh interface ipv4 show excludedportrange protocol=tcp."
            : $"FORWARDER input could not bind to {endpoint}: {ex.Message}";

        return new InvalidOperationException(message, ex);
    }

    private async Task HandleClientAsync(TcpClient client, IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = ForwardConnection.FromAcceptedClient(client);
            var options = new ForwardSessionOptions {
                BatchHandler = (frameType, batchId, payload, ct) => Task.FromResult(frameType switch {
                    ForwardFrameType.TypedBatch => PublishTypedBatch(payload, observer, out var errorMessage)
                        ? new ForwardAckOutcome(0, null)
                        : new ForwardAckOutcome(1, errorMessage),
                    // Unparsed bytes for a source parsed at the collector tier -- not this input's job to decode.
                    ForwardFrameType.RawEnvelope => new ForwardAckOutcome(0, null),
                    _ => new ForwardAckOutcome(2, $"Unsupported batch frame type {frameType}")
                })
            };

            await using var session = await ForwardSession.AcceptAsync(
                connection,
                offer => new ForwardHandshakeAck(
                    Accepted: true,
                    ProtocolVersion: offer.ProtocolVersion,
                    SessionId: Guid.NewGuid(),
                    GrantedWindowSize: offer.RequestedWindowSize,
                    DedupWindowSize: offer.DedupWindowSize,
                    CompressionSelected: offer.CompressionOffered,
                    UnknownSchemaFingerprints: [],
                    RejectReason: string.Empty),
                options,
                cancellationToken).ConfigureAwait(false);

            await session.ReceiveLoopCompletion!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            // The peer can close the connection while the input is shutting down.
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private bool PublishTypedBatch(ReadOnlyMemory<byte> payload, IObserver<SourceEvent> observer, out string errorMessage)
    {
        try
        {
            var batch = ForwardLogBatchCodec.Decode(payload);
            foreach (var record in batch.Records)
            {
                observer.OnNext(ToSourceEvent(record));
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorMessage = $"invalid ForwardLogBatch: {SanitizeErrorMessage(ex.Message)}";
            Console.Error.WriteLine($"FORWARDER input rejected {payload.Length} byte payload: {errorMessage}");
            Console.Error.Flush();
            return false;
        }
    }

    private static SourceEvent ToSourceEvent(ForwardLogRecord record)
    {
        var metadata = new ResourceMetadata {
            CollectorId = record.AgentId,
            ProfileId = record.ProfileId,
            ProfileVersion = record.ProfileVersion,
            SourceType = record.SourceType,
            SourceName = record.SourceName,
            Platform = record.Platform ?? "portable",
            Hostname = record.Hostname ?? record.AgentId,
            IngestedAt = DateTimeOffset.UtcNow,
            ParserName = nameof(ForwarderInput),
            RawPreserved = true,
            Properties = new Dictionary<string, object?> {
                ["forwarder"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["deliveryID"] = record.DeliveryId,
                    ["recordId"] = record.RecordId,
                    ["createdAt"] = record.CreatedAt
                }
            }
        };

        return new SourceEvent(metadata, new Dictionary<string, object?>(record.Fields, StringComparer.OrdinalIgnoreCase));
    }
}
