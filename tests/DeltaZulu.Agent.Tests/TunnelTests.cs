using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DeltaZulu.Agent.Tunnel;

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

        var provider = new FileTunnelCertificateProvider(new TunnelCertificateOptions
        {
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
        var provider = new FileTunnelCertificateProvider(new TunnelCertificateOptions
        {
            Enabled = false,
            CertificatePath = "/tmp/missing-dev-client-cert.pfx"
        });

        var lease = await provider.GetCurrentCertificateAsync(TestContext.CancellationToken);

        Assert.IsNull(lease);
    }

    [TestMethod]
    public async Task RelpMtlsTunnelOptions_ReturnsClientCertificateCollectionFromProvider()
    {
        using var certificate = CreateClientCertificate();
        var provider = new StaticTunnelCertificateProvider(certificate);
        var options = new RelpMtlsTunnelOptions
        {
            CertificateProvider = provider,
            Endpoints = [new TunnelEndpoint { Host = "ingest.example.com", Port = 443 }]
        };

        var certificates = await options.GetClientCertificatesAsync(TestContext.CancellationToken);

        Assert.IsNotNull(certificates);
        Assert.HasCount(1, certificates);
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
        return new X509Certificate2(certificate.Export(X509ContentType.Pkcs12));
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
