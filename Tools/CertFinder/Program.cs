using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

if (args.Length < 1) { Console.WriteLine("usage: CertFinder <path-to-exe | process-name | pid>"); return; }
string outDir = Path.Combine(Directory.GetCurrentDirectory(), "found_certs");
Directory.CreateDirectory(outDir);
var seen = new HashSet<string>();
int count = 0;

if (File.Exists(args[0]))
{
    Console.WriteLine("Scanning file: " + args[0]);
    Scan(File.ReadAllBytes(args[0]), 0);
}
else
{
    int pid = int.TryParse(args[0], out var pp) ? pp : Process.GetProcessesByName(args[0]).First().Id;
    IntPtr h = OpenProcess(0x0410, false, pid); // QUERY + VM_READ only
    if (h == IntPtr.Zero) { Console.WriteLine("OpenProcess failed (run as Administrator)"); return; }
    Console.WriteLine($"Scanning process {pid}");
    ulong addr = 0;
    while (addr < 0x7FFFFFFFFFFF && VirtualQueryEx(h, (IntPtr)addr, out var m, (UIntPtr)Marshal.SizeOf<MBI>()) != UIntPtr.Zero)
    {
        if (m.State == 0x1000 && m.Protect != 0 && (m.Protect & 0x101) == 0)
        {
            ulong off = 0;
            while (off < m.RegionSize)
            {
                int chunk = (int)Math.Min(m.RegionSize - off, 32UL * 1024 * 1024);
                var buf = new byte[chunk];
                if (ReadProcessMemory(h, (IntPtr)(m.BaseAddress + off), buf, chunk, out int rd) && rd > 0)
                    Scan(buf.AsSpan(0, rd).ToArray(), m.BaseAddress + off);
                if ((ulong)chunk >= m.RegionSize - off) break;
                off += (ulong)chunk - 4096; // overlap so certs on a boundary are not missed
            }
        }
        addr = m.BaseAddress + m.RegionSize;
    }
}
Console.WriteLine($"\nDone. {count} certificate(s) saved to {outDir}");

void Scan(byte[] b, ulong baseAddr)
{
    for (int i = 0; i + 8 < b.Length; i++)
    {
        if (b[i] != 0x30 || b[i + 1] != 0x82 || b[i + 4] != 0x30 || b[i + 5] != 0x82) continue;
        int len = ((b[i + 2] << 8) | b[i + 3]) + 4;
        if (len < 300 || len > 4096 || i + len > b.Length) continue;
        try
        {
            var der = b.AsSpan(i, len).ToArray();
            var cert = new X509Certificate2(der);
            var rsa = cert.GetRSAPublicKey();
            if (rsa == null) continue;
            string key = cert.Thumbprint;
            if (!seen.Add(key)) continue;
            var mod = rsa.ExportParameters(false).Modulus!;
            bool isCa = cert.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority);
            count++;
            File.WriteAllBytes(Path.Combine(outDir, $"cert_{count}.cer"), der);
            File.WriteAllBytes(Path.Combine(outDir, $"cert_{count}_modulus.bin"), mod);
            Console.WriteLine($"\n#{count} @0x{baseAddr + (ulong)i:X}  CA={isCa}  key={mod.Length * 8} bits");
            Console.WriteLine($"  Subject: {cert.Subject}");
            Console.WriteLine($"  Issuer:  {cert.Issuer}");
            Console.WriteLine($"  Expires: {cert.NotAfter:yyyy-MM-dd}");
            Console.WriteLine($"  Modulus starts: {BitConverter.ToString(mod, 0, 8)}");
        }
        catch { }
    }
}

[DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool i, int pid);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out int r);
[DllImport("kernel32.dll")] static extern UIntPtr VirtualQueryEx(IntPtr h, IntPtr a, out MBI m, UIntPtr l);
[StructLayout(LayoutKind.Sequential)]
struct MBI { public ulong BaseAddress, AllocationBase; public uint AllocationProtect, _a1; public ulong RegionSize; public uint State, Protect, Type, _a2; }
