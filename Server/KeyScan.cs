#nullable disable
#pragma warning disable
using System.Text;
namespace LocalFut15;

static class KeyScan
{
    public static void Run(string dir, string[] words)
    {
        if (!Directory.Exists(dir)) { Console.WriteLine("folder not found: " + dir); return; }
        if (words.Length == 0) words = new[] { "squad/mode", "draft", "tournament", "playerheads", "fut2dheads", "discard" };
        var needles = words.Select(w => Encoding.ASCII.GetBytes(w.ToLowerInvariant())).ToArray();
        var sb = new StringBuilder();
        var seen = new HashSet<string>();
        int files = 0, hits = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            files++;
            foreach (var w in words)
                if (Path.GetFileName(f).Contains(w, StringComparison.OrdinalIgnoreCase)) sb.AppendLine($"[FILENAME] {f}  (matches '{w}')");
            try { hits += ScanFile(f, words, needles, sb, seen); }
            catch (Exception e) { sb.AppendLine($"[skip] {f}: {e.Message}"); }
            if (files % 200 == 0) Console.WriteLine($"{files} files scanned, {hits} hits...");
        }
        File.WriteAllText("scan.txt", sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{files} files, {hits} hits. Wrote scan.txt ({sb.Length / 1024} KB). Send me that file.");
    }

    static int ScanFile(string path, string[] words, byte[][] needles, StringBuilder sb, HashSet<string> seen)
    {
        const int Chunk = 32 * 1024 * 1024, Overlap = 512;
        int hits = 0;
        var perWord = new int[words.Length];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        var buf = new byte[Chunk + Overlap];
        long basePos = 0; int keep = 0;
        while (true)
        {
            int n = fs.Read(buf, keep, Chunk);
            if (n <= 0) break;
            int len = keep + n;
            var low = new byte[len];
            for (int i = 0; i < len; i++) { byte b = buf[i]; low[i] = (b >= 65 && b <= 90) ? (byte)(b + 32) : b; }
            for (int w = 0; w < needles.Length; w++)
            {
                int from = 0;
                while (perWord[w] < 60)
                {
                    int ix = low.AsSpan(from).IndexOf(needles[w]);
                    if (ix < 0) break;
                    ix += from; from = ix + 1;
                    int a = ix, b = ix + needles[w].Length;
                    while (a > 0 && ix - a < 160 && buf[a - 1] >= 0x20 && buf[a - 1] < 0x7F) a--;
                    while (b < len && b - ix < 200 && buf[b] >= 0x20 && buf[b] < 0x7F) b++;
                    string ctx = Encoding.ASCII.GetString(buf, a, b - a);
                    string key = path + "|" + ctx;
                    if (!seen.Add(key)) continue;
                    perWord[w]++; hits++;
                    sb.AppendLine($"{path} @0x{basePos + a:X}  [{words[w]}]  {ctx}");
                }
            }
            if (n < Chunk) break;
            keep = Math.Min(Overlap, len);
            Buffer.BlockCopy(buf, len - keep, buf, 0, keep);
            basePos += len - keep;
        }
        return hits;
    }
}
