using System.Text.Json;
using Fortiva.Core.Policy;

namespace Fortiva.Core.Admin;

public enum SharedVaultRole
{
    User,
    Admin
}

public sealed class SharedVaultDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public Dictionary<string, SharedVaultRole> MemberRoles { get; set; } = new();
}

public sealed class SharedVaultConfiguration
{
    public List<SharedVaultDefinition> Vaults { get; set; } = [];
}

public static class SharedVaultStore
{
    public static string ConfigPath =>
        Path.Combine(Environment.ExpandEnvironmentVariables(@"%PROGRAMDATA%\Fortiva"), "shared-vaults.json");

    public static SharedVaultConfiguration Load()
    {
        if (!File.Exists(ConfigPath)) return new SharedVaultConfiguration();
        var protectedBytes = File.ReadAllBytes(ConfigPath);
        var json = System.Security.Cryptography.ProtectedData.Unprotect(
            protectedBytes,
            "Fortiva.SharedVaults.v1"u8.ToArray(),
            System.Security.Cryptography.DataProtectionScope.LocalMachine);
        return JsonSerializer.Deserialize<SharedVaultConfiguration>(json) ?? new();
    }

    public static void Save(SharedVaultConfiguration config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(config);
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            json,
            "Fortiva.SharedVaults.v1"u8.ToArray(),
            System.Security.Cryptography.DataProtectionScope.LocalMachine);
        File.WriteAllBytes(ConfigPath, protectedBytes);
    }
}

public static class PolicyValidator
{
    public static IReadOnlyList<string> Validate(FortivaPolicy policy)
    {
        var errors = new List<string>();
        if (policy.MinArgon2MemoryKb < 65536)
            errors.Add("Minimum Argon2 memory must be at least 65536 KB.");
        if (policy.MinArgon2Iterations < 1)
            errors.Add("Minimum Argon2 iterations must be at least 1.");
        if (policy.MaxAutoLockSeconds < 30)
            errors.Add("Maximum auto-lock must be at least 30 seconds.");
        if (policy.ClipboardClearSeconds < 5 && !policy.ClipboardDisabled)
            errors.Add("Clipboard clear timeout must be at least 5 seconds when clipboard is enabled.");
        return errors;
    }
}
