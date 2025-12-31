using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SysmonConfigPusher.Service;

/// <summary>
/// Helper class for certificate management.
/// </summary>
public static class CertificateHelper
{
    /// <summary>
    /// Ensures a self-signed certificate exists for HTTPS.
    /// Creates one if it doesn't exist in the LocalMachine store.
    /// </summary>
    public static void EnsureCertificateExists()
    {
        const string certSubject = "CN=SysmonConfigPusher";

        Console.WriteLine("Checking for HTTPS certificate...");

        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);

            // Check if certificate already exists and is valid
            var existingCerts = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, certSubject, false);
            if (existingCerts.Count > 0)
            {
                var cert = existingCerts[0];
                if (cert.NotAfter > DateTime.Now.AddDays(30))
                {
                    Console.WriteLine($"Found existing certificate: {cert.Thumbprint} (expires {cert.NotAfter:yyyy-MM-dd})");
                    return;
                }
                Console.WriteLine($"Removing expired certificate: {cert.Thumbprint}");
                store.Remove(cert);
            }

            Console.WriteLine("Creating new self-signed certificate...");

            // Create new self-signed certificate
            var hostName = Environment.MachineName;
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                certSubject,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Add extensions
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                    critical: false));

            // Add Subject Alternative Names
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(hostName);
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddDnsName(hostName.ToLowerInvariant());
            request.CertificateExtensions.Add(sanBuilder.Build());

            // Create the certificate (valid for 2 years)
            var certificate = request.CreateSelfSigned(
                DateTimeOffset.Now.AddDays(-1),
                DateTimeOffset.Now.AddYears(2));

            // Export and re-import with private key exportable for Kestrel
            var exportedCert = certificate.Export(X509ContentType.Pfx, "");
            var persistableCert = new X509Certificate2(
                exportedCert,
                "",
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

            store.Add(persistableCert);
            Console.WriteLine($"Certificate created: {persistableCert.Thumbprint}");

            store.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to create certificate: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.WriteLine();
            Console.WriteLine("Please create the certificate manually by running this PowerShell command as Administrator:");
            Console.WriteLine();
            Console.WriteLine(@"New-SelfSignedCertificate -Subject ""CN=SysmonConfigPusher"" -DnsName $env:COMPUTERNAME, ""localhost"" -CertStoreLocation ""Cert:\LocalMachine\My"" -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(2) -FriendlyName ""SysmonConfigPusher HTTPS Certificate""");
            Console.WriteLine();
        }
    }
}
