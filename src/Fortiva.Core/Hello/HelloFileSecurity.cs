using System.Security.AccessControl;
using System.Security.Principal;

namespace Fortiva.Core.Hello;

public static class HelloFileSecurity
{
    public static void WriteRestrictedFile(string path, ReadOnlySpan<byte> content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllBytes(temp, content.ToArray());
        ApplyCurrentUserOnlyAcl(temp);
        File.Move(temp, path, overwrite: true);
        ApplyCurrentUserOnlyAcl(path);
    }

    public static void ApplyCurrentUserOnlyAcl(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
            return;

        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null)
                return;

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch
        {
            /* best effort — DPAPI still protects content */
        }
    }

    public static void SecureDelete(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var length = new FileInfo(path).Length;
            if (length > 0 && length <= int.MaxValue)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                stream.SetLength(length);
                stream.Write(new byte[length], 0, (int)length);
                stream.Flush(true);
            }
        }
        catch
        {
            /* continue to delete */
        }

        try { File.Delete(path); }
        catch { /* best effort */ }
    }
}
