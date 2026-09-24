using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: MakeDevCert <subject: CN=...> <output.pfx> [password]");
    return 1;
}

var subject = args[0];
var outPfx = args[1];
var password = args.Length > 2 ? args[2] : null;

using var rsa = RSA.Create(2048);
var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
    new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true)); // code signing
req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

File.WriteAllBytes(outPfx, cert.Export(X509ContentType.Pfx, password));
Console.WriteLine($"wrote {outPfx}");
Console.WriteLine($"thumbprint={cert.Thumbprint}");

try
{
    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadWrite);
    store.Add(cert);
    store.Close();
    Console.WriteLine("installed into CurrentUser\\My");
}
catch (Exception ex)
{
    Console.WriteLine($"store install skipped: {ex.Message}");
}

return 0;