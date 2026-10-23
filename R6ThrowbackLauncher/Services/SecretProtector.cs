using System;
using System.Runtime.InteropServices;
using System.Text;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>Windows DPAPI (current user scope) for the optional saved Steam password.</summary>
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OperationThrowback.SteamPassword");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            byte[] data = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(Transform(data, protect: true));
        }

        public static string? Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return null;
            try
            {
                byte[] data = Convert.FromBase64String(protectedBase64);
                return Encoding.UTF8.GetString(Transform(data, protect: false));
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CryptProtectUiForbidden = 0x1;

        private static byte[] Transform(byte[] input, bool protect)
        {
            var inBlob = ToBlob(input);
            var entropyBlob = ToBlob(Entropy);
            var outBlob = new DataBlob();
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob);
                if (!ok) throw new InvalidOperationException($"DPAPI failed ({Marshal.GetLastWin32Error()})");
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                Marshal.FreeHGlobal(entropyBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static DataBlob ToBlob(byte[] data)
        {
            var ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            return new DataBlob { cbData = data.Length, pbData = ptr };
        }
    }
}
