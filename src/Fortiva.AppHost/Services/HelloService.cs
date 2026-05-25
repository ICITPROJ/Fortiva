using System.Runtime.InteropServices;
using Fortiva.Core.Hello;
using Windows.Foundation;
using Windows.Security.Credentials.UI;
using WinRT;

namespace Fortiva.AppHost.Services;

/// <summary>
/// Windows Hello unlock (face, fingerprint, or PIN) via UserConsentVerifier.
/// Unpackaged WinUI apps must use RequestVerificationForWindowAsync so Windows
/// offers the full Hello sign-in stack, not PIN-only fallback.
/// </summary>
public static class HelloService
{
    private static readonly Guid InteropGuid = new("9710727D-8E8D-4FBE-9002-3CB2AA5E9C7B");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RequestVerificationForWindowNative(
        IntPtr thisPtr,
        IntPtr appWindow,
        IntPtr messageHString,
        ref Guid riid,
        out IntPtr asyncOperation);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static extern void WindowsDeleteString(IntPtr hString);

    public static async Task<bool> IsAvailableAsync()
    {
        var availability = await GetAvailabilityAsync();
        return availability == UserConsentVerifierAvailability.Available;
    }

    public static async Task<HelloVerificationResult> VerifyAsync(string message)
    {
        try
        {
            var availability = await GetAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
                return new HelloVerificationResult(false, DescribeAvailability(availability));

            var result = await RequestVerificationAsync(message);
            if (result == UserConsentVerificationResult.Verified)
                HelloVerificationGate.MarkVerified();
            return MapResult(result);
        }
        catch (Exception ex)
        {
            App.LogException("HelloService.VerifyAsync", ex);
            return new HelloVerificationResult(false, UserFacingError(ex));
        }
    }

    private static async Task<UserConsentVerifierAvailability> GetAvailabilityAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync();
        }
        catch
        {
            return UserConsentVerifierAvailability.DeviceNotPresent;
        }
    }

    private static async Task<UserConsentVerificationResult> RequestVerificationAsync(string message)
    {
        var hwnd = App.MainWindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                return await RequestVerificationForWindowAsync(hwnd, message);
            }
            catch (Exception ex) when (ex is InvalidCastException or COMException)
            {
                App.LogException("HelloService.RequestVerificationForWindow", ex);
            }
        }

        return await UserConsentVerifier.RequestVerificationAsync(message);
    }

    private static async Task<UserConsentVerificationResult> RequestVerificationForWindowAsync(
        IntPtr hwnd, string message)
    {
        var factory = ActivationFactory.Get("Windows.Security.Credentials.UI.UserConsentVerifier");
        Marshal.ThrowExceptionForHR(factory.TryAs(InteropGuid, out var interopPtr));
        try
        {
            var iid = typeof(IAsyncOperation<UserConsentVerificationResult>).GUID;
            var hr = InvokeRequestVerificationForWindowAsync(interopPtr, hwnd, message, ref iid, out var opPtr);
            Marshal.ThrowExceptionForHR(hr);
            if (opPtr == IntPtr.Zero)
                throw new COMException("Windows Hello did not return a verification operation.");

            var operation = MarshalInterface<IAsyncOperation<UserConsentVerificationResult>>.FromAbi(opPtr);
            return await operation;
        }
        finally
        {
            Marshal.Release(interopPtr);
        }
    }

    private static int InvokeRequestVerificationForWindowAsync(
        IntPtr interopPtr,
        IntPtr hwnd,
        string message,
        ref Guid riid,
        out IntPtr asyncOperation)
    {
        var vtable = Marshal.ReadIntPtr(interopPtr);
        var methodPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var method = Marshal.GetDelegateForFunctionPointer<RequestVerificationForWindowNative>(methodPtr);

        Marshal.ThrowExceptionForHR(WindowsCreateString(message, message.Length, out var messageHString));
        try
        {
            return method(interopPtr, hwnd, messageHString, ref riid, out asyncOperation);
        }
        finally
        {
            WindowsDeleteString(messageHString);
        }
    }

    internal static string DescribeAvailability(UserConsentVerifierAvailability availability) =>
        availability switch
        {
            UserConsentVerifierAvailability.NotConfiguredForUser =>
                "Windows Hello is not set up on this PC. Open Settings → Accounts → Sign-in options and add face recognition, fingerprint, or PIN.",
            UserConsentVerifierAvailability.DeviceNotPresent =>
                "No Windows Hello sign-in method is available. Set up face, fingerprint, or a PIN in Windows Settings.",
            UserConsentVerifierAvailability.DisabledByPolicy =>
                "Windows Hello is disabled by your organization.",
            UserConsentVerifierAvailability.DeviceBusy =>
                "Windows Hello is busy. Wait a moment and try again.",
            _ => "Windows Hello is not available on this device."
        };

    internal static HelloVerificationResult MapResult(UserConsentVerificationResult result) =>
        result switch
        {
            UserConsentVerificationResult.Verified =>
                new HelloVerificationResult(true, null),
            UserConsentVerificationResult.Canceled =>
                new HelloVerificationResult(false, "Verification was cancelled."),
            UserConsentVerificationResult.DeviceNotPresent =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DeviceNotPresent)),
            UserConsentVerificationResult.DisabledByPolicy =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DisabledByPolicy)),
            UserConsentVerificationResult.NotConfiguredForUser =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.NotConfiguredForUser)),
            UserConsentVerificationResult.DeviceBusy =>
                new HelloVerificationResult(false, DescribeAvailability(UserConsentVerifierAvailability.DeviceBusy)),
            UserConsentVerificationResult.RetriesExhausted =>
                new HelloVerificationResult(false, "Too many failed attempts. Try again later or use your master password."),
            _ =>
                new HelloVerificationResult(false, "Windows Hello verification failed. Try again or use your master password.")
        };

    private static string UserFacingError(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message)
            ? "Windows Hello verification failed."
            : ex.Message;
}

public sealed record HelloVerificationResult(bool Verified, string? ErrorMessage);
