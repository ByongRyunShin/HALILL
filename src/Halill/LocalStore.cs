using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Halill;

// Windows DPAPI binds credentials to the current Windows user. Never persist plaintext tokens.
public sealed class LocalStore
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HALILL");
    public LocalStore() => Directory.CreateDirectory(root);
    public T? Read<T>(string name, bool encrypted = false)
    {
        var path = Path.Combine(root, name);
        if (!File.Exists(path)) return default;
        var bytes = File.ReadAllBytes(path);
        if (encrypted) bytes = Protect(bytes, false);
        return JsonSerializer.Deserialize<T>(bytes);
    }
    public void Write<T>(string name, T value, bool encrypted = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (encrypted) bytes = Protect(bytes, true);
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, true);
    }
    public void Delete(string name) => File.Delete(Path.Combine(root, name));

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);

    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
