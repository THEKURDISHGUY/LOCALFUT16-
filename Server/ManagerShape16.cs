#nullable disable
#pragma warning disable
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static void ApplyManagerShape(JsonObject o)
    {
        if (o == null) return;
        try
        {
            var f = FindFile("managers_shape.txt");
            if (!File.Exists(f)) return;
            foreach (var raw in File.ReadAllLines(f))
            {
                var line = raw.Trim(); if (line.Length == 0) continue;
                if (line.StartsWith("quality=")) { o["quality"] = line.Substring(8).Trim(); continue; }
                if (line.StartsWith("rare=") && long.TryParse(line.Substring(5).Trim(), out var rr)) { o["rareflag"] = rr; continue; }
                if (long.TryParse(line, out var n)) o["cardsubtypeid"] = n;
            }
        }
        catch { }
    }
    static void ApplyManagerShapeAll()
    {
        try
        {
            int n = 0;
            if (Club?["items"] is JsonArray a)
                foreach (var x in a) if (x is JsonObject o && (o["itemType"]?.ToString() ?? "") == "staff") { ApplyManagerShape(o); n++; }
            if (n > 0) { Save(); Console.WriteLine($"[MANAGER-SHAPE] applied to {n} manager card(s) (managers_shape.txt)"); }
        }
        catch (Exception ex) { Console.WriteLine("[MANAGER-SHAPE] failed: " + ex.Message); }
    }
}
