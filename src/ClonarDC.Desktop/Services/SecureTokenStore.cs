using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ClonarDC.Services;

public sealed class SecureTokenStore
{
    private readonly string _path;
    private readonly string _description;

    public SecureTokenStore(string fileName = "token.dat", string description = "Clonar DC")
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "token.dat";

        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClonarDC",
            safeFileName);
        _description = description;
    }

    public void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Clear();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var input = Encoding.UTF8.GetBytes(token);
        try
        {
            var protectedBytes = Protect(input, _description);
            File.WriteAllBytes(_path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public string? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var clear = Unprotect(protectedBytes);
            try { return Encoding.UTF8.GetString(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch { return null; }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private static byte[] Protect(byte[] data, string description) => Crypt(data, protect: true, description);
    private static byte[] Unprotect(byte[] data) => Crypt(data, protect: false, null);

    private static byte[] Crypt(byte[] data, bool protect, string? description)
    {
        var inBlob = new DATA_BLOB();
        var outBlob = new DATA_BLOB();
        try
        {
            inBlob.cbData = data.Length;
            inBlob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            bool ok = protect
                ? CryptProtectData(ref inBlob, description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}