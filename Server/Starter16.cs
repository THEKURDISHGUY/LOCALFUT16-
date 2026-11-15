#nullable disable
#pragma warning disable
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static JsonObject ExtraItem(string type, string name, long resId, int sub = 0)
    {
        long id = Math.Max(1, L(Club["nextId"])); Club["nextId"] = id + 1;
        return new JsonObject
        {
            ["id"] = id, ["resourceId"] = resId, ["assetId"] = resId, ["itemType"] = type, ["cardsubtypeid"] = sub,
            ["rating"] = 0, ["rareflag"] = 0, ["lastSalePrice"] = 0, ["discardValue"] = 50, ["contract"] = 0,
            ["owners"] = 1, ["itemState"] = "free", ["pile"] = 0, ["lifetimeStats"] = new JsonArray(),
            ["name"] = name, ["quality"] = "bronze", ["nation"] = 0, ["leagueId"] = 0, ["teamid"] = 0,
        };
    }

    static void EnsureStarterPlayers()
    {
        var items = (JsonArray)Club["items"]!;
        int n = items.Count(i => i != null && (i["itemType"]?.ToString() ?? "player") == "player" && i["pile"]?.ToString() != "trade");
        if (n >= 11) return;
        Console.WriteLine($"[STARTER] club has only {n} players -> giving the starter squad (bronze / silver)");
        GiveStarterSquad();
        Club.Remove("autoSquadV2"); Save();
        Console.WriteLine($"[STARTER] club now has {items.Count} cards");
    }

    static void StarterGift()
    {
        if (Club["starterFix2"] == null)
        {
            try
            {
                var all = (JsonArray)Club["items"]!;
                if (Club["unassigned"] is JsonArray u0)
                {
                    var keep0 = new JsonArray();
                    foreach (var n in u0)
                    {
                        long uid = L(n); var it0 = all.FirstOrDefault(x => L(x?["id"]) == uid);
                        if (it0 == null || (it0["itemType"]?.ToString() ?? "") == "staff") continue;
                        keep0.Add(uid);
                    }
                    Club["unassigned"] = keep0;
                }
            }
            catch { }
            Club["starterFix2"] = 1; Save();
        }
        if (Club["starterGift"] is JsonNode g && g.GetValue<bool>()) return;
        if (Managers.Count == 0) { Console.WriteLine("[STARTER] no managers loaded (male_coaches.csv) - no gift"); return; }
        var items = (JsonArray)Club["items"]!;
        var un = new JsonArray(); if (Club["unassigned"] is JsonArray ou) foreach (var o in ou) un.Add(L(o));
        var mg = ManagerItem(Managers[Rng.Next(Managers.Count)]);
        items.Add(mg.DeepClone());
        Console.WriteLine($"[STARTER] gift: random manager {mg["name"]}");
        if (File.Exists(FindFile("extras.txt")))
        {
            var extras = new[]
            {
                ConsumableItem(5001003, 7, 201, "gold", 80, 0, "contract_player", 15, 11, 13, 0),
                ConsumableItem(5001009, 8, 202, "gold", 80, 0, "contract_manager", 11, 11, 13, 0),
                ConsumableItem(5002003, 10, 219, "gold", 80, 0, "fitness_player", 0, 0, 0, 60),
                ConsumableItem(5002006, 10, 220, "gold", 80, 1, "fitness_team", 0, 0, 0, 30),
                ConsumableItem(5002009, 9, 211, "gold", 80, 0, "healing", 0, 0, 0, 5),
                ConsumableItem(5003024, 1, 61, "gold", 85, 0, "training_player", 0, 0, 0, 15),
            };
            foreach (var e in extras) { items.Add(e.DeepClone()); }
            Console.WriteLine("[STARTER] gift: 6 consumables in the real FIFA 15 layout (extras.txt)");
        }
        Club["unassigned"] = un; Club["starterGift"] = true; Save();
    }
}
