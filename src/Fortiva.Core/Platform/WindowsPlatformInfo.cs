using System.Runtime.InteropServices;

namespace Fortiva.Core.Platform;

/// <summary>Windows version info for compatibility gating (not for telemetry).</summary>
public static class WindowsPlatformInfo
{
    /// <summary>Windows 10 2004+ / Windows 11 baseline used by Fortiva installers.</summary>
    public const int MinSupportedBuild = 19041;

    /// <summary>Last Windows build this release train was tested on (updated via release manifest).</summary>
    public const int DefaultMaxBuildTested = 26100;

    public static int CurrentBuild => GetVersion().Build;

    public static Version GetVersion()
    {
        var info = new OsVersionInfo();
        info.Size = Marshal.SizeOf<OsVersionInfo>();
        _ = RtlGetVersion(ref info);
        return new Version(info.MajorVersion, info.MinorVersion, info.BuildNumber);
    }

    public static PlatformCompatibility Check(int minBuild, int maxBuildTested)
    {
        var build = CurrentBuild;
        if (build < minBuild)
            return PlatformCompatibility.Unsupported(
                $"Fortiva requires Windows 10 version 2004 (build {minBuild}) or later. This PC is build {build}.");

        if (build > maxBuildTested)
            return PlatformCompatibility.Untested(
                $"Windows build {build} is newer than Fortiva {maxBuildTested} was tested on. " +
                "Enable automatic updates - a compatible build is usually available without waiting for support.");

        return PlatformCompatibility.Supported();
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct OsVersionInfo
    {
        public int Size;
        public int MajorVersion;
        public int MinorVersion;
        public int BuildNumber;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CsdVersion;
    }
}

public readonly record struct PlatformCompatibility(
    bool IsSupported,
    bool IsUntested,
    string? Message)
{
    public static PlatformCompatibility Supported() => new(true, false, null);
    public static PlatformCompatibility Untested(string message) => new(true, true, message);
    public static PlatformCompatibility Unsupported(string message) => new(false, false, message);
}
