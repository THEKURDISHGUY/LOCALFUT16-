#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static HashSet<long> UnassignedIds()
    {
        var s = new HashSet<long>();
        if (Club["unassigned"] is JsonArray ua) foreach (var n in ua) s.Add(L(n));
        return s;
    }

    static List<JsonObject> ClubConsumables()
    {
        var un = UnassignedIds();
        var res = new List<JsonObject>();
        foreach (var c in ((JsonArray)Club["items"]!).Reverse())
        {
            if (c is not JsonObject o) continue;
            if (o["itemType"]?.ToString() != "development") continue;
            if (o["totwOpp"] != null || o["pile"]?.ToString() == "trade") continue;
            if (un.Contains(L(o["id"]))) continue;
            res.Add(o);
        }
        return res;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleConsumables16(string method, string path, string target, byte[] body)
    {
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        if (method == "POST" || method == "PUT")
        {
            var mr = System.Text.RegularExpressions.Regex.Match(path, @"fifa16/item/resource/(\d+)$");
            if (mr.Success) return J(ApplyConsumable16(long.Parse(mr.Groups[1].Value), body));
            return null;
        }
        if (method != "GET" || !path.Contains("fifa16/club/")) return null;
        if (path.Contains("club/consumables"))
        {
            var list = ClubConsumables();
            var arr = new JsonArray(); foreach (var o in list) arr.Add(o.DeepClone());
            Console.WriteLine($"[CONSUMABLES] {path} -> {arr.Count} items");
            return J(new JsonObject { ["itemData"] = arr, ["items"] = arr.DeepClone(), ["totalResults"] = arr.Count, ["count"] = arr.Count });
        }
        if (path.Contains("club/stats/"))
        {
            Console.WriteLine($"[CONSUMABLES] {path} -> club stats");
            return J(Fifa15ClubStats());
        }
        return null;
    }

    static JsonObject ApplyConsumable16(long rid, byte[] body)
    {
        var res = new JsonObject { ["itemData"] = new JsonArray() };
        JsonObject rq = null; try { rq = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject; } catch { }
        var items = (JsonArray)Club["items"]!;
        var un = UnassignedIds();
        JsonObject con = null;
        foreach (var n in items)
            if (n is JsonObject o && o["itemType"]?.ToString() == "development" && L(o["resourceId"]) == rid && o["pile"]?.ToString() != "trade" && !un.Contains(L(o["id"]))) { con = o; break; }
        if (con == null) { Console.WriteLine($"[CONSUMABLE] resource {rid}: none in club"); return res; }
        string kind = con["consumableKind"]?.ToString() ?? "";
        long amount = 0; try { amount = L(con["amount"]); } catch { }

        var ids = new List<long>();
        if (rq?["apply"] is JsonArray ap) foreach (var a in ap) { long i = L(a?["id"]); if (i > 0) ids.Add(i); }
        var targets = new List<JsonObject>();
        foreach (var i in ids) { var it = FindItem(i); if (it != null) targets.Add(it); }
        if (kind.EndsWith("_team") && targets.Count == 0)
            foreach (var n in items) if (n is JsonObject o && o["itemType"]?.ToString() == "player" && o["pile"]?.ToString() != "trade") targets.Add(o);

        string want = kind.StartsWith("contract_manager") ? "staff" : "player";
        var done = new JsonArray();
        foreach (var t in targets)
        {
            if (t["itemType"]?.ToString() != want) continue;
            long rating = 0; try { rating = L(t["rating"]); } catch { }
            string tier = t["quality"]?.ToString(); if (string.IsNullOrEmpty(tier)) tier = Q((int)rating);
            switch (kind)
            {
                case "contract_player":
                case "contract_manager":
                    {
                        long add = L(con[tier == "gold" ? "gold" : tier == "silver" ? "silver" : "bronze"]);
                        if (add <= 0) add = L(con["bronze"]);
                        long cur = 0; try { cur = L(t["contract"]); } catch { }
                        long nv = Math.Min(99, cur + add);
                        t["contract"] = nv; t["contracts"] = nv;
                        break;
                    }
                case "fitness_player":
                case "fitness_team":
                    {
                        long cur = 99; try { cur = L(t["fitness"]); } catch { }
                        t["fitness"] = Math.Min(99, cur + Math.Max(amount, 1));
                        break;
                    }
                case "healing":
                    t["injuryType"] = "none"; t["injuryGames"] = 0;
                    break;
                case "training_player":
                    {
                        long cur = 0; try { cur = L(t["training"]); } catch { }
                        t["training"] = cur + Math.Max(amount, 1);
                        break;
                    }
                default: continue;
            }
            done.Add(t.DeepClone());
        }
        if (done.Count == 0) { Console.WriteLine($"[CONSUMABLE] {kind} ({rid}): no valid target"); return res; }
        long cid = L(con["id"]);
        for (int k = items.Count - 1; k >= 0; k--) if (L(items[k]?["id"]) == cid) { items.RemoveAt(k); break; }
        Save();
        Console.WriteLine($"[CONSUMABLE] used {kind} ({rid}) on {done.Count} card(s)");
        res["itemData"] = done; res["success"] = true;
        return res;
    }
}
