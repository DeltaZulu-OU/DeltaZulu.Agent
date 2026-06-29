using System.Collections;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.MessagePack;
using DeltaZulu.Pipeline.Core.Relp;
using MessagePack;

namespace DeltaZulu.Pipeline.Inputs.Relp;

public sealed record RelpInputConfiguration
{
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 2514;
    public bool UseTls { get; init; }
    public string? ServerCertificatePath { get; init; }
    public string? ServerCertificatePassword { get; init; }
}

public sealed class RelpInput : ISourceInput
{
    private readonly MessagePackPayloadSerializer _serializer = new();
    private readonly RelpInputConfiguration _configuration;
    private readonly X509Certificate2? _serverCertificate;

    public string Name { get; }

    public RelpInput(RelpInputConfiguration configuration, string name = "relp-input")
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Name = name;
        Validate(configuration);
        _serverCertificate = LoadServerCertificate(configuration);
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var listener = new TcpListener(IPAddress.Parse(_configuration.Address), _configuration.Port);
        listener.Start();

        _ = Task.Run(async () => {
            try
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, observer, linkedCts.Token), linkedCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }
        }, linkedCts.Token);

        return Disposable.Create(() => {
            linkedCts.Cancel();
            listener.Stop();
            linkedCts.Dispose();
        });
    });

    private async Task HandleClientAsync(TcpClient client, IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        using var clientRegistration = client;
        try
        {
            await using var stream = await OpenStreamAsync(client, cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var maybeFrame = await RelpFrameCodec.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (maybeFrame is not { } frame)
                {
                    return;
                }

                if (frame.Command.Equals("open", StringComparison.OrdinalIgnoreCase))
                {
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (frame.Command.Equals("close", StringComparison.OrdinalIgnoreCase))
                {
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!frame.Command.Equals("syslog", StringComparison.OrdinalIgnoreCase))
                {
                    await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "500 unsupported command", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var accepted = PublishPayload(frame.Payload, observer, out var errorMessage);
                await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, accepted ? "200 OK" : $"500 {errorMessage}", cancellationToken).ConfigureAwait(false);
            }
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

    private async ValueTask<Stream> OpenStreamAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var network = client.GetStream();
        if (!_configuration.UseTls)
        {
            return network;
        }

        var ssl = new SslStream(network, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = _serverCertificate,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, cancellationToken).ConfigureAwait(false);
        return ssl;
    }

    private bool PublishPayload(ReadOnlyMemory<byte> payload, IObserver<SourceEvent> observer, out string errorMessage)
    {
        try
        {
            var batch = _serializer.Deserialize<DeliveryBatch>(payload);
            if (batch is null)
            {
                errorMessage = "invalid MessagePack DeliveryBatch: payload decoded to null";
                Console.Error.WriteLine($"RELP input rejected {payload.Length} byte payload: {errorMessage}.");
                Console.Error.Flush();
                return false;
            }

            foreach (var record in batch.Records)
            {
                observer.OnNext(ToSourceEvent(record));
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (MessagePackSerializationException ex)
        {
            errorMessage = $"invalid MessagePack DeliveryBatch: {SanitizeErrorMessage(ex.Message)}";
            Console.Error.WriteLine($"RELP input rejected {payload.Length} byte payload: {errorMessage}");
            Console.Error.Flush();
            return false;
        }
    }

    private static string SanitizeErrorMessage(string message) => message.Replace('\r', ' ').Replace('\n', ' ');

    private SourceEvent ToSourceEvent(DeliveryRecord deliveryRecord)
    {
        var record = deliveryRecord.Record;
        var metadata = new ResourceMetadata {
            CollectorId = GetString(record.Metadata, "collectorId") ?? deliveryRecord.AgentId,
            ProfileId = GetString(record.Metadata, "profileId") ?? deliveryRecord.ProfileId,
            ProfileVersion = GetString(record.Metadata, "profileVersion"),
            SourceType = GetString(record.Metadata, "sourceType") ?? "Relp",
            SourceName = GetString(record.Metadata, "sourceName") ?? deliveryRecord.SourceId,
            Platform = GetString(record.Metadata, "platform") ?? "portable",
            Hostname = GetString(record.Metadata, "hostname") ?? deliveryRecord.AgentId,
            IngestedAt = DateTimeOffset.UtcNow,
            ParserName = nameof(RelpInput),
            RawPreserved = true,
            Properties = new Dictionary<string, object?> {
                ["relp.deliveryId"] = deliveryRecord.DeliveryId,
                ["relp.recordId"] = deliveryRecord.RecordId,
                ["relp.createdAt"] = deliveryRecord.CreatedAt
            }
        };

        return new SourceEvent(metadata, new Dictionary<string, object?>(record.Event, StringComparer.OrdinalIgnoreCase));
    }

    private static X509Certificate2? LoadServerCertificate(RelpInputConfiguration configuration) => !configuration.UseTls
            ? null
            : string.IsNullOrEmpty(configuration.ServerCertificatePassword)
            ? X509CertificateLoader.LoadCertificateFromFile(configuration.ServerCertificatePath!)
            : X509CertificateLoader.LoadPkcs12FromFile(configuration.ServerCertificatePath!, configuration.ServerCertificatePassword);

    private static void Validate(RelpInputConfiguration configuration)
    {
        if (configuration.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Port));
        }

        if (configuration.UseTls && string.IsNullOrWhiteSpace(configuration.ServerCertificatePath))
        {
            throw new InvalidDataException("RELP/TLS input requires serverCertificatePath.");
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key) => !fields.TryGetValue(key, out var value) || value is null
            ? null
            : value switch {
                string text when !string.IsNullOrWhiteSpace(text) => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                JsonElement element when element.ValueKind is JsonValueKind.Object or JsonValueKind.Array => null,
                DateTimeOffset timestamp => timestamp.ToString("O"),
                DateTime timestamp => timestamp.ToString("O"),
                IDictionary => null,
                IEnumerable when value is not string => null,
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            };
}