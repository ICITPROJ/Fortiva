using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;

namespace Fortiva.Core.Crypto;

public sealed record Argon2Parameters(
    int MemoryKb,
    int Iterations,
    int Parallelism,
    int SaltSizeBytes = 16,
    int OutputSizeBytes = 32)
{
    public static Argon2Parameters PersonalDefault => new(65536, 3, 4);
    public static Argon2Parameters EnterpriseMinimum => new(131072, 4, 4);
    public static Argon2Parameters Paranoia => new(262144, 5, 4);

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(MemoryKb);
        bw.Write(Iterations);
        bw.Write(Parallelism);
        bw.Write(SaltSizeBytes);
        bw.Write(OutputSizeBytes);
        return ms.ToArray();
    }

    public static Argon2Parameters FromBytes(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms);
        return Validate(new(
            br.ReadInt32(),
            br.ReadInt32(),
            br.ReadInt32(),
            br.ReadInt32(),
            br.ReadInt32()));
    }

    public const int MaxMemoryKb = 524_288; // 512 MB
    public const int MaxIterations = 32;
    public const int MaxParallelism = 16;
    public const int MinMemoryKb = 1024;
    public const int MinIterations = 1;
    public const int MinParallelism = 1;

    public static Argon2Parameters Validate(Argon2Parameters parameters)
    {
        if (parameters.MemoryKb is < MinMemoryKb or > MaxMemoryKb)
            throw new InvalidDataException("Vault KDF memory parameter is out of allowed range.");
        if (parameters.Iterations is < MinIterations or > MaxIterations)
            throw new InvalidDataException("Vault KDF iteration parameter is out of allowed range.");
        if (parameters.Parallelism is < MinParallelism or > MaxParallelism)
            throw new InvalidDataException("Vault KDF parallelism parameter is out of allowed range.");
        if (parameters.SaltSizeBytes is < 8 or > 64)
            throw new InvalidDataException("Vault salt size is out of allowed range.");
        if (parameters.OutputSizeBytes is < 16 or > 64)
            throw new InvalidDataException("Vault KDF output size is out of allowed range.");
        return parameters;
    }
}

public static class Argon2Kdf
{
    public static (byte[] masterKey, byte[] salt) DeriveMasterKey(string password, Argon2Parameters parameters, byte[]? existingSalt = null)
    {
        var salt = existingSalt ?? new byte[parameters.SaltSizeBytes];
        if (existingSalt is null)
            RandomNumberGenerator.Fill(salt);

        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            Password = passwordBytes,
            Salt = salt,
            Threads = parameters.Parallelism,
            MemoryCost = parameters.MemoryKb,
            TimeCost = parameters.Iterations,
            Lanes = parameters.Parallelism,
            HashLength = parameters.OutputSizeBytes
        };

        try
        {
            using var argon2 = new Argon2(config);
            using var secureHash = argon2.Hash();
            var hash = secureHash.Buffer.ToArray();
            return (hash, salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
