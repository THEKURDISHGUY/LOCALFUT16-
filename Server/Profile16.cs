#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static int _trustCycle = 0;
    static string ClubNameNow() { var n = Club["clubName"]?.ToString(); return string.IsNullOrWhiteSpace(n) ? Brand : n; }
    static string ClubAbbrNow() { var n = Club["clubAbbr"]?.ToString(); return string.IsNullOrWhiteSpace(n) ? "FRB" : n; }

    static (int, string, byte[], Dictionary<string, string>?)? HandleProfile(string method, string path, string target, byte[] body)
    {
        if (method == "PUT" && path.EndsWith("/user/club"))
        {
            try
            {
                var rq = JsonNode.Parse(Encoding.UTF8.GetString(body));
                var nm = rq?["clubName"]?.ToString(); var ab = rq?["clubAbbr"]?.ToString();
                if (!string.IsNullOrWhiteSpace(nm))
                {
                    Club["clubName"] = nm; if (!string.IsNullOrWhiteSpace(ab)) Club["clubAbbr"] = ab;
                    Save(); Console.WriteLine($"[PROFILE] club name saved: {nm} ({ab})");
                }
            }
            catch { }
            return null;
        }

        if (!path.Contains("fifa16/phishing/")) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n, int code = 200) => (code, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        var q = QueryOf(target);
        string dev = q.TryGetValue("deviceId", out var d) ? d : "";
        if (Club["trustedDevices"] is not JsonArray trusted) { trusted = new JsonArray(); Club["trustedDevices"] = trusted; }
        bool isTrusted = dev.Length > 0 && trusted.Any(x => x?.ToString() == dev);

        if (path.Contains("phishing/validate"))
        {
            if (dev.Length > 0 && !isTrusted) { trusted.Add(dev); Save(); Console.WriteLine($"[TRUST] device {dev.Substring(0, Math.Min(8, dev.Length))}.. is trusted now - the question will not come again"); }
            return null;
        }
        if (!isTrusted) { Console.WriteLine($"[TRUST] new device -> the game shows the security question once ({path.Substring(path.LastIndexOf('/') + 1)})"); return null; }

        bool isDevice = path.Contains("trusteddevice"), isQuestion = path.Contains("phishing/question");
        string tt = "1"; try { var f = FindFile("trust_try.txt"); if (File.Exists(f)) tt = File.ReadAllText(f).Trim(); } catch { }
        if (tt == "auto") tt = (((_trustCycle++) % 6) + 1).ToString();
        Console.WriteLine($"[TRUST] known device -> shape {tt} for {(isDevice ? "trusteddevice" : isQuestion ? "question" : path)}");
        switch (tt)
        {
            case "2": return J(new JsonObject { ["string"] = "Already answered question.", ["trusted"] = true, ["isTrusted"] = true });
            case "3": return isDevice ? J(new JsonObject { ["trusted"] = true, ["isTrusted"] = true, ["trustedDevice"] = true, ["deviceId"] = dev }) : null;
            case "4": return (204, "application/json", new byte[0], null);
            case "5": return J(new JsonObject { ["code"] = 200, ["reason"] = "Already answered question.", ["string"] = "Already answered question." });
            case "6": return null;
            default: return isDevice ? J(Fifa15Trusted(dev)) : isQuestion ? J(new JsonObject { ["string"] = "Already answered question." }) : null;
        }
    }
}
