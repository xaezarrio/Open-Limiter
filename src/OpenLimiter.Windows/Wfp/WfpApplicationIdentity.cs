using System.Runtime.InteropServices;

namespace OpenLimiter.Windows.Wfp;

public static class WfpApplicationIdentity
{
    private const int MaximumHashedBytes = 4096;
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    public static bool TryGetHash(string executablePath, out ulong hash)
    {
        hash = 0;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            return false;
        }

        nint applicationIdPointer = nint.Zero;
        try
        {
            var result = NativeMethods.FwpmGetAppIdFromFileName0(executablePath, out applicationIdPointer);
            if (result != 0 || applicationIdPointer == nint.Zero)
            {
                return false;
            }

            var blob = Marshal.PtrToStructure<FwpByteBlob>(applicationIdPointer);
            if (blob.Data == nint.Zero)
            {
                return false;
            }

            var bytesToHash = (int)Math.Min(blob.Size, MaximumHashedBytes);
            var bytes = new byte[bytesToHash];
            Marshal.Copy(blob.Data, bytes, 0, bytes.Length);
            hash = HashBytes(bytes, blob.Size);
            return true;
        }
        finally
        {
            if (applicationIdPointer != nint.Zero)
            {
                NativeMethods.FwpmFreeMemory0(ref applicationIdPointer);
            }
        }
    }

    internal static ulong HashBytes(ReadOnlySpan<byte> bytes, uint originalLength)
    {
        var hash = FnvOffsetBasis;
        foreach (var value in bytes[..Math.Min(bytes.Length, MaximumHashedBytes)])
        {
            hash ^= value;
            hash = unchecked(hash * FnvPrime);
        }

        hash ^= originalLength;
        return unchecked(hash * FnvPrime);
    }
}
