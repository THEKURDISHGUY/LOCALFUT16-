#nullable disable
#pragma warning disable
using System.Text;
namespace LocalFut15;

static class KeyDump
{
    public static void Run(string exe, string[] words)
    {
        if (!File.Exists(exe)) { Console.WriteLine("exe not found: " + exe); return; }
        if (words.Length == 0) words = new[] { "draft", "season", "tournament", "manager" };
        var data = File.ReadAllBytes(exe);
        var strs = new List<(long off, string s)>();
        int i = 0;
        while (i < data.Length)
        {
            int st = i;
            while (i < data.Length && data[i] >= 0x20 && data[i] < 0x7F) i++;
            if (i - st >= 3 && i < data.Length && data[i] == 0) strs.Add((st, Encoding.ASCII.GetString(data, st, i - st)));
            i++;
        }
        Console.WriteLine($"{strs.Count} strings in {Path.GetFileName(exe)}");
        var sb = new StringBuilder();
        const int Win = 40;
        foreach (var w in words)
        {
            var hits = new List<int>();
            for (int k = 0; k < strs.Count; k++)
                if (strs[k].s.Contains(w, StringComparison.OrdinalIgnoreCase)) hits.Add(k);
            sb.AppendLine($"################ '{w}': {hits.Count} hit(s)");
            int lastEnd = -1;
            foreach (var h in hits)
            {
                int a = Math.Max(Math.Max(0, h - Win), lastEnd + 1), b = Math.Min(strs.Count - 1, h + Win);
                if (a > b) continue;
                sb.AppendLine($"---- @0x{strs[h].off:X}");
                for (int k = a; k <= b; k++) sb.AppendLine((k == h ? ">> " : "   ") + strs[k].s);
                lastEnd = b;
            }
        }
        File.WriteAllText("keys.txt", sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("Wrote keys.txt (" + sb.Length / 1024 + " KB). Send me that file.");
    }
}
