using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DeltaZulu.Agent.Daemon;
using DeltaZulu.Pipeline.Tunnel;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class TunnelTests
{
    [TestMethod]
    public async Task FileTunnelCertificateProvider_LoadsPkcs12ClientCertificate()
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "agent.pfx");
        using var certificate = CreateClientCertificate();
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pkcs12, "secret"), TestContext.CancellationToken);

        var provider = new FileTunnelCertificateProvider(new TunnelCertificateOptions {
            CertificatePath = path,
            CertificatePassword = "secret",
            RenewalWindowDays = 14
        });

        var lease = await provider.GetCurrentCertificateAsync(TestContext.CancellationToken);

        Assert.IsNotNull(lease);
        using var loaded = lease.Certificate;
        Assert.IsTrue(loaded.HasPrivateKey);
        Assert.IsGreaterThan(DateTimeOffset.UtcNow, lease.ExpiresAt);
        Assert.IsLessThanOrEqualTo(lease.ExpiresAt, lease.RenewAfter);
    }

    [TestMethod]
    public async Task FileTunnelCertificateProvider_ReturnsNullWhenDisabled()
    {
        var provider = new FileTunnelCertificateProvider(new TunnelCertificateOptions {
            Enabled = false,
            CertificatePath = "/tmp/missing-dev-client-cert.pfx"
        });

        var lease = await provider.GetCurrentCertificateAsync(TestContext.CancellationToken);

        Assert.IsNull(lease);
    }

    [TestMethod]
    public async Task TcpTunnel_ForwardsPlaintextBytes()
    {
        var remotePort = GetFreeTcpPort();
        var listenPort = GetFreeTcpPort();
        var remoteListener = new TcpListener(IPAddress.Loopback, remotePort);
        remoteListener.Start();

        await using var tunnel = new TcpTunnel(new TcpTunnelOptions {
            ListenEndpoint = new TunnelEndpoint { Host = "127.0.0.1", Port = listenPort },
            Endpoints = [new TunnelEndpoint { Host = "127.0.0.1", Port = remotePort }],
            UseTls = false
        });
        tunnel.Start();

        var remoteTask = Task.Run(async () => {
            using var remoteClient = await remoteListener.AcceptTcpClientAsync(TestContext.CancellationToken);
            await using var stream = remoteClient.GetStream();
            var buffer = new byte[4];
            await stream.ReadExactlyAsync(buffer, TestContext.CancellationToken);
            await stream.WriteAsync(buffer, TestContext.CancellationToken);
        }, TestContext.CancellationToken);

        using var localClient = new TcpClient();
        await localClient.ConnectAsync(IPAddress.Loopback, listenPort, TestContext.CancellationToken);
        await using var localStream = localClient.GetStream();
        await localStream.WriteAsync("ping"u8.ToArray(), TestContext.CancellationToken);

        var response = new byte[4];
        await localStream.ReadExactlyAsync(response, TestContext.CancellationToken);

        CollectionAssert.AreEqual("ping"u8.ToArray(), response);
        await remoteTask;
        remoteListener.Stop();
    }

    [TestMethod]
    public async Task TcpTunnelOptions_ReturnsClientCertificateCollectionFromProvider()
    {
        using var certificate = CreateClientCertificate();
        var provider = new StaticTunnelCertificateProvider(certificate);
        var options = new TcpTunnelOptions {
            CertificateProvider = provider,
            Endpoints = [new TunnelEndpoint { Host = "ingest.example.com", Port = 443 }]
        };

        var certificates = await options.GetClientCertificatesAsync(TestContext.CancellationToken);

        Assert.IsNotNull(certificates);
        Assert.HasCount(1, certificates);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static X509Certificate2 CreateClientCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=agent-01", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
        [
            new Oid("1.3.6.1.5.5.7.3.2")
        ], critical: false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90));
        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);
    }

    private sealed class StaticTunnelCertificateProvider(X509Certificate2 certificate) : ITunnelCertificateProvider
    {
        public ValueTask<TunnelCertificateLease?> GetCurrentCertificateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TunnelCertificateLease?>(new TunnelCertificateLease(
                certificate,
                new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
                DateTimeOffset.UtcNow.AddDays(30)));
    }

    public TestContext TestContext { get; set; }
}