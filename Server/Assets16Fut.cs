#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace LocalFut15;

static partial class Fut
{
    static readonly HashSet<string> _bigMiss = new();

    internal static byte[] TryGameBig(string fileName)
    {
        lock (_bigMiss)
        {
            if (_bigMiss.Contains(fileName)) return null;
            var dir = GameDir();
            if (dir == null) return null;
            var needle = Encoding.ASCII.GetBytes(fileName.ToLowerInvariant() + "\0");
            var files = new List<string>();
            try { files.AddRange(Directory.EnumerateFiles(dir, "*.big", SearchOption.AllDirectories)); } catch { }
            foreach (var bf in files.OrderBy(x => Path.GetFileName(x).ToLowerInvariant().StartsWith("cards") ? 0 : 1))
            {
                try
                {
                    long len = new FileInfo(bf).Length;
                    int want = (int)Math.Min(len, 48L * 1024 * 1024);
                    var idx = new byte[want];
                    using (var fs0 = File.OpenRead(bf)) { int got = 0; while (got < want) { int n = fs0.Read(idx, got, want - got); if (n <= 0) break; got += n; } }
                    for (int q = 0; q < idx.Length; q++) if (idx[q] >= 65 && idx[q] <= 90) idx[q] += 32;
                    int at = idx.AsSpan().IndexOf(needle);
                    if (at < 0) continue;
                    int st = at; int back = 0;
                    while (st > 0 && idx[st - 1] >= 0x20 && idx[st - 1] < 0x7f && back < 220) { st--; back++; }
                    if (st < 8) continue;
                    var raw = new byte[Math.Min((int)len, 48 * 1024 * 1024)];
                    using (var fs1 = File.OpenRead(bf)) { int got = 0; while (got < raw.Length) { int n = fs1.Read(raw, got, raw.Length - got); if (n <= 0) break; got += n; } }
                    foreach (bool big in new[] { true, false })
                    {
                        long off = big ? BE(raw, st - 8) : LE(raw, st - 8), size = big ? BE(raw, st - 4) : LE(raw, st - 4);
                        if (size < 16 || size > 16 * 1024 * 1024 || off + size > len) continue;
                        var d = new byte[size];
                        using var fs = File.OpenRead(bf); fs.Position = off;
                        int r = 0; while (r < d.Length) { int n = fs.Read(d, r, d.Length - r); if (n <= 0) break; r += n; }
                        Log.Info($"BIG {fileName} found in {Path.GetFileName(bf)} ({d.Length} bytes, head {BitConverter.ToString(d, 0, Math.Min(8, d.Length))})");
                        return d;
                    }
                }
                catch (Exception e) { Log.Warn("BIG scan " + bf + ": " + e.Message); }
            }
            _bigMiss.Add(fileName);
            Log.Warn("BIG: " + fileName + " not found in any .big of " + dir);
            return null;
        }
    }

    internal static byte[] UnpackChunkZipAny(byte[] d)
    {
        if (d == null || d.Length < 32 || Encoding.ASCII.GetString(d, 0, 8) != "chunkzip") return null;
        for (int start = 8; start < Math.Min(d.Length - 16, 96); start++)
            for (int mode = 0; mode < 2; mode++)
            {
                try
                {
                    using var ms = new MemoryStream(d, start, d.Length - start);
                    using Stream z = mode == 0 ? new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress)
                                               : new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var o = new MemoryStream();
                    try { z.CopyTo(o); } catch { }
                    var r = o.ToArray();
                    if (r.Length > 64) { Log.Info($"chunkzip unpacked ({(mode == 0 ? "zlib" : "deflate")} @{start}) {d.Length} -> {r.Length} bytes, head {BitConverter.ToString(r, 0, 8)}"); return r; }
                }
                catch { }
            }
        Log.Warn("chunkzip could not be unpacked");
        return null;
    }
}
