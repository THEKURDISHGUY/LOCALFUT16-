#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static JsonObject Fifa15Trusted(string dev) => new JsonObject
    {
        ["trusted"] = true, ["exists"] = true, ["locked"] = false, ["changed"] = false, ["token"] = "fut-reborn-phishing-token",
        ["isTrusted"] = true, ["completed"] = true, ["answerRequired"] = false, ["attempts"] = 0L, ["deviceId"] = dev ?? "",
    };

    static JsonObject ConsumableItem(long rid, int cardAsset, int cardSub, string tier, int rating, int rare, string kind, int b, int s, int g, int amount)
    {
        long id = Math.Max(1, L(Club["nextId"])); Club["nextId"] = id + 1;
        var o = new JsonObject
        {
            ["id"] = id, ["itemId"] = id, ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["resourceId"] = rid, ["cardassetid"] = cardAsset, ["cardsubtypeid"] = cardSub,
            ["rating"] = rating, ["rareFlag"] = rare, ["rareflag"] = rare, ["bronze"] = b, ["silver"] = s, ["gold"] = g, ["amount"] = amount,
            ["itemType"] = "development", ["resourceGameYear"] = 2016, ["discardValue"] = 0, ["lastSalePrice"] = 0, ["owners"] = 1,
            ["untradeable"] = false, ["tradeable"] = true, ["itemState"] = "free", ["pile"] = 0, ["consumableKind"] = kind,
        };
        switch (kind)
        {
            case "contract_player": o["consumablesContractPlayer"] = 1L; break;
            case "contract_manager": o["consumablesContractManager"] = 1L; break;
            case "fitness_team": o["consumablesFitnessTeam"] = (long)Math.Max(1, amount); break;
            case "fitness_player": o["consumablesFitnessPlayer"] = (long)Math.Max(1, amount); break;
            case "healing": o["consumablesHealing"] = (long)Math.Max(1, amount); break;
            case "training_player": o["consumablesTraining"] = 1L; o["consumablesTrainingPlayer"] = (long)Math.Max(1, amount); break;
        }
        return o;
    }

    static JsonObject Fifa15ClubStats()
    {
        var unIds = UnassignedIds();
        var items = ((JsonArray)Club["items"]!).Where(x => x != null && x["totwOpp"] == null && !unIds.Contains(L(x["id"])) && x["pile"]?.ToString() != "trade").ToList();
        long players = items.Count(x => x["itemType"]?.ToString() == "player");
        var cons = items.Where(x => x["itemType"]?.ToString() == "development").ToList();
        long K(string k) => cons.Count(x => x["consumableKind"]?.ToString() == k);
        long KP(string p) => cons.Count(x => (x["consumableKind"]?.ToString() ?? "").StartsWith(p));
        long staff = items.Count(x => x["itemType"]?.ToString() == "staff");
        return new JsonObject
        {
            ["playersEmployed"] = players, ["rarePlayersEmployed"] = players, ["bronzePlayersEmployed"] = 0L, ["silverPlayersEmployed"] = 0L, ["goldPlayersEmployed"] = players,
            ["staffEmployed"] = staff, ["kitsAvailable"] = 0L, ["badgesAvailable"] = 0L, ["stadiaOwned"] = 0L, ["ballsEarned"] = 0L, ["trophiesWon"] = 0L,
            ["consumables"] = (long)cons.Count, ["kits"] = 0L, ["badges"] = 0L, ["stadia"] = 0L, ["balls"] = 0L, ["trophies"] = 0L,
            ["consumablesContract"] = KP("contract_"), ["consumablesFitness"] = KP("fitness_"), ["consumablesHealing"] = K("healing"),
            ["consumablesPlaystyle"] = 0L, ["consumablesTraining"] = KP("training_"), ["consumablesContractPlayer"] = K("contract_player"),
            ["consumablesContractManager"] = K("contract_manager"), ["consumablesFitnessPlayer"] = K("fitness_player"),
            ["consumablesFitnessTeam"] = K("fitness_team"), ["consumablesTrainingPlayer"] = K("training_player"),
            ["staffHeadCoach"] = staff, ["staffFitnessCoach"] = 0L, ["staffGKCoach"] = 0L, ["staffPhysio"] = 0L,
        };
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleFifa15Blank(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/") || !File.Exists(FindFile("fifa15shapes.txt"))) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        JsonObject NoEntries() => new JsonObject { ["entries"] = new JsonArray() };
        string what = null; JsonNode reply = null;
        if (method == "GET" && path.EndsWith("/settings"))
        {
            what = "settings";
            reply = new JsonObject { ["configs"] = new JsonArray(
                new JsonObject { ["type"] = "IsStoreEnabled", ["value"] = "1" },
                new JsonObject { ["type"] = "cardPackStoreEnabled", ["value"] = "1" },
                new JsonObject { ["type"] = "pointsPackStoreEnabled", ["value"] = "1" }) };
        }
        else if (method == "GET" && path.EndsWith("/userdata")) { what = "userdata"; reply = new JsonObject { ["onlineELORating"] = 0L, ["onlineRatedUser"] = false, ["accountResetCount"] = 0L }; }
        else if (method == "GET" && path.EndsWith("/store/transaction")) { what = "store/transaction"; reply = new JsonObject { ["state"] = "NOTRANSACTION" }; }
        else if (method == "GET" && path.Contains("clientdata/tutorialpopups")) { what = "tutorialpopups"; reply = NoEntries(); }
        else if (method == "GET" && path.Contains("clientdata/loanplayerreward")) { what = "loanPlayerReward"; reply = NoEntries(); }
        else if (path.Contains("match/reset")) { what = "match/reset"; reply = new JsonObject { ["reset"] = true }; }
        else if (method == "GET" && path.Contains("club/stats/")) { what = "club/stats"; reply = Fifa15ClubStats(); }
        if (what == null) return null;
        Console.WriteLine($"[FIFA15] {what} answered with the FIFA 15 layout");
        return J(reply);
    }
}
