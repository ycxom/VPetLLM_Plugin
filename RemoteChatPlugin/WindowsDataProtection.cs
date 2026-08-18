using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RemoteChatPlugin;

internal static class WindowsDataProtection
{
    private const int CryptProtectUiForbidden = 0x1;

    internal static byte[] Protect(byte[] plaintext, byte[] entropy) => Transform(plaintext, entropy, protect: true);
    internal static byte[] Unprotect(byte[] ciphertext, byte[] entropy) => Transform(ciphertext, entropy, protect: false);

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("RemoteChatPlugin requires Windows DPAPI.");
        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        var inputBlob = new DataBlob { Size = input.Length, Data = inputHandle.AddrOfPinnedObject() };
        var entropyBlob = new DataBlob { Size = entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
        DataBlob outputBlob = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, out description, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
            if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (outputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[outputBlob.Size], 0, outputBlob.Data, outputBlob.Size);
                _ = LocalFree(outputBlob.Data);
            }
            if (description != IntPtr.Zero) _ = LocalFree(description);
            entropyHandle.Free();
            inputHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Size;
        internal IntPtr Data;
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string? description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, out IntPtr description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
