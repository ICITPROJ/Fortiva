using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fortiva.Core.Licensing;

/*
  Fortiva License Tool — generates and signs enterprise license files.

  Usage:
    generate-key              → print RSA key pair (embed public key in LicenseVerifier)
    sign  <company> <days>    → create signed license.dat for the given company
    verify <license.dat>      → verify a license file against the embedded public key
*/

if (args.Length == 0) { PrintHelp(); return; }

switch (args[0].ToLower())
{
    case "generate-key":  GenerateKey(); break;
    case "sign":          Sign(args);    break;
    case "verify":        Verify(args);  break;
    default:              PrintHelp();   break;
}

static void GenerateKey()
{
    using var rsa = RSA.Create(2048);
    var pub = rsa.ToXmlString(includePrivateParameters: false);
    var priv = rsa.ToXmlString(includePrivateParameters: true);
    Console.WriteLine("=== PUBLIC KEY (embed in LicenseVerifier.EmbeddedPublicKeyXml) ===");
    Console.WriteLine(pub);
    Console.WriteLine();
    Console.WriteLine("=== PRIVATE KEY (keep secret; use with 'sign' command) ===");
    Console.WriteLine(priv);
}

static void Sign(string[] args)
{
    if (args.Length < 4)
    {
        Console.Error.WriteLine("Usage: sign <company-name> <days-valid> <private-key.xml>");
        return;
    }
    var company = args[1];
    var days = int.Parse(args[2]);
    var keyFile = args[3];

    if (!File.Exists(keyFile)) { Console.Error.WriteLine($"Key file not found: {keyFile}"); return; }
    var keyXml = File.ReadAllText(keyFile);

    var doc = new LicenseDocument
    {
        CompanyName = company,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(days),
        Edition = "Enterprise",
        FeatureFlags = ["vault", "policy", "audit", "shared_vaults"],
        MaxSeats = 100
    };

    using var rsa = RSA.Create();
    rsa.FromXmlString(keyXml);

    var payload = LicenseVerifier.CanonicalPayload(doc);
    var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var signed = new SignedLicense { Document = doc, Signature = signature };
    LicenseStore.Save(signed);
    Console.WriteLine($"License signed and written to {LicensePaths.LicenseFilePath}");
    Console.WriteLine($"  Company:  {company}");
    Console.WriteLine($"  Expires:  {doc.ExpiresAt:yyyy-MM-dd}");
    Console.WriteLine($"  Edition:  {doc.Edition}");

    // Also write a portable .lic JSON for distribution
    var portablePath = Path.Combine(Environment.CurrentDirectory, $"fortiva-license-{company.ToLower().Replace(" ", "-")}.json");
    var json = JsonSerializer.Serialize(signed, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(portablePath, json);
    Console.WriteLine($"  Portable: {portablePath}");
}

static void Verify(string[] args)
{
    SignedLicense? license;
    if (args.Length >= 2 && File.Exists(args[1]))
    {
        var json = File.ReadAllText(args[1]);
        license = JsonSerializer.Deserialize<SignedLicense>(json);
    }
    else
    {
        license = LicenseStore.Load();
    }

    if (license is null) { Console.Error.WriteLine("No license found."); return; }

    var valid = LicenseVerifier.Verify(license);
    var expired = license.Document.ExpiresAt <= DateTimeOffset.UtcNow;

    Console.WriteLine($"Company:    {license.Document.CompanyName}");
    Console.WriteLine($"Edition:    {license.Document.Edition}");
    Console.WriteLine($"Expires:    {license.Document.ExpiresAt:yyyy-MM-dd}");
    Console.WriteLine($"Signature:  {(valid ? "VALID" : "INVALID")}");
    Console.WriteLine($"Expired:    {(expired ? "YES" : "no")}");
    Console.WriteLine($"Status:     {(valid && !expired ? "✓ Active" : "✗ Not valid")}");
}

static void PrintHelp()
{
    Console.WriteLine("Fortiva License Tool");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  generate-key                               Generate a new RSA key pair");
    Console.WriteLine("  sign <company> <days> <private-key.xml>    Sign a new license");
    Console.WriteLine("  verify [license.json]                      Verify a license file");
}
