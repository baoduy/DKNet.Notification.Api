using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// A certificate authority made for the test run. <see cref="Trusted" /> signs every mail server the service must
/// reach; the test host adds it to the service's trust through the SMTP trust seam, never through a setting
/// (DRK-2020 §3 "Connection to the SMTP provider"). <see cref="Untrusted" /> signs the one mail server the service
/// must refuse: no host ever trusts it.
/// </summary>
public sealed class TestCertificateAuthority
{
    private static readonly Lazy<TestCertificateAuthority> TrustedInstance =
        new(() => new TestCertificateAuthority("CN=DKNet Notification test authority"));

    private static readonly Lazy<TestCertificateAuthority> UntrustedInstance =
        new(() => new TestCertificateAuthority("CN=DKNet Notification untrusted test authority"));

    private readonly RSA _key;

    private TestCertificateAuthority(string subject)
    {
        _key = RSA.Create(2048);
        var request = new CertificateRequest(subject, _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var now = DateTimeOffset.UtcNow;
        Certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));
    }

    /// <summary>The authority the test host trusts.</summary>
    public static TestCertificateAuthority Trusted => TrustedInstance.Value;

    /// <summary>An authority no host trusts.</summary>
    public static TestCertificateAuthority Untrusted => UntrustedInstance.Value;

    /// <summary>The authority's own certificate.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>A server certificate for <c>localhost</c> and <c>127.0.0.1</c>, signed here, in PEM.</summary>
    public (string CertificatePem, string KeyPem) IssueLocalhostPem()
    {
        using var serverKey = RSA.Create(2048);
        using var server = IssueLocalhost(serverKey);
        return (server.ExportCertificatePem(), serverKey.ExportPkcs8PrivateKeyPem());
    }

    /// <summary>A server certificate for <c>localhost</c> and <c>127.0.0.1</c>, signed here, with its key.</summary>
    public X509Certificate2 IssueLocalhost()
    {
        using var serverKey = RSA.Create(2048);
        using var server = IssueLocalhost(serverKey);
        using var withKey = server.CopyWithPrivateKey(serverKey);
        // Loaded back from PKCS#12: on macOS a server stream cannot use an ephemeral key.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null);
    }

    private X509Certificate2 IssueLocalhost(RSA serverKey)
    {
        var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var now = DateTimeOffset.UtcNow;
        return request.Create(Certificate, now.AddDays(-1), now.AddDays(6), RandomNumberGenerator.GetBytes(16));
    }
}
