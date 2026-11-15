using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

string outDir = Path.Combine(Directory.GetCurrentDirectory(), "certs");
Directory.CreateDirectory(outDir);
const string pw = "kurdish";

var caName = new X500DistinguishedName("CN=GOS 2015 Certificate Authority, E=GOSDirtysockSupport@ea.com, OU=Global Online Studio, O=\"Electronic Arts, Inc.\", L=Redwood City, S=California, C=US");
var caKey = RSA.Create(2048);
var caReq = new CertificateRequest(caName, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
var ca = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(10));

var leafKey = RSA.Create(2048);
var leafName = new X500DistinguishedName("CN=utas.external.s2.fut.ea.com, OU=Global Online Studio, O=\"Electronic Arts, Inc.\", S=California, C=US");
var req = new CertificateRequest(leafName, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
var san = new SubjectAlternativeNameBuilder();
foreach (var h in new[] { "winter15.gosredirector.ea.com", "gosredirector.ea.com", "spring18.gosredirector.ea.com",
    "utas.external.s2.fut.ea.com", "utas.external.s3.fut.ea.com", "utas.external.fut.ea.com",
    "utas.s2.fut.ea.com", "utas.s3.fut.ea.com", "utas.fut.ea.com", "fut.ea.com",
    "accounts.ea.com", "gateway.ea.com" }) san.AddDnsName(h);
san.AddIpAddress(System.Net.IPAddress.Parse("127.0.0.1"));
req.CertificateExtensions.Add(san.Build());
var serial = new byte[16]; RandomNumberGenerator.Fill(serial); serial[0] &= 0x7F;
var leaf = req.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(9), serial);

File.WriteAllBytes(Path.Combine(outDir, "leaf.pfx"), leaf.CopyWithPrivateKey(leafKey).Export(X509ContentType.Pfx, pw));
File.WriteAllBytes(Path.Combine(outDir, "ca.pfx"), ca.Export(X509ContentType.Pfx, pw));
File.WriteAllBytes(Path.Combine(outDir, "ca.cer"), ca.Export(X509ContentType.Cert));
var mod = caKey.ExportParameters(false).Modulus!;
File.WriteAllBytes(Path.Combine(outDir, "ca-modulus.bin"), mod);
Console.WriteLine($"CA modulus: {mod.Length} bytes, first 8: {BitConverter.ToString(mod, 0, 8)}, last byte odd: {(mod[^1] & 1) == 1}");
Console.WriteLine("Leaf issuer: " + leaf.Issuer);
Console.WriteLine("Files written to " + outDir);
