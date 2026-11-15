#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static (int, string, byte[], Dictionary<string, string>?)? HandleOfflineMatchStart(string method, string path, byte[] body)
    {
        if (method != "POST" || !path.EndsWith("/game/fifa16/match")) return null;
        JsonObject rq = null; try { rq = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject; } catch { }
        if (rq == null || (rq["type"]?.ToString() ?? "").ToUpperInvariant() != "OFFLINE") return null;

        Console.WriteLine("[OFFLINE-MATCH] start body: " + Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 500)));
        Club["offlineMatchTournament"] = Encoding.UTF8.GetString(body).ToLowerInvariant().Contains("tournament");
        Club["offlineMatchPending"] = true; Club["offlineMatchStarters"] = new JsonArray();
        int variant = 4;
        try { var f = FindFile("matchreply.txt"); if (File.Exists(f)) int.TryParse(File.ReadAllText(f).Trim(), out variant); } catch { }
        if (variant <= 0) return null;

        try { EnsureSquads(); } catch { }
        long sqid = 0; try { sqid = L(rq["squadId"]); } catch { }
        if (SquadById(sqid) == null) sqid = ActiveSquadId();
        var sqSrc = SquadById(sqid); if (sqSrc == null) return null;
        var sq = (JsonObject)sqSrc.DeepClone(); SquadCleanSlots(sq);

        long seq = 0; try { seq = L(Club["offlineMatchSeq"]); } catch { } seq++; Club["offlineMatchSeq"] = seq;

        if (variant >= 4)
        {
            var ns = NativeSquadReply(sq);
            if (variant >= 5) Make15Exact(ns, variant >= 6, variant >= 7, variant >= 8);
            var r15 = new JsonObject { ["squad"] = ns, ["startDateTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            string t15 = r15.ToJsonString();
            try { File.WriteAllText(Path.Combine(Dir, "last_match_reply.json"), r15.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); } catch { }
            Console.WriteLine($"[OFFLINE-MATCH] POST match squad {sqid} -> FIFA 15 shape (variant {variant}), {t15.Length}B");
            return (200, "application/json", Encoding.UTF8.GetBytes(t15), null);
        }
        var reply = new JsonObject();
        if (variant >= 2)
        {
            var full = NativeSquadReply(sq);
            foreach (var kv in full.ToList()) reply[kv.Key] = kv.Value?.DeepClone();
        }
        reply["matchId"] = seq;
        reply["squadId"] = sqid;
        reply["type"] = "OFFLINE";
        reply["seasonId"] = rq["seasonId"]?.DeepClone() ?? JsonValue.Create(10);
        reply["divisionId"] = rq["divisionId"]?.DeepClone() ?? JsonValue.Create(10);
        reply["expiredItems"] = new JsonArray();
        if (variant >= 3) reply["squad"] = NativeSquadReply(sq);

        string txt = reply.ToJsonString();
        try { File.WriteAllText(Path.Combine(Dir, "last_match_reply.json"), reply.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); } catch { }
        Console.WriteLine($"[OFFLINE-MATCH] POST match squad {sqid} -> reply variant {variant}, matchId {seq}, {txt.Length}B (matchreply.txt: 0 old / 1 small / 2 +squad / 3 +squad twice)");
        return (200, "application/json", Encoding.UTF8.GetBytes(txt), null);
    }

    static void Make15Exact(JsonObject sq, bool dropManager, bool dropActives = false, bool dropEmpty = false)
    {
        void Card(JsonObject c)
        {
            if (c == null) return;
            c["loans"] = 0; c["injuryGames"] = 0; c["injuryType"] = "none"; c["suspension"] = 0; c["loyaltyBonus"] = 1; c["formation"] = "f442";
            c["playStyle"] = 0; c["posMods"] = new JsonArray(); c["statsList"] = new JsonArray(); c["trainingId"] = 0; c["trainingResourceId"] = 0;
            c["untradeable"] = false; c["tradeable"] = true; c["assists"] = 0; c["lifetimeAssists"] = 0;
            c["marketDataMinPrice"] = 150; c["marketDataMaxPrice"] = 15000000;
            if (c["timestamp"] == null) c["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400;
            long ct = 7; try { ct = Math.Max(1, L(c["contracts"] ?? c["contract"])); } catch { }
            c["contract"] = ct; c["contracts"] = ct;
            c.Remove("dream");
        }
        if (sq["players"] is JsonArray pls) foreach (var p in pls) Card(p?["itemData"] as JsonObject);
        sq.Remove("rating"); sq.Remove("squadType");
        if (sq["actives"] is JsonArray act) sq["club"] = act.DeepClone();
        if (dropActives) { sq["actives"] = new JsonArray(); sq["club"] = new JsonArray(); }
        if (dropEmpty && sq["players"] is JsonArray pl2)
        {
            var keep = new JsonArray();
            foreach (var p in pl2) if (p?["itemData"] != null) keep.Add(p.DeepClone());
            sq["players"] = keep;
        }
        if (dropManager) sq["manager"] = new JsonArray();
        else if (sq["manager"] is JsonArray mg) foreach (var m in mg) Card(m?["itemData"] as JsonObject);
    }
}
