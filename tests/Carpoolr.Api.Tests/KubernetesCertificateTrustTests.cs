using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Carpoolr.Api.Infrastructure;
using Xunit;

namespace Carpoolr.Api.Tests;

public sealed class KubernetesCertificateTrustTests
{
    [Fact]
    public void PublicOnlyCaBundle_ValidatesItsServerWithoutAClusterPrivateKey()
    {
        using var authority = CreateAuthority();
        using var unrelatedAuthority = CreateAuthority();
        using var server = CreateServer(authority, serverAuthentication: true);
        // Kubernetes supplies public certificates, not the CA's private key.
        using var trust = new KubernetesCertificateTrust(
            unrelatedAuthority.ExportCertificatePem() + "\n" + authority.ExportCertificatePem());

        Assert.True(trust.IsValidServerCertificate(server, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void WrongHostname_IsRejectedEvenWhenTheAuthorityIsTrusted()
    {
        using var authority = CreateAuthority();
        using var server = CreateServer(authority, serverAuthentication: true);
        using var trust = new KubernetesCertificateTrust(authority.ExportCertificatePem());

        Assert.False(trust.IsValidServerCertificate(server, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void UntrustedAuthority_IsRejected()
    {
        using var authority = CreateAuthority();
        using var unrelatedAuthority = CreateAuthority();
        using var server = CreateServer(authority, serverAuthentication: true);
        using var trust = new KubernetesCertificateTrust(unrelatedAuthority.ExportCertificatePem());

        Assert.False(trust.IsValidServerCertificate(server, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void ClientOnlyCertificate_IsRejectedAsAServerCertificate()
    {
        using var authority = CreateAuthority();
        using var clientOnly = CreateServer(authority, serverAuthentication: false);
        using var trust = new KubernetesCertificateTrust(authority.ExportCertificatePem());

        Assert.False(trust.IsValidServerCertificate(clientOnly, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private static X509Certificate2 CreateAuthority()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Test Kubernetes CA {Guid.NewGuid():N}", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
    }

    private static X509Certificate2 CreateServer(X509Certificate2 authority, bool serverAuthentication)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=kubernetes.default.svc", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(serverAuthentication ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        return request.Create(authority, DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
    }
}
