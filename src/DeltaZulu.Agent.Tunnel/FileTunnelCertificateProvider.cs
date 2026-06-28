using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Agent.Tunnel;

/// <summary>
/// Loads an agent mTLS certificate from local files while preserving the lifecycle abstraction for future enrollment services.
/// </summary>
public sealed class FileTunnelCertificateProvider : ITunnelCertificateProvider
{
    private readonly TunnelCertificateOptions _options;
    private readonly TimeProvider _timeProvider;

    public FileTunnelCertificateProvider(TunnelCertificateOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<TunnelCertificateLease?> GetCurrentCertificateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.CertificatePath))
        {
            return ValueTask.FromResult<TunnelCertificateLease?>(null);
        }

        if (!File.Exists(_options.CertificatePath))
        {
            throw new FileNotFoundException("The tunnel client certificate file was not found.", _options.CertificatePath);
        }

        var certificate = LoadCertificate(_options);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidDataException($"Tunnel client certificate '{_options.CertificatePath}' must include a private key.");
        }

        var now = _timeProvider.GetUtcNow();
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        var expiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        var renewAfter = expiresAt - TimeSpan.FromDays(Math.Max(0, _options.RenewalWindowDays));
        if (renewAfter < notBefore)
        {
            renewAfter = notBefore;
        }

        if (now < notBefore || now >= expiresAt)
        {
            certificate.Dispose();
            throw new InvalidDataException($"Tunnel client certificate '{_options.CertificatePath}' is not valid at {now:O}.");
        }

        return ValueTask.FromResult<TunnelCertificateLease?>(new TunnelCertificateLease(certificate, notBefore, expiresAt, renewAfter));
    }

    private static X509Certificate2 LoadCertificate(TunnelCertificateOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            if (!File.Exists(options.PrivateKeyPath))
            {
                throw new FileNotFoundException("The tunnel client private key file was not found.", options.PrivateKeyPath);
            }

            return X509Certificate2.CreateFromPemFile(options.CertificatePath!, options.PrivateKeyPath);
        }

        var extension = Path.GetExtension(options.CertificatePath);
        if (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, options.CertificatePassword);
        }

        return X509CertificateLoader.LoadCertificateFromFile(options.CertificatePath!);
    }
}
