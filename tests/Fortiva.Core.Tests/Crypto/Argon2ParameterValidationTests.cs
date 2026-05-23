using Fortiva.Core.Crypto;

namespace Fortiva.Core.Tests.Crypto;

public class Argon2ParameterValidationTests
{
    [Fact]
    public void Validate_RejectsExcessiveMemory()
    {
        var huge = new Argon2Parameters(Argon2Parameters.MaxMemoryKb + 1, 3, 4);
        Assert.Throws<InvalidDataException>(() => Argon2Parameters.Validate(huge));
    }

    [Fact]
    public void Validate_AcceptsPersonalDefault()
    {
        var ok = Argon2Parameters.Validate(Argon2Parameters.PersonalDefault);
        Assert.Equal(Argon2Parameters.PersonalDefault.MemoryKb, ok.MemoryKb);
    }
}
