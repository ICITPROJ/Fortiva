using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Fortiva.Core.Crypto;

/// <summary>
/// Wipes sensitive buffers using CryptographicOperations and native SecureZeroMemory where available.
/// </summary>
public static class SecureMemory
{
    [DllImport("kernel32.dll", EntryPoint = "RtlSecureZeroMemory")]
    private static extern void SecureZeroMemory(IntPtr ptr, UIntPtr cnt);

    public static void Zero(byte[] buffer)
    {
        if (buffer is null || buffer.Length == 0) return;
        CryptographicOperations.ZeroMemory(buffer);
    }

    public static void ZeroNative(IntPtr ptr, int length)
    {
        if (ptr == IntPtr.Zero || length <= 0) return;
        SecureZeroMemory(ptr, (UIntPtr)(uint)length);
    }

    public static SecureBuffer Rent(int length) => new(length);

    public sealed class SecureBuffer : IDisposable
    {
        private byte[]? _buffer;

        public SecureBuffer(int length)
        {
            _buffer = GC.AllocateArray<byte>(length, pinned: true);
        }

        public Span<byte> Span => _buffer ?? Span<byte>.Empty;

        public void Dispose()
        {
            if (_buffer is not null)
            {
                Zero(_buffer);
                _buffer = null;
            }
        }
    }
}
