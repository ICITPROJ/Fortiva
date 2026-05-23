using Fortiva.Core.Crypto;

namespace Fortiva.Core.Tests.Crypto;

public class Argon2Tests
{
    [Theory]
    [InlineData("password", "different from empty")]
    [InlineData("correct horse battery staple", "a passphrase")]
    [InlineData("!@#$%^&*()Unicode🔑Test", "unicode chars")]
    public void DeriveMasterKey_DeterministicWithSameSalt(string password, string _)
    {
        var (key1, salt) = Argon2Kdf.DeriveMasterKey(password, Argon2Parameters.PersonalDefault);
        var (key2, _) = Argon2Kdf.DeriveMasterKey(password, Argon2Parameters.PersonalDefault, salt);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveMasterKey_DifferentSalts_ProduceDifferentKeys()
    {
        var (k1, s1) = Argon2Kdf.DeriveMasterKey("same-password", Argon2Parameters.PersonalDefault);
        var (k2, s2) = Argon2Kdf.DeriveMasterKey("same-password", Argon2Parameters.PersonalDefault);
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void Paranoia_ParametersStrongerThanDefault()
    {
        var p = Argon2Parameters.Paranoia;
        var d = Argon2Parameters.PersonalDefault;
        Assert.True(p.MemoryKb > d.MemoryKb);
        Assert.True(p.Iterations >= d.Iterations);
    }

    [Fact]
    public void PersonalDefault_MeetsMinimumStrength()
    {
        var p = Argon2Parameters.PersonalDefault;
        Assert.True(p.MemoryKb >= 65536, "Must use at least 64 MB");
        Assert.True(p.Iterations >= 3, "At least 3 iterations required");
        Assert.True(p.Parallelism >= 1, "At least 1 thread required");
        Assert.Equal(32, p.OutputSizeBytes);
    }

    [Fact]
    public void ParameterSerialization_RoundTrips()
    {
        var original = new Argon2Parameters(131072, 5, 4);
        var bytes = original.ToBytes();
        var restored = Argon2Parameters.FromBytes(bytes);
        Assert.Equal(original.MemoryKb, restored.MemoryKb);
        Assert.Equal(original.Iterations, restored.Iterations);
        Assert.Equal(original.Parallelism, restored.Parallelism);
    }
}
