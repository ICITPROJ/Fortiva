using System.Text;

namespace Fortiva.Core.Crypto;

/// <summary>
/// Minimizes UTF-8 master-password lifetime: encode once, zero buffer after use.
/// CLR string interning cannot be eliminated; this limits duplicate byte buffers.
/// </summary>
public static class SensitivePassword
{
    public static T UseUtf8<T>(string password, Func<byte[], T> action)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return action(bytes);
        }
        finally
        {
            SecureMemory.Zero(bytes);
        }
    }

    public static void UseUtf8(string password, Action<byte[]> action)
        => UseUtf8(password, bytes =>
        {
            action(bytes);
            return 0;
        });
}
