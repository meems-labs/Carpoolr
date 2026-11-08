using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Carpoolr.Api.Infrastructure;

/// <summary>Trusts the public CA bundle mounted by Kubernetes without requiring any private key.</summary>
public sealed class KubernetesCertificateTrust : IDisposable
{
    private readonly X509Certificate2Collection authorities = [];

    public KubernetesCertificateTrust(string certificateAuthorityPem)
    {
        authorities.ImportFromPem(certificateAuthorityPem);
        if (authorities.Count == 0)
        {
            throw new CryptographicException("The Kubernetes CA bundle contains no certificates.");
        }
    }

    public bool IsValidServerCertificate(X509Certificate2? certificate, SslPolicyErrors errors)
    {
        if (certificate is null ||
            (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.ExtraStore.AddRange(authorities);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        // Cluster CA certificates do not publish an internet CRL/OCSP service.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    public void Dispose()
    {
        foreach (var certificate in authorities)
        {
            certificate.Dispose();
        }
    }
}
