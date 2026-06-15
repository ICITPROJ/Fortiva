using Fortiva.Core.Hello;

namespace Fortiva.AppHost.Services;

/// <summary>Snapshot of hello.keyprotect / hello.binding so a failed TPM upgrade can be rolled back.</summary>
internal sealed class HelloBindingBackup
{
    public required byte[] KeyProtect { get; init; }
    public byte[]? Binding { get; init; }

    public static HelloBindingBackup? TryCapture(string dataDirectory)
    {
        var keyProtectPath = Path.Combine(dataDirectory, "hello.keyprotect");
        if (!File.Exists(keyProtectPath))
            return null;

        var bindingPath = Path.Combine(dataDirectory, "hello.binding");
        return new HelloBindingBackup
        {
            KeyProtect = File.ReadAllBytes(keyProtectPath),
            Binding = File.Exists(bindingPath) ? File.ReadAllBytes(bindingPath) : null
        };
    }

    public void Restore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        HelloFileSecurity.WriteRestrictedFile(
            Path.Combine(dataDirectory, "hello.keyprotect"),
            KeyProtect);
        if (Binding is not null)
        {
            HelloFileSecurity.WriteRestrictedFile(
                Path.Combine(dataDirectory, "hello.binding"),
                Binding);
        }
    }
}
