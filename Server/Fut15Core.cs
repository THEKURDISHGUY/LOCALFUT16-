#nullable disable
#pragma warning disable
// FIFA 15 Local FUT - C# port, stage 1 (ported from server.py v0.2.39).
//
// Ported 1:1 from the Python server:  redirector (42230), QoS (17502), EASW (42232),
// Blaze/Fire2 + TDF (10051), FUT/POW HTTP (8199 + 8099) bootstrap, store, pack opening,
// club / squad basics, credits.
//
// NOT in stage 1:  AI transfer market, transfer-list auctions, offline seasons / match
// results, consumable apply, club cosmetics, discard.  Those routes fall through to the
// same "unknown request -> {}" behaviour the Python server uses, and they are logged.
//
// Origin/EA app login is NOT emulated. Sign in with the real EA app; this server does not use port 3216.
//
// Needs the same data files as the Python version, next to the exe:
//   players.json (required for packs), config.json and pack_settings.json (optional),
//   assets\packs\packs_backgrounds_<id>.png (optional).

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalFut15;

// ------------------------------------------------------------------ config / log / utils

static class Cfg
{
    public const string Version = "0.5.0-fifa16-fut-reborn";
    public const int RedirectPort = 42230, BlazePort = 10051, QosPort = 17502, EaswPort = 42232, FutPort = 8199, LegacyFutPort = 8099;
    // These are local FUT profile identifiers.  They are deliberately
    // configurable so every local save can have a stable, distinct profile.
    // These are local profile identifiers used by the local FUT emulation.
    public static long NucleusId = 1000000001, PersonaId = 1000000002;
    public static int ClubId = 1;
    public static string DisplayName = "THEKURDISHGUY", ClubName = "THEKURDISHGUY", ClubAbbr = "TKG";
    public static long StartCredits = 2000000;
    // Bump this number to wipe every saved club ONCE on the next start (old state.json is kept as state.json.old-<time>).
    public const int ProfileRev = 3;
    // Manager card in the starter squad. The resource id is a GUESS until the real FIFA 16 manager ids are known:
    // if the manager slot shows a blank card, set "manager_resource_id" (and rating/nation/league) in config.json.
    public static bool ManagerEnabled = true;
    public static long ManagerRid = 5000001, ManagerRating = 80, ManagerNation = 14, ManagerLeague = 13;
    public static long StartPoints = 100;
    public static List<long> StarterOverride = new();
    public static readonly string Root = AppContext.BaseDirectory;
    public static readonly string Runtime = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local"),
        "FIFA16LocalFUT");
    public static readonly long[] StarterPlayers = { 244809, 231538, 243347, 243929, 208544, 238046, 252019, 17021416, 246839, 221881, 228212, 245797, 242814, 245222, 211663 };

    // Finds a data file next to the exe, in the working folder, in parent folders of the exe, or in bin\Release / bin\Debug.
    public static string Find(string name)
    {
        var seen = new HashSet<string>();
        var dirs = new List<string> { Root, Directory.GetCurrentDirectory(), Runtime };
        try { var pd = Path.GetDirectoryName(Environment.ProcessPath ?? ""); if (!string.IsNullOrEmpty(pd)) dirs.Add(pd); } catch { }
        var d = new DirectoryInfo(Root);
        for (int i = 0; i < 6 && d != null; i++)
        {
            dirs.Add(d.FullName);
            dirs.Add(Path.Combine(d.FullName, "bin", "Release", "net8.0"));
            dirs.Add(Path.Combine(d.FullName, "bin", "Debug", "net8.0"));
            d = d.Parent;
        }
        foreach (var x in dirs)
        {
            if (string.IsNullOrEmpty(x)) continue;
            var f = Path.Combine(x, name);
            if (seen.Add(f) && File.Exists(f)) return f;
        }
        return Path.Combine(Root, name);
    }

    public static void Load()
    {
        var p = Find("config.json");
        if (!File.Exists(p)) return;
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(p), null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
            if (o == null) return;
            DisplayName = U.S(o, "display_name", DisplayName);
            NucleusId = U.L(o, "nucleus_id", NucleusId);
            PersonaId = U.L(o, "persona_id", PersonaId);
            ClubId = (int)U.L(o, "club_id", ClubId);
            ClubName = U.S(o, "club_name", ClubName);
            ClubAbbr = U.S(o, "club_abbr", ClubAbbr);
            StartCredits = Math.Max(2000000, U.L(o, "credits", StartCredits));   // never below 2,000,000
            ManagerEnabled = U.B(o, "manager_enabled", ManagerEnabled);
            ManagerRid = U.L(o, "manager_resource_id", ManagerRid);
            ManagerRating = U.L(o, "manager_rating", ManagerRating);
            ManagerNation = U.L(o, "manager_nation", ManagerNation);
            ManagerLeague = U.L(o, "manager_league", ManagerLeague);
            StartPoints = U.L(o, "points", StartPoints);
            if (o["starter_players"] is JsonArray sp) foreach (var n in sp) { try { StarterOverride.Add((long)n); } catch { } }

            if (NucleusId <= 0) NucleusId = 1000000001;
            if (PersonaId <= 0) PersonaId = 1000000002;
            if (ClubId <= 0) ClubId = 1;
        }
        catch (Exception e) { Log.Warn("config.json ignored: " + e.Message); }
    }
}

static class Log
{
    static readonly object L = new();
    static StreamWriter _w;
    public static string Dir;
    public static void Init(string dir)
    {
        Dir = dir;
        Directory.CreateDirectory(dir);
        _w = new StreamWriter(Path.Combine(dir, "server.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
    }
    static void W(string lvl, string m)
    {
        var s = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} {lvl} {m}";
        lock (L) { Console.WriteLine(s); _w?.WriteLine(s); }
    }
    public static void Info(string m) => W("INFO", m);
    public static void Warn(string m) => W("WARNING", m);
    public static void Error(string m) => W("ERROR", m);
}

static class U
{
    public static long NowS() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    public static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
    public static byte[] Cat(params byte[][] parts)
    {
        int n = 0; foreach (var p in parts) n += p.Length;
        var o = new byte[n]; int i = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, i, p.Length); i += p.Length; }
        return o;
    }
    public static long LN(JsonNode n, long d = 0)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var dd)) return (long)dd;
            if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
            if (v.TryGetValue<string>(out var s) && long.TryParse(s, out var p)) return p;
        }
        return d;
    }
    public static long L(JsonObject o, string k, long d = 0) => o != null && o.TryGetPropertyValue(k, out var n) && n != null ? LN(n, d) : d;
    public static string S(JsonObject o, string k, string d = "")
    {
        if (o != null && o.TryGetPropertyValue(k, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return d;
    }
    public static bool B(JsonObject o, string k, bool d = false)
    {
        if (o != null && o.TryGetPropertyValue(k, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            return LN(n, d ? 1 : 0) != 0;
        }
        return d;
    }
    public static double D(JsonObject o, string k, double d = 0)
    {
        if (o != null && o.TryGetPropertyValue(k, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var x)) return x;
            if (v.TryGetValue<long>(out var l)) return l;
        }
        return d;
    }
    public static byte[] Json(JsonNode n) => Encoding.UTF8.GetBytes(n.ToJsonString());
    public static readonly Random Rng = new();
    public static void Shuffle<T>(List<T> l)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
    }
}

// ------------------------------------------------------------------ TDF (Blaze Heat2) encoding

static class Tdf
{
    public const int VARINT = 0, STRING = 1, BLOB = 2, GROUP = 3, LIST = 4, MAP = 5, OBJECT_ID = 9;

    public static byte[] Tag(string tag, int type)
    {
        var raw = Encoding.ASCII.GetBytes(tag);
        if (raw.Length < 1 || raw.Length > 4) throw new ArgumentException("TDF tag must be 1-4 bytes: " + tag);
        int o0 = 0, o1 = 0, o2 = 0, o3 = type & 0xFF;
        if (raw.Length > 0) { o0 |= (raw[0] & 0x40) << 1; o0 |= (raw[0] & 0x10) << 2; o0 |= (raw[0] & 0x0F) << 2; }
        if (raw.Length > 1) { o0 |= (raw[1] & 0x40) >> 5; o0 |= (raw[1] & 0x10) >> 4; o1 |= (raw[1] & 0x0F) << 4; }
        if (raw.Length > 2) { o1 |= (raw[2] & 0x40) >> 3; o1 |= (raw[2] & 0x10) >> 2; o1 |= (raw[2] & 0x0C) >> 2; o2 |= (raw[2] & 0x03) << 6; }
        if (raw.Length > 3) { o2 |= (raw[3] & 0x40) >> 1; o2 |= raw[3] & 0x1F; }
        return new[] { (byte)o0, (byte)o1, (byte)o2, (byte)o3 };
    }

    public static byte[] Varint(long value)
    {
        ulong v = unchecked((ulong)value);
        if (v < 0x40) return new[] { (byte)v };
        var o = new List<byte> { (byte)((v & 0x3F) | 0x80) };
        v >>= 6;
        while (v >= 0x80) { o.Add((byte)((v & 0x7F) | 0x80)); v >>= 7; }
        o.Add((byte)(v & 0x7F));
        return o.ToArray();
    }

    public static byte[] StrValue(string s)
    {
        var raw = Encoding.UTF8.GetBytes(s ?? "");
        return U.Cat(Varint(raw.Length + 1), raw, new byte[] { 0 });
    }

    public static byte[] Int(string tag, long v) => U.Cat(Tag(tag, VARINT), Varint(v));
    public static byte[] Str(string tag, string v) => U.Cat(Tag(tag, STRING), StrValue(v));
    public static byte[] Blob(string tag, byte[] v) => U.Cat(Tag(tag, BLOB), Varint(v.Length), v);
    public static byte[] ObjectId(string tag, long comp, long etype, long eid) => U.Cat(Tag(tag, OBJECT_ID), Varint(comp), Varint(etype), Varint(eid));
    public static byte[] Group(string tag, byte[] body) => U.Cat(Tag(tag, GROUP), body, new byte[] { 0 });

    public static byte[] ListInt(string tag, params long[] values)
    {
        var o = new List<byte>(Tag(tag, LIST)) { VARINT };
        o.AddRange(Varint(values.Length));
        foreach (var v in values) o.AddRange(Varint(v));
        return o.ToArray();
    }

    public static byte[] ListStr(string tag, params string[] values)
    {
        var o = new List<byte>(Tag(tag, LIST)) { STRING };
        o.AddRange(Varint(values.Length));
        foreach (var v in values) o.AddRange(StrValue(v));
        return o.ToArray();
    }

    public static byte[] ListGroups(string tag, IList<byte[]> groups)
    {
        var o = new List<byte>(Tag(tag, LIST)) { GROUP };
        o.AddRange(Varint(groups.Count));
        foreach (var g in groups) { o.AddRange(g); o.Add(0); }
        return o.ToArray();
    }

    public static byte[] MapStrStr(string tag, IList<(string, string)> values)
    {
        var o = new List<byte>(Tag(tag, MAP)) { STRING, STRING };
        o.AddRange(Varint(values.Count));
        foreach (var (k, v) in values) { o.AddRange(StrValue(k)); o.AddRange(StrValue(v)); }
        return o.ToArray();
    }

    public static byte[] MapStrGroup(string tag, IList<(string, byte[])> values)
    {
        var o = new List<byte>(Tag(tag, MAP)) { STRING, GROUP };
        o.AddRange(Varint(values.Count));
        foreach (var (k, body) in values) { o.AddRange(StrValue(k)); o.AddRange(body); o.Add(0); }
        return o.ToArray();
    }

    public static (long value, int pos) DecodeVarint(byte[] raw, int pos)
    {
        if (pos >= raw.Length) throw new InvalidDataException("truncated TDF varint");
        int first = raw[pos++];
        ulong value = (ulong)(first & 0x3F);
        int shift = 6;
        if ((first & 0x80) != 0)
        {
            while (true)
            {
                if (pos >= raw.Length) throw new InvalidDataException("truncated TDF varint");
                int b = raw[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
        }
        return (unchecked((long)value), pos);
    }

    static int IndexOf(byte[] hay, byte[] needle, bool last)
    {
        if (needle.Length == 0 || hay.Length < needle.Length) return -1;
        if (!last)
        {
            for (int i = 0; i <= hay.Length - needle.Length; i++) if (Match(hay, needle, i)) return i;
        }
        else
        {
            for (int i = hay.Length - needle.Length; i >= 0; i--) if (Match(hay, needle, i)) return i;
        }
        return -1;
    }
    static bool Match(byte[] h, byte[] n, int at) { for (int j = 0; j < n.Length; j++) if (h[at + j] != n[j]) return false; return true; }

    public static string GetString(byte[] raw, string tag)
    {
        int p = IndexOf(raw, Tag(tag, STRING), false);
        if (p < 0) return null;
        p += 4;
        try
        {
            var (n, np) = DecodeVarint(raw, p);
            if (n <= 0 || np + n > raw.Length) return null;
            int len = (int)n;
            if (raw[np + len - 1] == 0) len--;
            return Encoding.UTF8.GetString(raw, np, len);
        }
        catch { return null; }
    }

    public static long? GetIntLast(byte[] raw, string tag)
    {
        var marker = Tag(tag, VARINT);
        int p = IndexOf(raw, marker, true);
        if (p < 0) return null;
        try { return DecodeVarint(raw, p + marker.Length).value; } catch { return null; }
    }
}

// ------------------------------------------------------------------ Fire2 framing

sealed class Packet
{
    public int Component, Command, MsgNum, MsgType;
    public byte[] Metadata, Payload, Raw;
}

static class Fire2
{
    public static Packet TryParse(List<byte> buf)
    {
        if (buf.Count < 16) return null;
        long payloadSize = ((long)buf[0] << 24) | ((long)buf[1] << 16) | ((long)buf[2] << 8) | buf[3];
        int metaSize = (buf[4] << 8) | buf[5];
        if (payloadSize > 16 * 1024 * 1024 || metaSize > 1024 * 1024)
            throw new InvalidDataException($"unreasonable Fire2 sizes payload={payloadSize} metadata={metaSize}");
        long total = 16 + metaSize + payloadSize;
        if (buf.Count < total) return null;
        var raw = buf.GetRange(0, (int)total).ToArray();
        buf.RemoveRange(0, (int)total);
        var p = new Packet
        {
            Raw = raw,
            Component = (raw[6] << 8) | raw[7],
            Command = (raw[8] << 8) | raw[9],
            MsgNum = (raw[10] << 16) | (raw[11] << 8) | raw[12],
            MsgType = raw[13] >> 5,
            Metadata = raw[16..(16 + metaSize)],
            Payload = raw[(16 + metaSize)..],
        };
        return p;
    }

    public static byte[] Build(int comp, int cmd, long msgNum, int msgType, byte[] payload = null, byte[] meta = null)
    {
        payload ??= Array.Empty<byte>(); meta ??= Array.Empty<byte>();
        var o = new List<byte>(16 + meta.Length + payload.Length);
        o.Add((byte)(payload.Length >> 24)); o.Add((byte)(payload.Length >> 16)); o.Add((byte)(payload.Length >> 8)); o.Add((byte)payload.Length);
        o.Add((byte)(meta.Length >> 8)); o.Add((byte)meta.Length);
        o.Add((byte)(comp >> 8)); o.Add((byte)comp);
        o.Add((byte)(cmd >> 8)); o.Add((byte)cmd);
        long mn = msgNum & 0xFFFFFF;
        o.Add((byte)(mn >> 16)); o.Add((byte)(mn >> 8)); o.Add((byte)mn);
        o.Add((byte)((msgType & 7) << 5)); o.Add(0); o.Add(0);
        o.AddRange(meta); o.AddRange(payload);
        return o.ToArray();
    }
}

// ------------------------------------------------------------------ Blaze payloads (what FIFA 15 needs)

static class Blaze
{
    static string Persona => Cfg.DisplayName;
    const long Loc = 1701729619;

    public static byte[] PreAuth()
    {
        var conf = Tdf.MapStrStr("CONF", new[]
        {
            ("connIdleTimeout", "120s"), ("defaultRequestTimeout", "80s"), ("pingPeriod", "20s"),
            ("voipHeadsetUpdateRate", "1000"), ("xlspConnectionIdleTimeout", "300"),
        });
        var bwps = U.Cat(Tdf.Str("PSA", "127.0.0.1"), Tdf.Int("PSP", Cfg.QosPort), Tdf.Str("SNA", "qos"));
        var lat = U.Cat(Tdf.Str("PSA", "127.0.0.1"), Tdf.Int("PSP", Cfg.QosPort), Tdf.Str("SNA", "ea-sjc"));
        var qoss = U.Cat(
            Tdf.Group("BWPS", bwps), Tdf.Int("LNP", 10),
            Tdf.MapStrGroup("LTPS", new[] { ("ea-sjc", lat) }),
            Tdf.Int("SVID", 1161889797), Tdf.Int("TIME", 5000));
        return U.Cat(
            Tdf.Str("ASRC", "303107"), Tdf.ListInt("CIDS", 1, 9, 35, 30722), Tdf.Group("CONF", conf),
            Tdf.Int("EEFA", 1), Tdf.Str("ESRC", "303107"), Tdf.Str("INST", "fifa-2015-pc"),
            Tdf.Int("MAID", 89), Tdf.Int("MINR", 0), Tdf.Str("NASP", "cem_ea_id"), Tdf.Str("PILD", ""),
            Tdf.Str("PLAT", "pc"), Tdf.Group("QOSS", qoss), Tdf.Str("RSRC", "303107"), Tdf.Str("SVER", "Blaze 14.2.1.1.0"));
    }

    public static byte[] AuthPayload()
    {
        var pdtl = U.Cat(Tdf.Str("DSNM", Persona), Tdf.Int("LAST", 0), Tdf.Int("PID", Cfg.PersonaId), Tdf.Int("PLAT", 4), Tdf.Int("STAS", 0), Tdf.Int("XREF", Cfg.NucleusId));
        var session = U.Cat(
            Tdf.Int("1CON", 0), Tdf.Int("BUID", Cfg.PersonaId), Tdf.Int("FRST", 0), Tdf.Str("KEY", Fut15Program.State.Token),
            Tdf.Int("LLOG", U.NowS()), Tdf.Str("MAIL", "local@localhost"), Tdf.Group("PDTL", pdtl), Tdf.Int("UID", Cfg.NucleusId));
        return U.Cat(
            Tdf.Int("CNTX", 0), Tdf.Int("ERRC", 0), Tdf.Str("SKEY", Fut15Program.State.Token), Tdf.Int("ANON", 0), Tdf.Int("NTOS", 0),
            Tdf.Group("SESS", session), Tdf.Int("SPAM", 0), Tdf.Int("UNDR", 0));
    }

    public static byte[] PostAuth()
    {
        var pss = U.Cat(Tdf.Int("PORT", 0), Tdf.Int("RPRT", 0), Tdf.Int("TIID", 0));
        var tick = U.Cat(Tdf.Str("ADRS", "127.0.0.1"), Tdf.Int("PORT", 8999), Tdf.Str("SKEY", ""));
        var urop = U.Cat(Tdf.Int("TMOP", 0), Tdf.Int("UID", Cfg.PersonaId));
        return U.Cat(Tdf.Group("PSS", pss), Tdf.Group("TELE", Telemetry()), Tdf.Group("TICK", tick), Tdf.Group("UROP", urop));
    }

    public static byte[] Telemetry() => U.Cat(
        Tdf.Str("ADRS", "127.0.0.1"), Tdf.Int("ANON", 0), Tdf.Str("DISA", "1"), Tdf.Str("FILT", ""), Tdf.Int("LOC", Loc),
        Tdf.Str("NOOK", ""), Tdf.Int("PORT", 9988), Tdf.Int("SDLY", 0), Tdf.Str("SESS", ""), Tdf.Str("SKEY", ""),
        Tdf.Int("SPCT", 0), Tdf.Str("STIM", ""));

    public static byte[] Entitlements()
    {
        var groups = new List<byte[]>();
        int idx = 1;
        foreach (var gnam in new[] { "FIFA15PCBoxContent", "FIFA14PC" })
        {
            groups.Add(U.Cat(
                Tdf.Str("DEVI", ""), Tdf.Str("GDAY", ""), Tdf.Str("GNAM", gnam), Tdf.Int("ID", idx), Tdf.Int("ISCO", 0),
                Tdf.Int("PID", Cfg.PersonaId), Tdf.Str("PJID", ""), Tdf.Int("PRCA", 0), Tdf.Str("PRID", ""), Tdf.Int("STAT", 1),
                Tdf.Int("STRC", 0), Tdf.Str("TAG", "ONLINE_ACCESS"), Tdf.Str("TDAY", ""), Tdf.Int("TYPE", 1), Tdf.Int("UCNT", 0), Tdf.Int("VER", 0)));
            idx++;
        }
        return Tdf.ListGroups("NLST", groups);
    }

    public static byte[] Fifa35Login()
    {
        var pdtl = U.Cat(Tdf.Str("DSNM", Persona), Tdf.Int("PID", Cfg.PersonaId), Tdf.Int("PLAT", 4));
        var sess = U.Cat(
            Tdf.Int("1CON", 0), Tdf.Int("BUID", Cfg.PersonaId), Tdf.Int("FRST", 0), Tdf.Str("KEY", "localfut15-blaze"),
            Tdf.Int("LLOG", U.NowS()), Tdf.Str("MAIL", "localfut15@localhost"), Tdf.Group("PDTL", pdtl), Tdf.Int("UID", Cfg.PersonaId));
        return U.Cat(Tdf.Int("ANON", 0), Tdf.Int("NTOS", 0), Tdf.Group("SESS", sess), Tdf.Int("SPAM", 1), Tdf.Int("UNDR", 0));
    }

    static byte[] ExtendedDataMinimal()
    {
        var qdat = U.Cat(Tdf.Int("DBPS", 0), Tdf.Int("NATT", 0), Tdf.Int("UBPS", 0));
        return U.Cat(Tdf.Int("HWFG", 0), Tdf.Group("QDAT", qdat), Tdf.Int("UATT", 0));
    }

    public static List<(int comp, int cmd, byte[] payload, string label)> LoginNotifications()
    {
        long p = Cfg.PersonaId;
        var user = U.Cat(
            Tdf.Int("AID", p), Tdf.Int("ALOC", Loc), Tdf.Blob("EXBB", Array.Empty<byte>()), Tdf.Int("EXID", 0), Tdf.Int("ID", p),
            Tdf.Str("NAME", Persona), Tdf.Str("NASP", "cem_ea_id"), Tdf.Int("ORIG", p), Tdf.Int("PIDI", 0));
        var added = U.Cat(Tdf.Group("DATA", ExtendedDataMinimal()), Tdf.Group("USER", user));
        var login = U.Cat(
            Tdf.Int("BUID", p), Tdf.ObjectId("CGID", 30722, 2, p), Tdf.Str("DSNM", Persona), Tdf.Str("KEY", "localfut15-blaze"),
            Tdf.Int("LAST", U.NowS()), Tdf.Str("MAIL", "localfut15@localhost"), Tdf.Str("NASP", "cem_ea_id"), Tdf.Int("PID", p),
            Tdf.Int("PLAT", 0), Tdf.Int("UID", p), Tdf.Int("USTP", 0), Tdf.Int("XREF", 0));
        var ext = U.Cat(Tdf.Group("DATA", ExtendedDataMinimal()), Tdf.Int("USID", p));
        var upd = U.Cat(Tdf.Int("FLGS", 3), Tdf.Int("ID", p));
        return new()
        {
            (30722, 2, added, "UserSessions.UserAdded"),
            (30722, 8, login, "UserSessions.LoginSession"),
            (30722, 1, ext, "UserSessions.ExtendedDataUpdate"),
            (30722, 5, upd, "UserSessions.UserUpdated"),
        };
    }

    static List<(string, string)> OsdkCore()
    {
        int fut = Cfg.FutPort, easw = Cfg.EaswPort;
        return new()
        {
            ("AUTH_TYPE", "NUCLEUS"),
            ("CONTENT_URL", $"http://127.0.0.1:{easw}/content"),
            ("CTL_UPDATE_URL", $"http://127.0.0.1:{easw}/ctl"),
            ("FIFA_POW_CONTENT_SERVER_URL", $"http://127.0.0.1:{fut}/"),
            ("FIFA_POW_NUCLEUS_PROXY_URL", $"http://127.0.0.1:{fut}/"),
            ("FIFA_POW_URL", $"http://127.0.0.1:{fut}/"),
            ("FUT_RS4_BASE_URL", $"http://127.0.0.1:{fut}/"),
            ("FUTDYNAMICMESSAGES_CUSTOMURL", $"127.0.0.1:{fut}"),
            ("FUTDYNAMICMESSAGES_URL_BASE", $"http://127.0.0.1:{fut}"),
            ("NUCLEUS_LOGIN_ENABLED", "1"),
            ("ONLINE/FUTDYNAMICMESSAGES_CUSTOMURL", $"127.0.0.1:{fut}"),
            ("ONLINE/FUTDYNAMICMESSAGES_TUTORIAL_MSG_URL", $"http://127.0.0.1:{fut}/"),
            ("ORIGIN_LOGIN_ENABLED", "1"),
            ("OSDK_AUTH_REQUIRED", "1"),
            ("OSDK_EASW_AUTH_URL", $"http://127.0.0.1:{easw}"),
            ("OSDK_EASW_EVENT_URL", $"http://127.0.0.1:{easw}"),
            ("OSDK_EASW_MEDIA_URL", $"http://127.0.0.1:{easw}"),
            ("OSDK_EASW_REQ_URL", $"http://127.0.0.1:{easw}"),
            ("ROSTERUPDATE_URL", $"http://127.0.0.1:{easw}/roster"),
            ("ROUTINGCFGFILE_URL", $"http://127.0.0.1:{easw}/routing"),
            ("USE_TOKEN_AUTH", "1"),
        };
    }

    static byte[] FetchConfig(string cfid)
    {
        var items = cfid == "OSDK_CORE" ? OsdkCore() : new List<(string, string)>();
        return U.Cat(Tdf.MapStrStr("CONF", items), Tdf.Str("ID", cfid), Tdf.Str("ITYP", ""), Tdf.Str("TOKN", ""));
    }

    public static byte[] GameReportingResult(long id)
    {
        id = Math.Max(0, id);
        return U.Cat(Tdf.Int("EROR", 0), Tdf.Int("FNL", 1), Tdf.Int("GHID", id), Tdf.Int("GRID", id));
    }

    static readonly HashSet<(int, int)> CapturedEmpty = new()
    {
        (30722, 20), (30722, 8), (21, 10), (15, 2), (10, 1), (25, 6), (11, 2600), (7, 15), (7, 3), (11, 1600), (0, 0),
    };

    public static (byte[] payload, string label) Respond(Packet p)
    {
        int c = p.Component, k = p.Command;
        if (c == 9 && k == 7) return (PreAuth(), "Util.PreAuth");
        if (c == 9 && k == 2) return (Tdf.Int("STIM", U.NowS()), "Util.Ping");
        if (c == 9 && k == 1)
        {
            var cfid = Tdf.GetString(p.Payload, "CFID") ?? "";
            Log.Info("Util.FetchClientConfig CFID=" + cfid);
            return (FetchConfig(cfid), $"Util.FetchClientConfig({cfid})");
        }
        if (c == 9 && k == 8) return (PostAuth(), "Util.PostAuth");
        if (c == 9 && k == 5) return (Telemetry(), "Util.GetTelemetryServer");
        if (c == 9 && k == 12) return (Tdf.MapStrStr("SMAP", new[] { ("FirstTimeFlag", "0") }), "Util.GetUserOptions");
        if (c == 9 && k == 10)
        {
            var key = Tdf.GetString(p.Payload, "KEY") ?? "";
            var value = key == "FirstTimeFlag" ? "0" : "";
            return (U.Cat(Tdf.Str("DATA", value), Tdf.Str("KEY", key)), $"Util.GetUserOption({key})");
        }
        if (c == 9 && (k == 22 || k == 28)) return (Array.Empty<byte>(), "Util.ClientState/Metrics");
        if (c == 35 && k == 10)
        {
            Log.Info("FIFA35 login request: payload=" + U.Hex(p.Payload) + "; replying with local session");
            return (Fifa35Login(), "FIFA35.LoginSession");
        }
        if (c == 28 && k == 2) return (Array.Empty<byte>(), "GameReporting.SubmitOffline");
        if (CapturedEmpty.Contains((c, k))) return (Array.Empty<byte>(), "captured-empty");
        if (c == 2249 && k == 1)
        {
            var entry = U.Cat(Tdf.Str("ID", "O_TKfilter"), Tdf.Int("LOCF", 0), Tdf.Int("TOGG", 0));
            return (Tdf.ListGroups("LSST", new[] { entry }), "Ticker.GetSettings");
        }
        if (c == 2249 && k == 2)
        {
            var entry = U.Cat(Tdf.Str("ID", "O_SG_TCKR"), Tdf.ListStr("LSET", "O_TKfilter"));
            return (Tdf.ListGroups("LGRP", new[] { entry }), "Ticker.GetGroups");
        }
        if (c == 1 && k == 0x20) return (Entitlements(), "Authentication.ListEntitlements");
        if (c == 1 && (k == 0x28 || k == 0x32 || k == 0x3C || k == 0x98 || k == 0x0A)) return (AuthPayload(), $"Authentication.LocalLogin(0x{k:X})");
        if (c == 1 && (k == 0x1D || k == 0x30)) return (Entitlements(), $"Authentication.Entitlements(0x{k:X})");
        if (c == 1 && k == 0x24) return (Tdf.Str("AUTH", Fut15Program.State.Token), "Authentication.GetAuthToken");
        if (c == 1 && (k == 0x1E || k == 0x5A || k == 0x64 || k == 0x6E)) return (AuthPayload(), $"Authentication.AccountPersona(0x{k:X})");
        if (c == 1 && k == 0x46) return (Array.Empty<byte>(), "Authentication.Logout");
        return (Array.Empty<byte>(), "unknown-empty");
    }

    // One Blaze client connection on 10051 (also accepts plain HTTP like the Python server).
    public static void Client(TcpClient cl)
    {
        using var _ = cl;
        var peer = cl.Client.RemoteEndPoint?.ToString() ?? "?";
        var ns = cl.GetStream();
        cl.ReceiveTimeout = 120000;
        var tmp = new byte[65536];
        int n;
        try { n = ns.Read(tmp, 0, tmp.Length); } catch { return; }
        if (n <= 0) return;
        if (HttpLite.LooksLikeHttp(tmp, n))
        {
            HttpLite.ServeStream(ns, tmp, n, "BLAZE-HTTP", Fut.Handle, false);
            return;
        }
        var buf = new List<byte>();
        buf.AddRange(new ArraySegment<byte>(tmp, 0, n));
        bool Send(byte[] b) { try { ns.Write(b, 0, b.Length); return true; } catch { return false; } }

        while (true)
        {
            Packet p;
            try { p = Fire2.TryParse(buf); }
            catch (Exception e)
            {
                var f = Path.Combine(Log.Dir, $"blaze10051-unparsed-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.bin");
                File.WriteAllBytes(f, buf.ToArray());
                Log.Error($"10051 Fire2 framing failed; saved {Path.GetFileName(f)}: {e.Message}");
                return;
            }
            if (p == null)
            {
                try { n = ns.Read(tmp, 0, tmp.Length); } catch { return; }
                if (n <= 0) return;
                buf.AddRange(new ArraySegment<byte>(tmp, 0, n));
                continue;
            }
            Log.Info($"FIRE2 RX c={p.Component} cmd={p.Command}(0x{p.Command:X}) msg={p.MsgNum} type={p.MsgType} payload={p.Payload.Length} metadata={p.Metadata.Length}");
            if (p.MsgType == 4)
            {
                if (!Send(Fire2.Build(p.Component, p.Command, p.MsgNum, 5))) return;
                Log.Info($"FIRE2 TX PingReply msg={p.MsgNum}");
                continue;
            }
            if (p.MsgType != 0) continue;

            var (payload, label) = Respond(p);
            if (!Send(Fire2.Build(p.Component, p.Command, p.MsgNum, 1, payload))) return;
            Log.Info($"FIRE2 TX {label} c={p.Component} cmd={p.Command} msg={p.MsgNum} payload={payload.Length}");

            if (label == "FIFA35.LoginSession")
            {
                foreach (var (nc, nk, np, nl) in LoginNotifications())
                {
                    if (!Send(Fire2.Build(nc, nk, 0, 2, np))) return;
                    Log.Info($"FIRE2 TX NOTIFY {nl} c={nc} cmd={nk} payload={np.Length}");
                }
            }
            if (label == "GameReporting.SubmitOffline")
            {
                long rid = Tdf.GetIntLast(p.Payload, "GRID") ?? 0;
                var np = GameReportingResult(rid);
                Thread.Sleep(20);
                if (!Send(Fire2.Build(28, 114, 0, 2, np))) return;
                Log.Warn($"GAME REPORTING RESULT NOTIFY c=28 cmd=114 reportId={rid} payload={U.Hex(np)}");
            }
            if (label == "unknown-empty")
            {
                Log.Warn($"CARDS-SCOUT Unhandled Blaze request c={p.Component} cmd={p.Command}(0x{p.Command:X}) msg={p.MsgNum} payload={p.Payload.Length} hex={U.Hex(p.Payload)}");
                try { File.WriteAllText(Path.Combine(Log.Dir, $"cards-scout-c{p.Component}-k{p.Command}-m{p.MsgNum}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt"), U.Hex(p.Payload)); } catch { }
            }
        }
    }
}

// ------------------------------------------------------------------ tiny HTTP/1.1 server (raw sockets, keeps "//pow/auth" intact)

sealed class HttpReq
{
    public string Method, Target;
    public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body = Array.Empty<byte>();
}

sealed class HttpRes
{
    public int Status = 200;
    public Dictionary<string, string> Headers = new();
    public byte[] Body = Array.Empty<byte>();
    public string ContentType = "application/json; charset=utf-8";
}

static class HttpLite
{
    static readonly string[] Verbs = { "GET ", "POST ", "PUT ", "DELETE ", "OPTIONS ", "HEAD " };

    public static bool LooksLikeHttp(byte[] b, int n)
    {
        var head = Encoding.ASCII.GetString(b, 0, Math.Min(n, 8));
        foreach (var v in Verbs) if (head.StartsWith(v, StringComparison.Ordinal)) return true;
        return false;
    }

    public static void Listen(string label, int port, Func<HttpReq, HttpRes> handler, bool keepAlive)
    {
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        Log.Info($"Bound {label,-12} on 127.0.0.1:{port}");
        new Thread(() =>
        {
            while (true)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    using (c) { c.ReceiveTimeout = 60000; ServeStream(c.GetStream(), null, 0, label, handler, keepAlive); }
                });
            }
        }) { IsBackground = true, Name = label }.Start();
    }

    static int HeaderEnd(List<byte> b)
    {
        for (int i = 0; i + 3 < b.Count; i++)
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
        return -1;
    }

    static string Reason(int s) => s switch { 200 => "OK", 404 => "Not Found", 500 => "Internal Server Error", _ => "Error" };

    public static void ServeStream(NetworkStream ns, byte[] first, int firstLen, string label, Func<HttpReq, HttpRes> h, bool keepAlive)
    {
        var buf = new List<byte>();
        if (first != null) buf.AddRange(new ArraySegment<byte>(first, 0, firstLen));
        var tmp = new byte[65536];
        while (true)
        {
            int he;
            while ((he = HeaderEnd(buf)) < 0)
            {
                int n;
                try { n = ns.Read(tmp, 0, tmp.Length); } catch { return; }
                if (n <= 0) return;
                buf.AddRange(new ArraySegment<byte>(tmp, 0, n));
                if (buf.Count > (1 << 20)) return;
            }
            var head = Encoding.Latin1.GetString(buf.GetRange(0, he).ToArray());
            buf.RemoveRange(0, he + 4);
            var lines = head.Split("\r\n");
            var rl = lines[0].Split(' ');
            if (rl.Length < 2) return;
            var req = new HttpReq { Method = rl[0], Target = rl[1] };
            foreach (var ln in lines.Skip(1))
            {
                int i = ln.IndexOf(':');
                if (i > 0) req.Headers[ln[..i].Trim()] = ln[(i + 1)..].Trim();
            }
            int need = req.Headers.TryGetValue("Content-Length", out var cls) && int.TryParse(cls, out var cn) ? cn : 0;
            while (buf.Count < need)
            {
                int n;
                try { n = ns.Read(tmp, 0, tmp.Length); } catch { return; }
                if (n <= 0) break;
                buf.AddRange(new ArraySegment<byte>(tmp, 0, n));
            }
            need = Math.Min(need, buf.Count);
            req.Body = buf.GetRange(0, need).ToArray();
            buf.RemoveRange(0, need);

            HttpRes res;
            try { res = h(req); }
            catch (Exception e)
            {
                Log.Error($"{label} handler error: {e}");
                res = new HttpRes { Status = 500, Body = Encoding.UTF8.GetBytes("{\"error\":\"local server exception\"}") };
            }
            Log.Info($"{label} 127.0.0.1 - \"{req.Method} {req.Target} HTTP/1.1\" {res.Status} -");
            var sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {res.Status} {Reason(res.Status)}\r\n");
            sb.Append($"Content-Type: {res.ContentType}\r\n");
            sb.Append($"Content-Length: {res.Body.Length}\r\n");
            sb.Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");
            foreach (var kv in res.Headers) sb.Append($"{kv.Key}: {kv.Value}\r\n");
            sb.Append("\r\n");
            try
            {
                var hb = Encoding.ASCII.GetBytes(sb.ToString());
                ns.Write(hb, 0, hb.Length);
                if (res.Body.Length > 0) ns.Write(res.Body, 0, res.Body.Length);
            }
            catch { return; }
            if (!keepAlive) return;
        }
    }
}

// ------------------------------------------------------------------ small services: redirector, QoS, EASW

static class Services
{
    static HttpRes Xml(string body, int status = 200, string ct = "application/xml; charset=utf-8", Dictionary<string, string> h = null) =>
        new() { Status = status, Body = Encoding.UTF8.GetBytes(body), ContentType = ct, Headers = h ?? new() };

    public static HttpRes Redirector(HttpReq r)
    {
        Log.Info($"Redirector request {r.Method} {r.Target} body={Encoding.UTF8.GetString(r.Body, 0, Math.Min(r.Body.Length, 800))}");
        const long ipInt = 2130706433; // 127.0.0.1
        return Xml(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<serverinstanceinfo>\n    <address member=\"0\">\n        <valu>\n" +
            "            <hostname>127.0.0.1</hostname>\n" +
            $"            <ip>{ipInt}</ip>\n            <port>{Cfg.BlazePort}</port>\n" +
            "        </valu>\n    </address>\n    <secure>0</secure>\n</serverinstanceinfo>", 200, "application/xml");
    }

    public static HttpRes Qos(HttpReq r)
    {
        var target = r.Target;
        int qi = target.IndexOf('?');
        var path = (qi >= 0 ? target[..qi] : target).ToLowerInvariant();
        var q = Fut.ParseQuery(qi >= 0 ? target[(qi + 1)..] : "");
        const long ip = 16777343; // 127.0.0.1 little-endian
        var nostore = new Dictionary<string, string> { ["Cache-Control"] = "no-store" };
        if (path == "/qos/qos")
        {
            var qtyp = q.TryGetValue("qtyp", out var t) ? t : "1";
            return qtyp == "2"
                ? Xml($"<?xml version=\"1.0\" encoding=\"utf-8\"?><qos><numprobes>10</numprobes><qosport>{Cfg.QosPort}</qosport><probesize>1200</probesize><qosip>{ip}</qosip><requestid>1234</requestid><reqsecret>5678</reqsecret></qos>", 200, "application/xml; charset=utf-8", nostore)
                : Xml($"<?xml version=\"1.0\" encoding=\"utf-8\"?><qos><numprobes>0</numprobes><qosport>{Cfg.QosPort}</qosport><probesize>0</probesize><qosip>{ip}</qosip><requestid>1</requestid><reqsecret>0</reqsecret></qos>", 200, "application/xml; charset=utf-8", nostore);
        }
        if (path == "/qos/firewall")
            return Xml($"<?xml version=\"1.0\" encoding=\"utf-8\"?><firewall><ips><ips>{ip}</ips><ips>{ip}</ips></ips><numinterfaces>2</numinterfaces><ports><ports>{Cfg.QosPort}</ports><ports>{Cfg.QosPort}</ports></ports><requestid>1234</requestid><reqsecret>5678</reqsecret></firewall>", 200, "application/xml; charset=utf-8", nostore);
        if (path == "/qos/firetype")
            return Xml("<?xml version=\"1.0\" encoding=\"utf-8\"?><firetype><firetype>2</firetype></firetype>", 200, "application/xml; charset=utf-8", nostore);
        return Xml("not found", 404, "text/plain", nostore);
    }

    const string RosterXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<squad>\n  <squadInfoSet>\n" +
        "    <squadInfo platform=\"pc64\">\n      <dbSchemaCRC>596426fe3525c25b90593b800b9ce5c3c61ccab5</dbSchemaCRC>\n" +
        "      <dbMajor>1</dbMajor><dbMinor>0</dbMinor>\n      <dbMajorCRC>0</dbMajorCRC><dbMinorCRC>0</dbMinorCRC>\n" +
        "      <dbMajorLoc></dbMajorLoc><dbMinorLoc></dbMinorLoc>\n      <dbFUTVer>1</dbFUTVer><dbFUTCRC>0</dbFUTCRC><dbFUTLoc></dbFUTLoc>\n    </squadInfo>\n" +
        "    <squadInfo platform=\"pc64\">\n      <dbSchemaCRC>00e20ae6ad44d892ad15ea7f060ce6d97ca9e730</dbSchemaCRC>\n" +
        "      <dbMajor>1</dbMajor><dbMinor>0</dbMinor>\n      <dbMajorCRC>0</dbMajorCRC><dbMinorCRC>0</dbMinorCRC>\n" +
        "      <dbMajorLoc></dbMajorLoc><dbMinorLoc></dbMinorLoc>\n      <dbFUTVer>1</dbFUTVer><dbFUTCRC>0</dbFUTCRC><dbFUTLoc></dbFUTLoc>\n    </squadInfo>\n" +
        "  </squadInfoSet>\n</squad>";

    public static HttpRes Easw(HttpReq r)
    {
        int qi = r.Target.IndexOf('?');
        var path = (qi >= 0 ? r.Target[..qi] : r.Target).ToLowerInvariant();
        if (r.Method == "POST")
        {
            if (path == "/extension")
                return Xml("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<result/>\n", 200, "text/xml",
                    new() { ["EASW-Session"] = "fut15-local-easw-session", ["EASW-Token"] = "fut15-local-easw-token" });
            return Xml("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<result/>\n", 200, "text/xml");
        }
        if (path == "/roster") return Xml(RosterXml, 200, "text/xml");
        return Xml("not found", 404, "text/plain");
    }
}

// ------------------------------------------------------------------ players / consumables / packs

sealed class PackCfg
{
    public int Id; public bool Enabled = true; public string Category, Name, Bio;
    public int Price, Points, Size, PlayerSlots, ClubItemSlots, GoldW, SilverW, BronzeW, RareSlots, PlayerRareSlots;
    public double SpecialChance;
    public double ManagerChance;   // chance (0-1) that one common player slot of a normal pack becomes a manager card
    public string Mode;   // null = normal pack; "stars" | "totw" | "toty" | "icon" | "manager" = FUT REBORN special packs
}

sealed class ManagerDef
{
    public long Rid, Rating = 80, Nation, League; public string Name = "";
}

sealed class Consumable
{
    public long Rid; public int CardAsset, CardSub, Rating, RareFlag, Bronze, Silver, Gold, Amount; public string Tier, Kind, Name;
}

static class Catalog
{
    public static readonly Dictionary<long, JsonObject> Players = new();
    public static readonly Dictionary<int, PackCfg> Packs = new();
    public static readonly List<ManagerDef> Managers = new();   // manager cards that can drop from packs (managers.json)
    static readonly Dictionary<int, double> SpecialWeights = new() { [3] = 55, [4] = 4, [5] = 2, [6] = 3, [7] = 4, [8] = 12, [9] = 5, [11] = 8 };

    static Consumable C(long rid, int ca, int cs, string tier, int rating, int rare, string kind, int b, int s, int g, int amount, string name) =>
        new() { Rid = rid, CardAsset = ca, CardSub = cs, Tier = tier, Rating = rating, RareFlag = rare, Kind = kind, Bronze = b, Silver = s, Gold = g, Amount = amount, Name = name };

    public static readonly List<Consumable> Consumables = new()
    {
        C(5001001, 7, 201, "bronze", 50, 0, "contract_player", 8, 2, 1, 0, "Bronze Player Contract"),
        C(5001004, 7, 201, "bronze", 60, 1, "contract_player", 15, 6, 3, 0, "Rare Bronze Player Contract"),
        C(5001002, 7, 201, "silver", 65, 0, "contract_player", 10, 10, 8, 0, "Silver Player Contract"),
        C(5001005, 7, 201, "silver", 70, 1, "contract_player", 20, 24, 18, 0, "Rare Silver Player Contract"),
        C(5001003, 7, 201, "gold", 80, 0, "contract_player", 15, 11, 13, 0, "Gold Player Contract"),
        C(5001006, 7, 201, "gold", 90, 1, "contract_player", 28, 24, 28, 0, "Rare Gold Player Contract"),
        C(5001007, 8, 202, "bronze", 50, 0, "contract_manager", 8, 2, 1, 0, "Bronze Manager Contract"),
        C(5001010, 8, 202, "bronze", 60, 1, "contract_manager", 15, 6, 3, 0, "Rare Bronze Manager Contract"),
        C(5001008, 8, 202, "silver", 65, 0, "contract_manager", 8, 10, 8, 0, "Silver Manager Contract"),
        C(5001011, 8, 202, "silver", 70, 1, "contract_manager", 18, 24, 18, 0, "Rare Silver Manager Contract"),
        C(5001009, 8, 202, "gold", 80, 0, "contract_manager", 11, 11, 13, 0, "Gold Manager Contract"),
        C(5001012, 8, 202, "gold", 90, 1, "contract_manager", 24, 24, 28, 0, "Rare Gold Manager Contract"),
        C(5002001, 10, 219, "bronze", 55, 0, "fitness_player", 0, 0, 0, 20, "Bronze Player Fitness"),
        C(5002004, 10, 220, "bronze", 55, 1, "fitness_team", 0, 0, 0, 10, "Bronze Squad Fitness"),
        C(5002002, 10, 219, "silver", 70, 0, "fitness_player", 0, 0, 0, 40, "Silver Player Fitness"),
        C(5002005, 10, 220, "silver", 70, 1, "fitness_team", 0, 0, 0, 20, "Silver Squad Fitness"),
        C(5002003, 10, 219, "gold", 80, 0, "fitness_player", 0, 0, 0, 60, "Gold Player Fitness"),
        C(5002006, 10, 220, "gold", 80, 1, "fitness_team", 0, 0, 0, 30, "Gold Squad Fitness"),
        C(5002007, 9, 211, "bronze", 55, 0, "healing", 0, 0, 0, 1, "Bronze Healing"),
        C(5002028, 9, 218, "bronze", 60, 1, "healing", 0, 0, 0, 1, "Rare Bronze All Healing"),
        C(5002008, 9, 211, "silver", 70, 0, "healing", 0, 0, 0, 2, "Silver Healing"),
        C(5002029, 9, 218, "silver", 74, 1, "healing", 0, 0, 0, 2, "Rare Silver All Healing"),
        C(5002009, 9, 211, "gold", 80, 0, "healing", 0, 0, 0, 5, "Gold Healing"),
        C(5002030, 9, 218, "gold", 85, 1, "healing", 0, 0, 0, 4, "Rare Gold All Healing"),
        C(5003022, 1, 61, "bronze", 55, 0, "training_player", 0, 0, 0, 5, "Bronze Player Training"),
        C(5003040, 1, 67, "bronze", 64, 1, "training_player", 0, 0, 0, 3, "Rare Bronze All Training"),
        C(5003023, 1, 61, "silver", 65, 0, "training_player", 0, 0, 0, 10, "Silver Player Training"),
        C(5003041, 1, 67, "silver", 74, 1, "training_player", 0, 0, 0, 6, "Rare Silver All Training"),
        C(5003024, 1, 61, "gold", 85, 0, "training_player", 0, 0, 0, 15, "Gold Player Training"),
        C(5003042, 1, 67, "gold", 95, 1, "training_player", 0, 0, 0, 10, "Rare Gold All Training"),
    };

    static PackCfg P(int id, string cat, string name, int tierCount, int rare, int price, int pts, int g, int s, int b, double sc) => new()
    {
        Id = id, Category = cat, Name = name, Price = price, Points = pts, Size = 12, PlayerSlots = 3, ClubItemSlots = 2,
        GoldW = g, SilverW = s, BronzeW = b, RareSlots = rare, PlayerRareSlots = 1, SpecialChance = sc,
        Bio = $"A mix of 12 items, including players, club items and consumables, at least 10 {char.ToUpper(cat[0]) + cat[1..]} with {rare} rare.",
    };

    public static void Load()
    {
        // FUT REBORN built-in packs: exactly one pack per store tab, players only (12-item packs with consumables broke the pack reveal)
        Packs[1] = new PackCfg { Id = 1, Category = "bronze", Name = "FUT REBORN Bronze Pack", Bio = "FUT REBORN Bronze Pack: 3 bronze players.", Price = 400, Points = 0, Size = 3, PlayerSlots = 3, ClubItemSlots = 0, GoldW = 0, SilverW = 5, BronzeW = 95, RareSlots = 0, PlayerRareSlots = 0, SpecialChance = 0, ManagerChance = 0.02 };
        Packs[2] = new PackCfg { Id = 2, Category = "silver", Name = "FUT REBORN Silver Pack", Bio = "FUT REBORN Silver Pack: 4 players with at least 1 rare.", Price = 2500, Points = 50, Size = 4, PlayerSlots = 4, ClubItemSlots = 0, GoldW = 5, SilverW = 95, BronzeW = 0, RareSlots = 1, PlayerRareSlots = 1, SpecialChance = 0, ManagerChance = 0.03 };
        Packs[3] = new PackCfg { Id = 3, Category = "gold", Name = "FUT REBORN Gold Pack", Bio = "FUT REBORN Gold Pack: 5 players with at least 2 rares.", Price = 7500, Points = 100, Size = 5, PlayerSlots = 5, ClubItemSlots = 0, GoldW = 100, SilverW = 0, BronzeW = 0, RareSlots = 2, PlayerRareSlots = 2, SpecialChance = 0.05, ManagerChance = 0.05 };
        Packs[4] = new PackCfg { Id = 4, Category = "bronze", Name = "FUT REBORN Jumbo Bronze Pack", Bio = "FUT REBORN Jumbo Bronze Pack: 5 bronze players.", Price = 750, Points = 0, Size = 5, PlayerSlots = 5, ClubItemSlots = 0, GoldW = 0, SilverW = 5, BronzeW = 95, RareSlots = 0, PlayerRareSlots = 0, SpecialChance = 0, ManagerChance = 0.03 };
        Packs[5] = new PackCfg { Id = 5, Category = "silver", Name = "FUT REBORN Jumbo Silver Pack", Bio = "FUT REBORN Jumbo Silver Pack: 5 players with at least 2 rares.", Price = 5000, Points = 80, Size = 5, PlayerSlots = 5, ClubItemSlots = 0, GoldW = 10, SilverW = 90, BronzeW = 0, RareSlots = 2, PlayerRareSlots = 2, SpecialChance = 0, ManagerChance = 0.05 };
        Packs[6] = new PackCfg { Id = 6, Category = "gold", Name = "FUT REBORN Rare Gold Pack", Bio = "FUT REBORN Rare Gold Pack: 4 players, 3 rares.", Price = 20000, Points = 200, Size = 4, PlayerSlots = 4, ClubItemSlots = 0, GoldW = 100, SilverW = 0, BronzeW = 0, RareSlots = 3, PlayerRareSlots = 3, SpecialChance = 0.02, ManagerChance = 0.08 };
        Packs[7] = new PackCfg { Id = 7, Category = "gold", Name = "FUT REBORN Stars Pack", Bio = "FUT REBORN Stars Pack: 5 famous players. Tiny chance of a superstar.", Price = 15000, Points = 150, Size = 5, PlayerSlots = 5, GoldW = 100, RareSlots = 5, PlayerRareSlots = 5, Mode = "stars" };
        Packs[8] = new PackCfg { Id = 8, Category = "gold", Name = "FUT REBORN TOTW Pack", Bio = "FUT REBORN TOTW Pack: 3 Team of the Week in-form players.", Price = 30000, Points = 300, Size = 3, PlayerSlots = 3, GoldW = 100, RareSlots = 3, PlayerRareSlots = 3, Mode = "totw" };
        Packs[9] = new PackCfg { Id = 9, Category = "gold", Name = "FUT REBORN TOTY Pack", Bio = "FUT REBORN TOTY Pack: 1 Team of the Year player.", Price = 120000, Points = 600, Size = 1, PlayerSlots = 1, GoldW = 100, RareSlots = 1, PlayerRareSlots = 1, Mode = "toty" };
        var pp = Cfg.Find("pack_settings.json");
        Log.Info("pack_settings.json: " + pp + (File.Exists(pp) ? "" : "  (NOT FOUND - using built-in FUT REBORN packs)"));
        if (File.Exists(pp))
        {
            try
            {
                // AllowTrailingCommas also tolerates the stray comma that breaks pack_settings.json in the Python build.
                var root = JsonNode.Parse(File.ReadAllText(pp), null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
                if (root?["packs"] is JsonObject packs)
                {
                    Packs.Clear();
                    foreach (var kv in packs)
                    {
                        if (kv.Value is not JsonObject o || !int.TryParse(kv.Key, out var id)) continue;
                        var cat = U.S(o, "category", "gold");
                        var pc = new PackCfg
                        {
                            Id = id, Enabled = U.B(o, "enabled", true), Category = cat, Name = U.S(o, "name", $"FUT Pack {id}"),
                            Bio = U.S(o, "bio", "A FIFA 15 Ultimate Team pack."), Price = (int)U.L(o, "price", 7500), Points = (int)U.L(o, "fifa_points", 0),
                            Size = (int)Math.Clamp(U.L(o, "size", 12), 1, 30), PlayerSlots = (int)U.L(o, "player_slots", 3), ClubItemSlots = (int)U.L(o, "club_item_slots", 0),
                            GoldW = (int)U.L(o, "gold_weight", 100), SilverW = (int)U.L(o, "silver_weight", 0), BronzeW = (int)U.L(o, "bronze_weight", 0),
                            RareSlots = (int)U.L(o, "rare_slots", 3), PlayerRareSlots = (int)U.L(o, "player_rare_slots", 1), SpecialChance = U.D(o, "special_chance", 0.1),
                            ManagerChance = U.D(o, "manager_chance", 0), Mode = U.S(o, "mode", ""),
                        };
                        Packs[id] = pc;
                    }
                }
            }
            catch (Exception e) { Log.Warn("pack_settings.json ignored, using built-in packs: " + e.Message); }
        }

        // managers.json (next to the exe): {"managers":[{"resource_id":5000001,"name":"...","rating":85,"nation":14,"league":13}, ...]}
        // The game draws the manager's face/name from its own files using resource_id, so these must be REAL FIFA 16 manager ids.
        Managers.Clear();
        var mgrPath = Cfg.Find("managers.json");
        if (File.Exists(mgrPath))
        {
            try
            {
                var mgrRoot = JsonNode.Parse(File.ReadAllText(mgrPath), null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var mgrArr = (mgrRoot as JsonObject)?["managers"] as JsonArray ?? mgrRoot as JsonArray;
                if (mgrArr != null)
                    foreach (var mgrNode in mgrArr)
                        if (mgrNode is JsonObject mgrObj && U.L(mgrObj, "resource_id", 0) > 0)
                            Managers.Add(new ManagerDef { Rid = U.L(mgrObj, "resource_id", 0), Name = U.S(mgrObj, "name", ""), Rating = U.L(mgrObj, "rating", 80), Nation = U.L(mgrObj, "nation", 0), League = U.L(mgrObj, "league", 0) });
            }
            catch (Exception e) { Log.Warn("managers.json ignored: " + e.Message); }
        }
        if (Managers.Count == 0)
        {
            Managers.Add(new ManagerDef { Rid = Cfg.ManagerRid, Name = "Default manager", Rating = Cfg.ManagerRating, Nation = Cfg.ManagerNation, League = Cfg.ManagerLeague });
            Log.Warn("managers.json not found: manager packs give only the default manager card (resource id " + Cfg.ManagerRid + ", a guess). Create managers.json with real ids.");
        }
        else Log.Info($"managers.json: {Managers.Count} managers loaded");

        PlayersBuilder.LoadTable();   // players compiled into PlayersData.cs (no json needed)
        if (Players.Count == 0)
        {
            // old fallback: a players.json from the FIFA 15 project, only used when PlayersData.cs is still the empty stub
            var pj = Cfg.Find("players.json");
            if (File.Exists(pj))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(pj)) is JsonObject all)
                        foreach (var kv in all)
                            if (kv.Value is JsonObject meta && long.TryParse(kv.Key, out var rid)) Players[rid] = meta;
                    Log.Warn("Using the old players.json fallback. Run:  LocalFUT16.exe --make-players <male_players.csv>  to build PlayersData.cs with FIFA 16 players.");
                }
                catch (Exception e) { Log.Error("players.json could not be read: " + e.Message); }
            }
            else Log.Error("NO PLAYER DATA. PlayersData.cs is empty. Run:  dotnet run -- --make-players <path to male_players.csv>  in the project folder, then rebuild. Packs and starter squad will be empty until then.");
        }
        Log.Info($"Loaded {Players.Count} player definitions");
    }

    static string TierFor(JsonObject m)
    {
        var t = U.S(m, "tier", "").ToLowerInvariant();
        if (t is "gold" or "silver" or "bronze") return t;
        var r = U.L(m, "rating");
        return r >= 75 ? "gold" : r >= 65 ? "silver" : "bronze";
    }

    // ---- FUT REBORN starter squad: 15 cards, mostly bronze plus a few common (non-rare) golds ----
    // Slot order = f442: GK RB CB CB LB RM CM CM LM ST ST, then 4 subs.
    // Put your own list in config.json  "starter_players": [rid, rid, ...]  to override.
    public static List<long> PickStarters()
    {
        if (Cfg.StarterOverride.Count >= 11)
        {
            bool okOverride = Cfg.StarterOverride.All(r => Players.TryGetValue(r, out var pm) && U.L(pm, "rating") <= 72);
            if (okOverride) return Cfg.StarterOverride;
            Log.Warn("config.json starter_players ignored: some ids are not in the player table or are rated above 72 (bronze/silver only)");
        }
        var rnd = new Random(1616);
        string[][] slotPos = {
            new[]{"GK"}, new[]{"RB","RWB"}, new[]{"CB"}, new[]{"CB"}, new[]{"LB","LWB"},
            new[]{"RM","RW"}, new[]{"CM","CDM","CAM"}, new[]{"CM","CDM","CAM"}, new[]{"LM","LW"}, new[]{"ST","CF"}, new[]{"ST","CF"},
            new[]{"GK"}, new[]{"CB","LB","RB"}, new[]{"CM","CDM","CAM"}, new[]{"ST","CF","RM","LM"} };
        // FUT REBORN starter club: 2 common golds, 4 silvers, 5 bronze in the XI; bench = 3 bronze + 1 silver
        string[] tiers = { "bronze", "silver", "bronze", "silver", "bronze",
                           "silver", "silver", "bronze", "silver", "silver", "bronze",
                           "bronze", "bronze", "bronze", "silver" };
        var used = new HashSet<long>();
        var res = new List<long>();
        for (int i = 0; i < slotPos.Length; i++)
        {
            string want = tiers[i];
            bool Tier(KeyValuePair<long, JsonObject> kv)
            {
                long r = U.L(kv.Value, "rating");
                if (U.L(kv.Value, "rareflag", 0) != 0) return false;                 // common cards only
                return want == "gold" ? r >= 75 && r <= 79 : want == "silver" ? r >= 65 && r <= 72 : r >= 45 && r <= 62;
            }
            var pool = Players.Where(kv => !used.Contains(kv.Key) && Tier(kv)).ToList();
            var byPos = pool.Where(kv => slotPos[i].Contains(PosName(U.S(kv.Value, "preferredPosition", "")))).ToList();
            var pick = byPos.Count > 0 ? byPos : pool;
            if (pick.Count == 0) continue;
            long rid = pick[rnd.Next(pick.Count)].Key;
            used.Add(rid); res.Add(rid);
        }
        if (res.Count < 11)
        {
            Log.Error("STARTER SQUAD: no usable player data (PlayersData.cs is empty and there is no players.json). Packs and the starter squad will not work until you build PlayersData.cs (see --make-players) or copy a players.json next to the exe.");
            return new List<long>();
        }
        return res;
    }

    // preferredPosition can be a name ("CB") or EA's numeric position id ("5")
    static string PosName(string p)
    {
        p = (p ?? "").Trim().ToUpperInvariant();
        if (int.TryParse(p, out var n))
        {
            string[] names = { "GK","SW","RWB","RB","RCB","CB","LCB","LB","LWB","RDM","CDM","LDM","RM","RCM","CM","LCM","LM","RAM","CAM","LAM","RF","CF","LF","RW","RS","ST","LS" };
            return n >= 0 && n < names.Length ? names[n] : "";
        }
        return p;
    }

    public static JsonObject MakePlayer(long rid, string pile, long id, long ts)
    {
        Players.TryGetValue(rid, out var m);
        if (m == null) Log.Warn($"MakePlayer: resource {rid} is not in the player table (fallback 80-rated card used)");
        m ??= new JsonObject { ["rating"] = 80L, ["rareflag"] = 1L };
        long rating = U.L(m, "rating");
        long asset = U.L(m, "assetId", rid);
        var attrs = new JsonArray();
        if (m["face"] is JsonArray face)
        {
            int i = 0;
            foreach (var f in face) attrs.Add(new JsonObject { ["index"] = (long)i++, ["value"] = U.LN(f) });
        }
        long team = U.L(m, "teamid");
        return new JsonObject
        {
            ["id"] = id, ["itemId"] = id, ["assetId"] = asset, ["definitionId"] = asset, ["resourceId"] = rid,
            ["resourceGameYear"] = 2016L, ["attributeList"] = attrs, ["cardsubtypeid"] = 1L, ["contract"] = 7L, ["contracts"] = 7L,
            ["discardValue"] = rating >= 75 ? 700L : rating >= 65 ? 300L : 100L,
            ["fitness"] = 99L, ["formation"] = "f442", ["injuryGames"] = 0L, ["injuryType"] = "none", ["itemState"] = "free",
            ["itemType"] = "player", ["lastSalePrice"] = 0L, ["leagueId"] = U.L(m, "leagueId"), ["loans"] = 0L, ["loyaltyBonus"] = 1L,
            ["marketDataMinPrice"] = 150L, ["marketDataMaxPrice"] = 15000000L, ["morale"] = 50L, ["nation"] = U.L(m, "nation"), ["owners"] = 1L,
            ["preferredPosition"] = U.S(m, "preferredPosition"), ["rareflag"] = U.L(m, "rareflag", 1), ["rating"] = rating, ["suspension"] = 0L,
            ["teamid"] = team, ["teamId"] = team, ["timestamp"] = ts, ["training"] = 0L, ["trainingId"] = 0L, ["trainingResourceId"] = 0L,
            ["untradeable"] = false, ["tradeable"] = true, ["playStyle"] = 0L, ["posMods"] = new JsonArray(), ["statsList"] = new JsonArray(),
            ["lifetimeStats"] = new JsonArray(), ["assists"] = 0L, ["lifetimeAssists"] = 0L, ["pile"] = pile,
        };
    }

    // Manager card (itemType "manager"). Field names follow the player card; the exact manager schema of FIFA 16 is unverified.
    public static JsonObject MakeManager(long id, long ts, ManagerDef def = null, string pile = "club")
    {
        long rid = def?.Rid ?? Cfg.ManagerRid;
        return new JsonObject
        {
            ["id"] = id, ["itemId"] = id, ["assetId"] = rid, ["definitionId"] = rid, ["resourceId"] = rid, ["resourceGameYear"] = 2016L,
            ["itemType"] = "manager", ["cardsubtypeid"] = 1L, ["rating"] = def?.Rating ?? Cfg.ManagerRating, ["rareflag"] = 1L, ["contract"] = 7L, ["contracts"] = 7L,
            ["leagueId"] = def?.League ?? Cfg.ManagerLeague, ["nation"] = def?.Nation ?? Cfg.ManagerNation, ["teamid"] = 0L, ["teamId"] = 0L,
            ["discardValue"] = 100L, ["itemState"] = "free", ["lastSalePrice"] = 0L, ["marketDataMinPrice"] = 150L, ["marketDataMaxPrice"] = 15000000L,
            ["owners"] = 1L, ["untradeable"] = false, ["tradeable"] = true, ["timestamp"] = ts, ["loans"] = 0L, ["training"] = 0L,
            ["attributeList"] = new JsonArray(), ["statsList"] = new JsonArray(), ["lifetimeStats"] = new JsonArray(), ["pile"] = pile,
        };
    }

    public static JsonObject MakeConsumable(Consumable c, string pile, long id, long ts)
    {
        var o = new JsonObject
        {
            ["id"] = id, ["itemId"] = id, ["timestamp"] = ts, ["resourceId"] = c.Rid, ["cardassetid"] = (long)c.CardAsset, ["cardsubtypeid"] = (long)c.CardSub,
            ["rating"] = (long)c.Rating, ["rareFlag"] = (long)c.RareFlag, ["rareflag"] = (long)c.RareFlag, ["bronze"] = (long)c.Bronze, ["silver"] = (long)c.Silver,
            ["gold"] = (long)c.Gold, ["amount"] = (long)c.Amount, ["itemType"] = "development", ["resourceGameYear"] = 2016L, ["discardValue"] = 0L,
            ["lastSalePrice"] = 0L, ["owners"] = 1L, ["untradeable"] = false, ["tradeable"] = true, ["itemState"] = "free", ["pile"] = pile,
            ["consumableKind"] = c.Kind,
        };
        switch (c.Kind)
        {
            case "contract_player": o["consumablesContractPlayer"] = 1L; break;
            case "contract_manager": o["consumablesContractManager"] = 1L; break;
            case "fitness_team": o["consumablesFitnessTeam"] = (long)Math.Max(1, c.Amount); break;
            case "fitness_player": o["consumablesFitnessPlayer"] = (long)Math.Max(1, c.Amount); break;
            case "healing": o["consumablesHealing"] = (long)Math.Max(1, c.Amount); break;
            case "training_player": o["consumablesTraining"] = 1L; o["consumablesTrainingPlayer"] = (long)Math.Max(1, c.Amount); break;
        }
        return o;
    }

    static T Weighted<T>(IList<(T v, double w)> opts, T fallback)
    {
        double total = 0; foreach (var (_, w) in opts) if (w > 0) total += w;
        if (total <= 0) return fallback;
        double r = U.Rng.NextDouble() * total, acc = 0;
        foreach (var (v, w) in opts) { if (w <= 0) continue; acc += w; if (r <= acc) return v; }
        return opts[^1].v;
    }

    static readonly string[] StarNames = { "Iniesta", "Modri", "Xavier Hern", "Ronaldinho", "Ibrahimovi", "Pirlo", "Lampard", "Gerrard", "Totti", "Casillas", "Buffon", "Robben", "Drogba", "Lahm", "Neuer", "Hazard", "Sergio Ramos", "Pogba", "Schweinsteiger", "Aguero", "Falcao" };
    static readonly string[] SuperstarNames = { "Lionel Andr", "Cristiano Ronaldo", "Neymar" };

    static List<JsonObject> BuildSpecialPack(PackCfg pc, State st)
    {
        bool NameHas(JsonObject m, string[] names) { var n = U.S(m, "name", ""); foreach (var x in names) if (n.Contains(x, StringComparison.OrdinalIgnoreCase)) return true; return false; }
        long RF(JsonObject m) => U.L(m, "rareflag");
        long RT(JsonObject m) => U.L(m, "rating");
        List<long> Pool(Func<JsonObject, bool> f) => Players.Where(kv => f(kv.Value)).Select(kv => kv.Key).ToList();

        List<long> main, superstars = null;
        double superChance = 0;
        switch (pc.Mode)
        {
            case "totw": main = Pool(m => RF(m) == 3 && RT(m) >= 70); break;
            case "toty": main = Pool(m => RF(m) == 5); break;
            case "icon": main = Pool(m => RF(m) is 4 or 5 or 6 or 9 or 11 && RT(m) >= 90); break;
            default:
                main = Pool(m => NameHas(m, StarNames) && RT(m) >= 80);
                superstars = Pool(m => NameHas(m, SuperstarNames) && RT(m) >= 90 && (RF(m) == 1 || RF(m) == 3));
                superChance = 0.04;   // one superstar card in about 1 of 25 packs
                break;
        }
        if (main.Count == 0) main = Pool(m => RT(m) >= 80 && RF(m) >= 1);
        if (main.Count == 0) throw new InvalidOperationException("special pack pool is empty (is players.json present?)");

        var picks = new List<long>();
        var used = new HashSet<long>();
        for (int i = 0; i < Math.Max(1, pc.PlayerSlots); i++)
        {
            List<long> pool = main;
            if (i == 0 && superstars != null && superstars.Count > 0 && U.Rng.NextDouble() < superChance) pool = superstars;
            for (int t = 0; t < 30; t++) { long r = pool[U.Rng.Next(pool.Count)]; if (used.Add(r)) { picks.Add(r); break; } }
        }
        picks = picks.OrderByDescending(r => RT(Players[r])).ToList();
        int total = picks.Count;
        long baseId = st.ReserveIds(total);
        long ts = U.NowS();
        var items = new List<JsonObject>();
        for (int i = 0; i < total; i++) items.Add(MakePlayer(picks[i], "unassigned", baseId + total - 1 - i, ts + total - 1 - i));
        return items;
    }

    static List<JsonObject> BuildManagerPack(PackCfg pc, State st)
    {
        int count = Math.Max(1, pc.PlayerSlots);
        var picks = new List<ManagerDef>();
        var bag = new List<ManagerDef>(Managers); U.Shuffle(bag);
        for (int i = 0; i < count; i++) picks.Add(bag.Count > 0 && i < bag.Count ? bag[i] : Managers[U.Rng.Next(Managers.Count)]);
        picks = picks.OrderByDescending(m => m.Rating).ToList();
        long baseId = st.ReserveIds(picks.Count);
        long ts = U.NowS();
        var items = new List<JsonObject>();
        for (int i = 0; i < picks.Count; i++) items.Add(MakeManager(baseId + picks.Count - 1 - i, ts + picks.Count - 1 - i, picks[i], "unassigned"));
        return items;
    }

    // Returns the items in reveal order (not yet stored). Consumables fill the non-player slots
    // (club items / cosmetics are stage 2).
    public static List<JsonObject> BuildPack(PackCfg pc, State st)
    {
        if (pc.Mode == "manager") return BuildManagerPack(pc, st);
        if (!string.IsNullOrEmpty(pc.Mode)) return BuildSpecialPack(pc, st);
        int size = Math.Clamp(pc.Size, 1, 30);
        int playerSlots = Math.Clamp(pc.PlayerSlots, 0, size);
        int miscSlots = size - playerSlots;
        int rareSlots = Math.Clamp(pc.RareSlots, 0, size);
        int playerRare = Math.Clamp(pc.PlayerRareSlots, 0, Math.Min(playerSlots, rareSlots));
        int miscRare = Math.Clamp(rareSlots - playerRare, 0, miscSlots);
        var tierW = new List<(string, double)> { ("gold", pc.GoldW), ("silver", pc.SilverW), ("bronze", pc.BronzeW) };
        // TIER FLOOR (higher price = better): gold packs only gold, silver packs never bronze, bronze packs never gold
        string pcat = (pc.Category ?? "").ToLowerInvariant();
        if (pcat == "gold") tierW = new() { ("gold", 100), ("silver", 0), ("bronze", 0) };
        else if (pcat == "silver") tierW = new() { ("gold", Math.Min(pc.GoldW, 5)), ("silver", 100 - Math.Min(pc.GoldW, 5)), ("bronze", 0) };
        else if (pcat == "bronze") tierW = new() { ("gold", 0), ("silver", Math.Min(pc.SilverW, 5)), ("bronze", 100 - Math.Min(pc.SilverW, 5)) };
        if (tierW.Sum(t => t.Item2) <= 0) tierW = new() { ("gold", 100), ("silver", 0), ("bronze", 0) };
        string ChooseTier() => Weighted(tierW, "gold");

        var pools = new Dictionary<string, Dictionary<string, List<long>>>();
        foreach (var t in new[] { "gold", "silver", "bronze" }) pools[t] = new() { ["common"] = new(), ["rare"] = new(), ["special"] = new() };
        foreach (var kv in Players)
        {
            long rf = U.L(kv.Value, "rareflag");
            bool special = U.B(kv.Value, "special") || rf > 1;
            pools[TierFor(kv.Value)][special ? "special" : rf == 1 ? "rare" : "common"].Add(kv.Key);
        }
        foreach (var t in pools.Values) foreach (var l in t.Values) U.Shuffle(l);

        var used = new HashSet<long>();
        long? Pop(List<long> pool)
        {
            while (pool.Count > 0)
            {
                long rid = pool[^1]; pool.RemoveAt(pool.Count - 1);
                if (used.Add(rid)) return rid;
            }
            return null;
        }
        long? ChooseSpecial(string tier)
        {
            var byType = new Dictionary<int, List<long>>();
            foreach (var rid in pools[tier]["special"])
            {
                if (used.Contains(rid)) continue;
                int rf = (int)U.L(Players[rid], "rareflag");
                if (!SpecialWeights.TryGetValue(rf, out var w) || w <= 0) continue;
                if (!byType.TryGetValue(rf, out var l)) byType[rf] = l = new();
                l.Add(rid);
            }
            if (byType.Count == 0) return null;
            int pick = Weighted(byType.Keys.Select(k => (k, SpecialWeights[k])).ToList(), -1);
            if (pick < 0) return null;
            var arr = byType[pick];
            long chosen = arr[U.Rng.Next(arr.Count)];
            used.Add(chosen); pools[tier]["special"].Remove(chosen);
            return chosen;
        }

        var players = new List<long>();
        int specials = 0;
        for (int i = 0; i < playerRare; i++)
        {
            var tier = ChooseTier();
            bool allow = specials < 2;
            long? rid = allow && U.Rng.NextDouble() < pc.SpecialChance ? ChooseSpecial(tier) : null;
            rid ??= Pop(pools[tier]["rare"]);
            if (rid == null && allow) rid = ChooseSpecial(tier);
            if (rid == null) foreach (var alt in new[] { "gold", "silver", "bronze" }) { if (alt == tier) continue; rid = Pop(pools[alt]["rare"]); if (rid != null) break; }
            if (rid == null) throw new InvalidOperationException("rare player pool is empty (is players.json present?)");
            if (U.L(Players[rid.Value], "rareflag") > 1) specials++;
            players.Add(rid.Value);
        }
        for (int i = 0; i < playerSlots - playerRare; i++)
        {
            var tier = ChooseTier();
            long? rid = Pop(pools[tier]["common"]);
            if (rid == null) foreach (var alt in new[] { "gold", "silver", "bronze" }) { if (alt == tier) continue; rid = Pop(pools[alt]["common"]); if (rid != null) break; }
            if (rid == null) throw new InvalidOperationException("common player pool is empty (is players.json present?)");
            players.Add(rid.Value);
        }
        // reveal order: specials first, then rating desc, rare first
        players = players.OrderBy(r => U.L(Players[r], "rareflag") > 1 ? 0 : 1)
                         .ThenByDescending(r => U.L(Players[r], "rating")).ThenByDescending(r => U.L(Players[r], "rareflag")).ToList();

        // manager drop: the last (lowest) player of the reveal order becomes a manager card
        ManagerDef mgr = null;
        if (pc.ManagerChance > 0 && Managers.Count > 0 && players.Count > 1 && U.Rng.NextDouble() < pc.ManagerChance)
        {
            players.RemoveAt(players.Count - 1);
            mgr = Managers[U.Rng.Next(Managers.Count)];
        }

        var cons = new List<Consumable>();
        for (int i = 0; i < miscSlots; i++)
        {
            var tier = ChooseTier();
            int wantRare = i < miscRare ? 1 : 0;
            var opts = Consumables.Where(c => c.Tier == tier && c.RareFlag == wantRare).ToList();
            if (opts.Count == 0) opts = Consumables.Where(c => c.RareFlag == wantRare).ToList();
            cons.Add(opts[U.Rng.Next(opts.Count)]);
        }

        // ids/timestamps descend in reveal order, like the Python pack (first revealed = highest id)
        int total = players.Count + cons.Count + (mgr != null ? 1 : 0);
        long baseId = st.ReserveIds(total);
        long ts = U.NowS();
        var items = new List<JsonObject>();
        int idx = 0;
        foreach (var rid in players) { items.Add(MakePlayer(rid, "unassigned", baseId + total - 1 - idx, ts + total - 1 - idx)); idx++; }
        if (mgr != null) { items.Add(MakeManager(baseId + total - 1 - idx, ts + total - 1 - idx, mgr, "unassigned")); idx++; }
        foreach (var c in cons) { items.Add(MakeConsumable(c, "unassigned", baseId + total - 1 - idx, ts + total - 1 - idx)); idx++; }
        return items;
    }
}

// ------------------------------------------------------------------ persistent state (JSON file)

sealed partial class State
{
    readonly object _l = new();
    readonly string _path;
    public long Credits;
    long _nextId = 900000000001;
    public long Established;
    public int ProfileRev;
    public string Token, Sid;
    public List<JsonObject> Items = new();
    public List<JsonObject> Squads = new();

    public State(string dir) { _path = Path.Combine(dir, "state.json"); }

    public void LoadOrCreate()
    {
        lock (_l)
        {
            if (File.Exists(_path))
            {
                try
                {
                    var r = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
                    Credits = U.L(r, "credits", Cfg.StartCredits);
                    if (r["points"] == null) { Credits = Math.Max(Credits, Cfg.StartCredits); Points = Cfg.StartPoints; }   // older save: one-time top-up to the FUT REBORN start balance
                    else Points = U.L(r, "points", Cfg.StartPoints);
                    _nextId = U.L(r, "nextId", _nextId);
                    ProfileRev = (int)U.L(r, "profileRev", 0);
                    Established = U.L(r, "established", U.NowS());
                    Token = U.S(r, "token"); Sid = U.S(r, "sid");
                    if (r["items"] is JsonArray ia) foreach (var n in ia) if (n is JsonObject o) Items.Add((JsonObject)o.DeepClone());
                    if (r["squads"] is JsonArray sa) foreach (var n in sa) if (n is JsonObject o) Squads.Add((JsonObject)o.DeepClone());
                }
                catch (Exception e) { Log.Error("state.json unreadable, starting fresh: " + e.Message); Items.Clear(); Squads.Clear(); }
            }
            if (string.IsNullOrEmpty(Token)) Token = "localfut15-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            if (string.IsNullOrEmpty(Sid)) Sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            if (Established == 0) Established = U.NowS();
            if (ProfileRev < Cfg.ProfileRev)
            {
                // one-time wipe: old club is backed up, everything starts fresh with the start balance
                try { if (File.Exists(_path)) File.Copy(_path, _path + ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
                Items.Clear(); Squads.Clear();
                Credits = Cfg.StartCredits; Points = Cfg.StartPoints;
                _nextId = 900000000001; Established = U.NowS(); ProfileRev = Cfg.ProfileRev;
                Log.Warn("PROFILE WIPE (rev " + Cfg.ProfileRev + "): fresh club, " + Credits + " coins");
            }
            if (Items.Count == 0 && Squads.Count == 0)
            {
                if (File.Exists(_path) == false) { Credits = Cfg.StartCredits; Points = Cfg.StartPoints; }
                SeedStarterClub();
            }
            SaveLocked();
        }
    }

    // In-game "delete club": wipe everything and build a brand new starter club (bronze/silver squad)
    public void ResetClub()
    {
        lock (_l)
        {
            Items.Clear(); Squads.Clear();
            Credits = Cfg.StartCredits; Points = Cfg.StartPoints;
            _nextId = 900000000001; Established = U.NowS(); ProfileRev = Cfg.ProfileRev;
            SeedStarterClub();
            SaveLocked();
            Log.Warn("CLUB RESET: new starter club created, " + Items.Count + " cards");
        }
    }

    void SeedStarterClub()
    {
        var squadPlayers = new JsonArray();
        int idx = 0;
        foreach (var rid in Catalog.PickStarters())
        {
            bool inSquad = idx < 11;
            var item = Catalog.MakePlayer(rid, inSquad ? "squad" : "club", _nextId++, Established);
            Items.Add(item);
            if (inSquad) squadPlayers.Add(new JsonObject { ["index"] = (long)idx, ["itemId"] = U.L(item, "id"), ["kitNumber"] = (long)(idx + 1) });
            idx++;
        }
        long managerId = 0;
        if (Cfg.ManagerEnabled)
        {
            var mgr = Catalog.MakeManager(_nextId++, Established);
            Items.Add(mgr); managerId = U.L(mgr, "id");
        }
        Squads.Add(new JsonObject { ["id"] = 1L, ["squadName"] = "THEKURDISH XI", ["formation"] = "f442", ["active"] = true, ["players"] = squadPlayers, ["managerId"] = managerId });
    }

    void SaveLocked()
    {
        var root = new JsonObject
        {
            ["credits"] = Credits, ["points"] = Points, ["nextId"] = _nextId, ["profileRev"] = ProfileRev, ["established"] = Established, ["token"] = Token, ["sid"] = Sid,
            ["items"] = new JsonArray(Items.Select(i => (JsonNode)i.DeepClone()).ToArray()),
            ["squads"] = new JsonArray(Squads.Select(i => (JsonNode)i.DeepClone()).ToArray()),
        };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, _path, true);
    }

    public void Save() { lock (_l) SaveLocked(); }
    public long ReserveIds(int n) { lock (_l) { long b = _nextId; _nextId += n; return b; } }
    public long AddCredits(long d) { lock (_l) { Credits += d; SaveLocked(); return Credits; } }
    public long Points;
    public long GetPoints() { lock (_l) return Points; }
    public long AddPoints(long d) { lock (_l) { Points += d; SaveLocked(); return Points; } }
    public long GetCredits() { lock (_l) return Credits; }

    public List<JsonObject> ListItems(string pile = null)
    {
        lock (_l) return Items.Where(i => pile == null || U.S(i, "pile") == pile).Select(i => (JsonObject)i.DeepClone()).ToList();
    }

    public JsonObject Get(long id) { lock (_l) { var i = Items.FirstOrDefault(x => U.L(x, "id") == id); return i == null ? null : (JsonObject)i.DeepClone(); } }

    public int ClearPile(string pile) { lock (_l) { int n = Items.RemoveAll(i => U.S(i, "pile") == pile); if (n > 0) SaveLocked(); return n; } }

    public void AddItems(IEnumerable<JsonObject> items) { lock (_l) { foreach (var i in items) Items.Add((JsonObject)i.DeepClone()); SaveLocked(); } }

    public bool SetPile(long id, string pile)
    {
        lock (_l)
        {
            var i = Items.FirstOrDefault(x => U.L(x, "id") == id);
            if (i == null) return false;
            i["pile"] = pile; i.Remove("_trade"); SaveLocked(); return true;
        }
    }

    public JsonObject OwnedPlayerByResource(long rid, long exceptId = 0)
    {
        lock (_l)
        {
            var i = Items.FirstOrDefault(x => U.L(x, "resourceId") == rid && U.S(x, "itemType", "player") == "player"
                                              && U.L(x, "id") != exceptId && (U.S(x, "pile") == "club" || U.S(x, "pile") == "squad"));
            return i == null ? null : (JsonObject)i.DeepClone();
        }
    }

    public int TradeCount() { lock (_l) return Items.Count(i => U.S(i, "pile") == "trade"); }

    public JsonObject ActiveSquad()
    {
        lock (_l)
        {
            var s = Squads.FirstOrDefault(x => U.B(x, "active")) ?? Squads.FirstOrDefault();
            return s == null ? null : (JsonObject)s.DeepClone();
        }
    }

    public JsonObject GetSquad(long id) { lock (_l) { var s = Squads.FirstOrDefault(x => U.L(x, "id") == id); return s == null ? null : (JsonObject)s.DeepClone(); } }
    public List<JsonObject> ListSquads() { lock (_l) return Squads.Select(s => (JsonObject)s.DeepClone()).ToList(); }

    public JsonObject SaveSquad(long id, JsonObject src)
    {
        lock (_l)
        {
            var s = Squads.FirstOrDefault(x => U.L(x, "id") == id);
            if (s == null)
            {
                s = new JsonObject { ["id"] = id, ["squadName"] = "THEKURDISH XI", ["formation"] = "f442", ["active"] = Squads.Count == 0, ["players"] = new JsonArray() };
                Squads.Add(s);
            }
            if (src["squadName"] != null) s["squadName"] = U.S(src, "squadName", "THEKURDISH XI");
            if (src["formation"] != null) s["formation"] = U.S(src, "formation", "f442");
            if (src["manager"] is JsonArray ma)
            {
                long mid = 0;
                foreach (var mn in ma)
                    if (mn is JsonObject mo) { mid = mo["id"] != null ? U.L(mo, "id") : mo["itemData"] is JsonObject md ? U.L(md, "id") : 0; break; }
                bool isMgr = mid > 0 && Items.Any(x => U.L(x, "id") == mid && U.S(x, "itemType") == "manager");
                if (isMgr) s["managerId"] = mid;
                else Log.Info("SQUAD manager slot sent id=" + mid + " (not a manager card) - keeping managerId=" + U.L(s, "managerId"));
            }
            if (src["players"] is JsonArray pl)
            {
                var np = new JsonArray();
                var placed = new HashSet<long>();
                foreach (var pn in pl)
                {
                    if (pn is not JsonObject po) continue;
                    long iid = po["itemId"] != null ? U.L(po, "itemId") : po["itemData"] is JsonObject idata ? U.L(idata, "id") : 0;
                    if (iid == 0) continue;
                    long index = U.L(po, "index", np.Count);
                    np.Add(new JsonObject { ["index"] = index, ["itemId"] = iid, ["kitNumber"] = U.L(po, "kitNumber", index + 1) });
                    placed.Add(iid);
                }
                // players that left the squad go back to the club, players that entered leave it
                var old = new HashSet<long>();
                if (s["players"] is JsonArray op) foreach (var o in op) if (o is JsonObject oo) old.Add(U.L(oo, "itemId"));
                foreach (var it in Items)
                {
                    long iid = U.L(it, "id");
                    if (placed.Contains(iid)) it["pile"] = "squad";
                    else if (old.Contains(iid) && U.S(it, "pile") == "squad") it["pile"] = "club";
                }
                s["players"] = np;
            }
            SaveLocked();
            return (JsonObject)s.DeepClone();
        }
    }
}

// ------------------------------------------------------------------ FUT / POW HTTP routes

static partial class Fut
{
    static State St => Fut15Program.State;

    public static Dictionary<string, string> ParseQuery(string q)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = part.IndexOf('=');
            var k = Uri.UnescapeDataString(i >= 0 ? part[..i] : part);
            var v = i >= 0 ? Uri.UnescapeDataString(part[(i + 1)..].Replace('+', ' ')) : "";
            d[k] = v;
        }
        return d;
    }

    static string PowSid() => "pow." + U.Sha256Hex("POW-" + St.Token)[..32];
    static string UtSid() => "ut." + U.Sha256Hex("UT-" + St.Sid)[..32];

    static HttpRes Ok(JsonNode n, Dictionary<string, string> h = null) =>
        new() { Status = 200, Body = U.Json(n), Headers = h ?? new() };
    static readonly Dictionary<string, string> NoStore = new() { ["Cache-Control"] = "no-store" };
    static Dictionary<string, string> NS() => new(NoStore);

    static JsonObject CreditsPayload(JsonObject into = null)
    {
        long c = St.GetCredits();
        var o = into ?? new JsonObject();
        o["coins"] = c; o["credits"] = c; o["totalCredits"] = c; o["funds"] = c; o["finalFunds"] = c; o["points"] = St.GetPoints(); o["fifaPoints"] = St.GetPoints();
        o["currencies"] = new JsonArray(
            new JsonObject { ["name"] = "COINS", ["funds"] = c, ["finalFunds"] = c },
            new JsonObject { ["name"] = "POINTS", ["funds"] = St.GetPoints(), ["finalFunds"] = St.GetPoints() });
        o["unopenedPacks"] = new JsonObject { ["preOrderPacks"] = 0L, ["recoveredPacks"] = 0L };
        return o;
    }

    static JsonObject PackItem(JsonObject item)
    {
        var o = (JsonObject)item.DeepClone();
        foreach (var k in new[] { "pile", "special", "tier", "wefutCard", "consumableKind", "_trade" }) o.Remove(k);
        return o;
    }

    static JsonObject DisplayAlias(JsonObject item, int rank)
    {
        var o = (JsonObject)item.DeepClone();
        if (U.S(o, "itemType", "player") != "player") return o;
        long asset = U.L(o, "assetId") & 0x00FFFFFF;
        o["resourceId"] = ((long)((rank + 1) & 0x7F) << 24) | asset;
        return o;
    }

    static JsonObject NativePlayer(JsonObject item) => PackItem(item);

    static JsonObject NativeConsumableForCache(JsonObject item)
    {
        var o = PackItem(item);
        o["pile"] = 7L;
        return o;
    }

    static JsonObject NativeSquad(long? requested = null)
    {
        var squads = St.ListSquads();
        JsonObject src = null;
        if (requested is > 0) src = squads.FirstOrDefault(x => U.L(x, "id") == requested);
        src ??= squads.FirstOrDefault(x => U.B(x, "active")) ?? squads.FirstOrDefault();
        src ??= new JsonObject { ["id"] = 1L, ["squadName"] = "THEKURDISH XI", ["formation"] = "f442", ["players"] = new JsonArray() };
        var byId = St.ListItems().ToDictionary(x => U.L(x, "id"));
        var positions = new Dictionary<long, JsonObject>();
        if (src["players"] is JsonArray pl)
            foreach (var pn in pl)
            {
                if (pn is not JsonObject po) continue;
                long iid = U.L(po, "itemId"); long idx = U.L(po, "index", positions.Count);
                if (byId.TryGetValue(iid, out var item))
                    positions[idx] = new JsonObject { ["index"] = idx, ["kitNumber"] = U.L(po, "kitNumber", idx + 1), ["itemData"] = NativePlayer(item) };
            }
        var players = new JsonArray();
        var kick = new List<long>();
        long captain = 0;
        for (long i = 0; i < 23; i++)
        {
            if (positions.TryGetValue(i, out var p))
            {
                players.Add(p);
                long id = U.L((JsonObject)p["itemData"], "id");
                if (captain == 0) captain = id;
                if (kick.Count < 5) kick.Add(id);
            }
            else players.Add(new JsonObject { ["index"] = i, ["kitNumber"] = 0L });
        }
        while (kick.Count > 0 && kick.Count < 5) kick.Add(kick[^1]);
        var mgrArr = new JsonArray();
        long mgrId = U.L(src, "managerId");
        if (mgrId > 0 && byId.TryGetValue(mgrId, out var mgrItem))
            mgrArr.Add(new JsonObject { ["id"] = mgrId, ["dream"] = false, ["itemData"] = NativePlayer(mgrItem) });
        var kt = new JsonArray();
        for (int i = 0; i < kick.Count; i++) kt.Add(new JsonObject { ["index"] = (long)i, ["id"] = kick[i], ["dream"] = false });
        return new JsonObject
        {
            ["id"] = U.L(src, "id", 1), ["personaId"] = Cfg.PersonaId, ["squadName"] = U.S(src, "squadName", "THEKURDISH XI"),
            ["formation"] = U.S(src, "formation", "f442"), ["captain"] = U.L(src, "captain", captain), ["chemistry"] = 100L, ["changed"] = 0L,
            ["starRating"] = 5L, ["custom"] = "[0,0,0,0,0,0,0,0,0,0,0]", ["players"] = players,
            ["actives"] = new JsonArray(), ["manager"] = mgrArr, ["club"] = new JsonArray(), ["kicktakers"] = kt,
        };
    }

    static JsonObject NativeSquadList()
    {
        var arr = new JsonArray();
        long active = 0;
        var squads = St.ListSquads();
        foreach (var s in squads)
        {
            long id = U.L(s, "id");
            if (U.B(s, "active") && active == 0) active = id;
            arr.Add(new JsonObject
            {
                ["id"] = id, ["personaId"] = Cfg.PersonaId, ["squadName"] = U.S(s, "squadName", "THEKURDISH XI"), ["formation"] = U.S(s, "formation", "f442"),
                ["active"] = U.B(s, "active"), ["changed"] = false, ["chemistry"] = 0L, ["starRating"] = 0L, ["rating"] = 0L, ["valid"] = true, ["newsquad"] = 0L,
            });
        }
        if (active == 0 && squads.Count > 0) active = U.L(squads[0], "id");
        return new JsonObject { ["activeSquadId"] = active, ["squad"] = arr };
    }

    static JsonObject NativeUser()
    {
        var all = St.ListItems();
        var clubPlayers = all.Where(x => U.S(x, "pile") == "club" && U.S(x, "itemType", "player") == "player").ToList();
        var squadPlayers = all.Where(x => U.S(x, "pile") == "squad" && U.S(x, "itemType", "player") == "player").ToList();
        var consum = all.Where(x => U.S(x, "pile") == "club" && U.S(x, "itemType") == "development").ToList();
        long totalPlayers = clubPlayers.Count + squadPlayers.Count;
        var o = new JsonObject
        {
            ["pileClubPlayers"] = (long)clubPlayers.Count, ["pileSquadPlayers"] = (long)squadPlayers.Count, ["pileConsumables"] = (long)consum.Count,
            ["pileManagers"] = (long)all.Count(x => U.S(x, "itemType") == "manager"), ["pileClubItems"] = 0L, ["pileClubTotal"] = totalPlayers + consum.Count,
            ["pilePack"] = (long)all.Count(x => U.S(x, "pile") == "unassigned"),
            ["kitCount"] = 0L, ["badgeCount"] = 0L, ["stadiumCount"] = 0L, ["ballCount"] = 0L,
            ["clubPlayers"] = totalPlayers, ["clubItemCount"] = (long)consum.Count, ["clubCount"] = totalPlayers + consum.Count,
            ["accountCreatedPlatformName"] = "pc", ["personaId"] = Cfg.PersonaId, ["clubName"] = Cfg.ClubName, ["clubAbbr"] = Cfg.ClubAbbr,
            ["clubNameChangeAllowed"] = 0L, ["returningUser"] = 1L, ["established"] = St.Established.ToString(),
        };
        CreditsPayload(o);
        o["bidTokens"] = new JsonObject { ["count"] = 0L, ["updateTime"] = 0L };
        o["divisionOffline"] = 10L; o["divisionOnline"] = 10L; o["won"] = 0L; o["draw"] = 0L; o["loss"] = 0L; o["trophies"] = 0L;
        o["purchased"] = false; o["fifaPointsFromLastYear"] = 0L; o["fifaPointsTransferredStatus"] = 0L;
        o["reliability"] = new JsonObject { ["reliability"] = 100L, ["matchUnfinishedTime"] = 0L };
        o["actives"] = new JsonArray();
        o["squadList"] = NativeSquadList();
        return o;
    }

    static JsonObject ClubStats()
    {
        var all = St.ListItems();
        long players = all.Count(x => (U.S(x, "pile") == "club" || U.S(x, "pile") == "squad") && U.S(x, "itemType", "player") == "player");
        var cons = all.Where(x => U.S(x, "pile") == "club" && U.S(x, "itemType") == "development").ToList();
        long K(string k) => cons.Count(x => U.S(x, "consumableKind") == k);
        long KP(string p) => cons.Count(x => U.S(x, "consumableKind").StartsWith(p));
        var o = new JsonObject
        {
            ["playersEmployed"] = players, ["rarePlayersEmployed"] = players, ["bronzePlayersEmployed"] = 0L, ["silverPlayersEmployed"] = 0L, ["goldPlayersEmployed"] = players,
            ["staffEmployed"] = 0L, ["kitsAvailable"] = 0L, ["badgesAvailable"] = 0L, ["stadiaOwned"] = 0L, ["ballsEarned"] = 0L, ["trophiesWon"] = 0L,
            ["consumables"] = (long)cons.Count, ["kits"] = 0L, ["badges"] = 0L, ["stadia"] = 0L, ["balls"] = 0L, ["trophies"] = 0L,
            ["consumablesContract"] = KP("contract_"), ["consumablesFitness"] = KP("fitness_"), ["consumablesHealing"] = K("healing"),
            ["consumablesPlaystyle"] = 0L, ["consumablesTraining"] = KP("training_"), ["consumablesTrainingPlayerPlayStyle"] = 0L, ["consumablesTrainingGkPlayStyle"] = 0L,
            ["consumablesContractPlayer"] = K("contract_player"), ["consumablesContractManager"] = K("contract_manager"),
            ["consumablesFitnessPlayer"] = K("fitness_player"), ["consumablesFitnessTeam"] = K("fitness_team"), ["consumablesTrainingManager"] = 0L,
            ["consumablesTrainingPlayer"] = K("training_player"), ["consumablesTrainingGk"] = 0L, ["consumablesTrainingManagerLeagueModifier"] = 0L,
            ["consumablesPosition"] = 0L, ["consumablesFormationManager"] = 0L, ["staffHeadCoach"] = 0L, ["staffFitnessCoach"] = 0L, ["staffGKCoach"] = 0L, ["staffPhysio"] = 0L,
            ["trophiesOffline"] = 0L, ["trophiesOnline"] = 0L, ["trophiesFeaturedOffline"] = 0L, ["trophiesFeaturedOnline"] = 0L, ["trophiesSeasonOffline"] = 0L, ["trophiesSeasonOnline"] = 0L,
        };
        var stat = new JsonArray();
        void S(string n, long v) => stat.Add(new JsonObject { ["type"] = n, ["typeValue"] = v, ["contextId"] = 0L, ["contextValue"] = 0L });
        S("FUT_MYCLUB_PLAYERS_EMPLOYED", players); S("FUT_MYCLUB_RARE_PLAYERS_EMPLOYED", players); S("FUT_MYCLUB_GOLD_PLAYERS_EMPLOYED", players);
        S("FUT_MYCLUB_SILVER_PLAYERS_EMPLOYED", 0); S("FUT_MYCLUB_BRONZE_PLAYERS_EMPLOYED", 0); S("FUT_MYCLUB_TROPHIES_WON", 0);
        S("FUT_MYCLUB_KITS_AVAILABLE", 0); S("FUT_MYCLUB_BADGES_AVAILABLE", 0); S("FUT_MYCLUB_STADIA_OWNED", 0); S("FUT_MYCLUB_BALLS_EARNED", 0); S("FUT_MYCLUB_STAFF_EMPLOYED", 0);
        S("players", players); S("playersEmployed", players); S("rarePlayers", players); S("rarePlayersEmployed", players);
        S("playersBronze", 0); S("playersSilver", 0); S("playersGold", players); S("staff", 0); S("staffEmployed", 0);
        S("consumables", cons.Count); S("clubPlayers", players); S("playerCount", players); S("rareQuantity", players);
        o["stat"] = stat;
        o["newcards"] = (long)all.Count(x => U.S(x, "pile") == "unassigned");
        return o;
    }

    static (string group, long assetId) GroupOf(PackCfg p) => p.Category switch { "bronze" => ("bronze", 1), "silver" => ("silver", 2), _ => ("gold", 3) };

    static JsonObject StorePurchaseGroups()
    {
        long now = U.NowS();
        var purchase = new JsonArray();
        int pri = 0;
        foreach (var p in Catalog.Packs.Values.Where(x => x.Enabled).OrderBy(x => x.Id))
        {
            pri++;
            var (grp, asset) = GroupOf(p);
            long primary = p.Size - 1;
            long gold = grp == "gold" ? primary : grp == "silver" ? 1 : 0;
            long silver = grp == "silver" ? primary : 1;
            long bronze = grp == "bronze" ? primary : 0;
            if (grp == "gold") { silver = 1; bronze = 0; }
            long grpPriority = asset;
            purchase.Add(new JsonObject
            {
                ["actionType"] = "CREATEPACK", ["assetId"] = asset, ["bonus"] = 0L,
                ["currencies"] = new JsonArray(
                    new JsonObject { ["name"] = "COINS", ["funds"] = (long)p.Price, ["finalFunds"] = (long)p.Price },
                    new JsonObject { ["name"] = "POINTS", ["funds"] = (long)p.Points, ["finalFunds"] = (long)p.Points }),
                ["name"] = p.Name, ["packName"] = p.Name, ["displayName"] = p.Name, ["title"] = p.Name, ["description"] = p.Name,
                ["packDescription"] = p.Bio, ["longDescription"] = p.Bio, ["shortDescription"] = p.Bio, ["bio"] = p.Bio,
                ["label"] = p.Name, ["displayLabel"] = p.Name, ["storePackName"] = p.Name,
                ["nameToken"] = $"FUT_STORE_PACK_{p.Id}_NAME", ["descriptionToken"] = $"FUT_STORE_PACK_{p.Id}_DESC",
                ["displayGroup"] = new JsonObject { ["priority"] = grpPriority, ["value"] = grp }, ["displayGroupAssetId"] = asset, ["displayGroupUseDefaultImage"] = true,
                ["end"] = now + 315360000, ["firstPartyStoreId"] = "0", ["id"] = (long)p.Id, ["packId"] = (long)p.Id,
                ["packType"] = grp.ToUpperInvariant(), ["packTypeId"] = (long)p.Id, ["purchasePackType"] = "CARDPACK",
                ["purchaseCount"] = 0L, ["purchaseLimit"] = 999999L, ["quantity"] = (long)p.Size,
                ["isPremium"] = p.Name.StartsWith("Premium"), ["isSeasonTicketDiscount"] = false, ["points"] = (long)p.Points,
                ["priority"] = (long)pri, ["saleType"] = "NONE", ["sortPriority"] = (long)pri, ["start"] = now - 86400, ["state"] = "active",
                ["unopened"] = false, ["useDefaultImage"] = true, ["visible"] = true, ["isPurchaseable"] = true,
                ["finalPrice"] = (long)p.Price, ["originalPrice"] = (long)p.Price, ["group"] = grp, ["groupName"] = char.ToUpper(grp[0]) + grp[1..] + " Packs",
                ["isPromo"] = false, ["category"] = grp, ["categoryId"] = asset,
                ["packContentInfo"] = new JsonObject
                {
                    ["itemQuantity"] = (long)p.Size, ["goldQuantity"] = gold, ["silverQuantity"] = silver, ["bronzeQuantity"] = bronze, ["rareQuantity"] = (long)p.RareSlots,
                },
            });
        }
        JsonArray Cats()
        {
            var a = new JsonArray();
            var names = new[] { ("bronze", 1, true), ("silver", 2, true), ("gold", 3, true), ("promo", 4, false) };
            foreach (var (v, id, vis) in names)
            {
                var nm = char.ToUpper(v[0]) + v[1..] + " Packs";
                a.Add(new JsonObject { ["id"] = (long)id, ["categoryId"] = (long)id, ["name"] = nm, ["groupName"] = nm, ["value"] = v, ["displayGroup"] = v, ["sortPriority"] = (long)id, ["visible"] = vis });
            }
            return a;
        }
        return new JsonObject { ["purchase"] = purchase, ["categoryInfo"] = Cats(), ["categories"] = Cats(), ["timestamp"] = now };
    }

    static string PackLocXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<xliff version=\"1.2\">\n  <file original=\"storepackdescriptions\" source-language=\"en-US\" datatype=\"plaintext\">\n    <body>\n");
        foreach (var p in Catalog.Packs.Values.Where(x => x.Enabled).OrderBy(x => x.Id))
        {
            string nm = WebUtility.HtmlEncode(p.Name), bio = WebUtility.HtmlEncode(p.Bio);
            sb.Append($"      <trans-unit id=\"FUT_STORE_PACK_{p.Id}_NAME\" resname=\"FUT_STORE_PACK_{p.Id}_NAME\"><source>{nm}</source></trans-unit>\n");
            sb.Append($"      <trans-unit id=\"FUT_STORE_PACK_{p.Id}_DESC\" resname=\"FUT_STORE_PACK_{p.Id}_DESC\"><source>{bio}</source></trans-unit>\n");
            sb.Append($"      <trans-unit id=\"FUT_STORE_PACK_{p.Id}_NAME_MOBILE\" resname=\"FUT_STORE_PACK_{p.Id}_NAME_MOBILE\"><source>{nm}</source></trans-unit>\n");
        }
        sb.Append("    </body>\n  </file>\n</xliff>");
        return sb.ToString();
    }

    static JsonArray DuplicateList(IEnumerable<JsonObject> items)
    {
        var dups = new JsonArray();
        foreach (var it in items)
        {
            if (U.S(it, "itemType", "player") != "player") continue;
            long iid = U.L(it, "id");
            var owned = St.OwnedPlayerByResource(U.L(it, "resourceId"), iid);
            if (owned != null) dups.Add(new JsonObject { ["itemId"] = iid, ["duplicateItemId"] = U.L(owned, "id") });
        }
        return dups;
    }

    static HttpRes OpenPack(JsonObject payload)
    {
        int packId = payload != null ? (int)U.L(payload, "packId", 3) : 3;
        if (!Catalog.Packs.TryGetValue(packId, out var pc))
            return Ok(new JsonObject { ["duplicateItemIdList"] = new JsonArray(), ["itemData"] = new JsonArray() });
        if (payload == null || U.L(payload, "useCredits", 1) != 0)
        {
            if (St.GetCredits() < pc.Price)
                return Ok(CreditsPayload(new JsonObject { ["duplicateItemIdList"] = new JsonArray(), ["itemData"] = new JsonArray() }));
            St.AddCredits(-pc.Price);
        }
        else if (pc.Points > 0)
        {
            if (St.GetPoints() < pc.Points)
                return Ok(CreditsPayload(new JsonObject { ["duplicateItemIdList"] = new JsonArray(), ["itemData"] = new JsonArray() }));
            St.AddPoints(-pc.Points);
        }
        Log.Warn($"CARDS-SCOUT PACK_OPEN_BEGIN packId={packId}");
        St.ClearPile("unassigned");
        List<JsonObject> items;
        try { items = Catalog.BuildPack(pc, St); }
        catch (Exception e)
        {
            Log.Error("pack generation failed: " + e.Message);
            if (payload == null || U.L(payload, "useCredits", 1) != 0) St.AddCredits(pc.Price); else St.AddPoints(pc.Points); // refund
            return Ok(CreditsPayload(new JsonObject { ["duplicateItemIdList"] = new JsonArray(), ["itemData"] = new JsonArray() }));
        }
        St.AddItems(items);
        var dups = DuplicateList(items);
        var ordered = items.Select(PackItem).Select((x, i) => DisplayAlias(x, i)).ToList();
        Log.Warn($"PACK id={packId} name={pc.Name} items={items.Count} resources=[{string.Join(",", items.Select(i => U.L(i, "resourceId")))}]");
        JsonArray Arr() { var a = new JsonArray(); foreach (var o in ordered) a.Add(o.DeepClone()); return a; }
        var res = new JsonObject
        {
            ["numberItems"] = (long)items.Count, ["purchasedPackId"] = (long)packId, ["duplicateItemIdList"] = dups,
            ["itemList"] = Arr(), ["itemData"] = Arr(), ["items"] = Arr(),
        };
        return Ok(CreditsPayload(res));
    }

    static HttpRes UnassignedItems()
    {
        var un = St.ListItems("unassigned")
            .OrderBy(i => (U.B(i, "special") || U.L(i, "rareflag") > 1) ? 0 : 1)
            .ThenByDescending(i => U.L(i, "rating")).ThenByDescending(i => U.L(i, "rareflag")).ThenByDescending(i => U.L(i, "resourceId")).ToList();
        var dups = DuplicateList(un);
        bool market = un.Any(i => U.L(i, "lastSalePrice") > 0);
        var arr = new JsonArray();
        for (int i = 0; i < un.Count; i++)
        {
            var it = PackItem(un[i]);
            arr.Add(market ? it : DisplayAlias(it, i));
        }
        return Ok(new JsonObject { ["numberItems"] = (long)un.Count, ["duplicateItemIdList"] = dups, ["itemData"] = arr });
    }

    static HttpRes ItemUpdate(JsonObject payload)
    {
        var results = new JsonArray();
        var dupResults = new JsonArray();
        JsonArray list = payload?["itemData"] as JsonArray;
        if (list == null && payload?["itemData"] is JsonObject single) list = new JsonArray(single.DeepClone());
        list ??= new JsonArray();
        bool batch = list.Count > 1;
        foreach (var node in list)
        {
            if (node is not JsonObject ch) continue;
            long iid = ch["id"] != null ? U.L(ch, "id") : U.L(ch, "itemId");
            if (iid == 0) continue;
            var cur = St.Get(iid);
            string requested = U.S(ch, "pile", null);
            if (cur == null) { results.Add(new JsonObject { ["id"] = iid, ["pile"] = requested ?? "club", ["success"] = false }); continue; }
            string curPile = U.S(cur, "pile", "club");
            string pile = requested ?? curPile;
            if (requested != null && (pile == "club" || pile == "squad") && U.S(cur, "itemType", "player") == "player"
                && St.OwnedPlayerByResource(U.L(cur, "resourceId"), iid) != null)
            {
                dupResults.Add(new JsonObject { ["itemId"] = iid });
                var r = new JsonObject { ["id"] = iid, ["pile"] = "unassigned", ["success"] = true };
                if (!batch) r["IS_DUPLICATE"] = true;
                results.Add(r);
                continue;
            }
            if (requested != null && pile == "trade" && curPile != "trade" && St.TradeCount() >= 100)
            {
                results.Add(new JsonObject { ["id"] = iid, ["pile"] = curPile, ["success"] = false, ["reason"] = "TRADE_PILE_FULL" });
                continue;
            }
            bool ok = true;
            if (requested != null) ok = St.SetPile(iid, pile);
            Log.Info($"ITEM UPDATE id={iid} pile={pile} explicitPile={requested != null} success={ok}");
            results.Add(new JsonObject { ["id"] = iid, ["pile"] = pile, ["success"] = ok });
        }
        var resp = new JsonObject { ["itemData"] = results };
        if (dupResults.Count > 0) resp["duplicateItemIdList"] = dupResults;
        return Ok(resp);
    }

    static JsonObject TradePileEmpty()
    {
        long c = St.GetCredits();
        return new JsonObject
        {
            ["auctionInfo"] = new JsonArray(), ["duplicateItemIdList"] = new JsonArray(), ["credits"] = c, ["totalCredits"] = c, ["coins"] = c,
            ["total"] = 0L, ["count"] = 0L, ["currentTradePileSize"] = 0L, ["tradePileCount"] = 0L, ["tradePileItems"] = 0L, ["transferListCount"] = 0L,
            ["maxAuctionsAllowed"] = 100L, ["maximumTradePileSize"] = 100L, ["offered"] = 0L, ["selling"] = 0L, ["activeCount"] = 0L, ["sold"] = 0L,
            ["soldCount"] = 0L, ["available"] = 0L, ["unlisted"] = 0L, ["itemData"] = new JsonArray(),
        };
    }

    public static HttpRes Handle(HttpReq req)
    {
        var res = Route(req);
        var t = req.Target;
        if (t.StartsWith("/ut") || t.StartsWith("//ut")) res.Headers.TryAdd("X-UT-SID", UtSid());
        return res;
    }

    static HttpRes Route(HttpReq req)
    {
        string method = req.Method;
        string raw = req.Target;
        if (raw.StartsWith("//pow/")) raw = "/" + raw.TrimStart('/');
        int qi = raw.IndexOf('?');
        string pathPart = qi >= 0 ? raw[..qi] : raw;
        var query = ParseQuery(qi >= 0 ? raw[(qi + 1)..] : "");
        string low = ("/" + pathPart.TrimStart('/')).ToLowerInvariant().TrimEnd('/');
        if (low == "") low = "/";
        JsonObject payload = null;
        if (req.Body.Length > 0) { try { payload = JsonNode.Parse(req.Body) as JsonObject; } catch { } }

        if (new[] { "store", "pack", "purchase", "loc" }.Any(x => low.Contains(x)))
            Log.Warn($"STORE-SCOUT REQUEST method={method} path={raw} body={Encoding.UTF8.GetString(req.Body, 0, Math.Min(req.Body.Length, 300))}");

        // pack background art (optional assets\packs folder)
        var m = System.Text.RegularExpressions.Regex.Match(low, @"^/fut/packs/images/(?:kc|web)/packs_backgrounds_(\d+)\.png$");
        if (m.Success && method == "GET")
        {
            var f = Path.Combine(Cfg.Root, "assets", "packs", $"packs_backgrounds_{m.Groups[1].Value}.png");
            if (File.Exists(f))
                return new HttpRes { Body = File.ReadAllBytes(f), ContentType = "image/png", Headers = new() { ["Cache-Control"] = "public, max-age=3600" } };
            return new HttpRes { Status = 404, Body = Encoding.UTF8.GetBytes("pack background not found"), ContentType = "text/plain" };
        }

        // ---- POW / EASFC bootstrap
        if (low == "/pow/auth")
        {
            var sid = PowSid();
            return Ok(new JsonObject { ["sid"] = sid, ["serverTime"] = U.NowS(), ["lastOnlineTime"] = U.NowS() }, new() { ["X-POW-SID"] = sid });
        }
        if (low.StartsWith("/pow/"))
        {
            if (low == "/pow/bank/user/account")
                return Ok(new JsonObject
                {
                    ["currencies"] = new JsonArray(new JsonObject
                    {
                        ["currency"] = "pow_funds", ["funds"] = St.GetPoints(),
                        ["fundsCapInfo"] = new JsonArray(
                            new JsonObject { ["period"] = "daily", ["fundsEarned"] = 0L },
                            new JsonObject { ["period"] = "weekly", ["fundsEarned"] = 0L }),
                    }),
                });
            if (low == "/pow/store/game/fifa16/catalog/list")
                return Ok(new JsonObject
                {
                    ["catalogs"] = new JsonArray(
                        new JsonObject { ["catalogId"] = 0L, ["name"] = "FUT Store" },
                        new JsonObject { ["catalogId"] = 1L, ["name"] = "FIFAPoints" }),
                });
            if (low.Contains("/catalog/0/item/list")) return Ok(new JsonObject { ["items"] = new JsonArray(), ["endOfList"] = true });
            return Ok(new JsonObject());
        }

        // ---- FUT authentication
        if (low == "/ut/auth")
        {
            if (method == "DELETE") return Ok(new JsonObject());
            var sid = UtSid();
            return Ok(new JsonObject { ["sid"] = sid, ["protocol"] = "http" }, new() { ["X-UT-SID"] = sid });
        }
        if (low == "/ut/shards" || low == "/ut/shards/v2")
            return Ok(new JsonObject
            {
                ["shardInfo"] = new JsonArray(new JsonObject
                {
                    ["customdata1"] = "127.0.0.1", ["customdata2"] = Cfg.FutPort.ToString(), ["name"] = "LocalFUT15", ["platform"] = "pc",
                    ["clientFacingIpPort"] = $"127.0.0.1:{Cfg.FutPort}",
                }),
            });
        if (low == "/ut/game/fifa16/user/accountinfo")
            return Ok(new JsonObject
            {
                ["userAccountInfo"] = new JsonObject
                {
                    ["personas"] = new JsonArray(new JsonObject
                    {
                        ["personaId"] = Cfg.PersonaId, ["personaName"] = Cfg.DisplayName,
                        ["userClubList"] = new JsonArray(new JsonObject
                        {
                            ["year"] = "2016", ["platform"] = "pc", ["clubName"] = Cfg.ClubName, ["established"] = St.Established,
                            ["assetId"] = (long)Cfg.ClubId, ["clubAbbr"] = Cfg.ClubAbbr,
                        }),
                        ["returningUser"] = 1L,
                    }),
                },
            });
        if (low == "/ut/game/fifa16/user/club" && (method == "PUT" || method == "POST"))
        {
            if (payload != null)
            {
                var cn = U.S(payload, "clubName", ""); var ca = U.S(payload, "clubAbbr", "");
                if (cn.Length > 0) Cfg.ClubName = cn;
                if (ca.Length > 0) Cfg.ClubAbbr = ca;
            }
            return Ok(CreditsPayloadClub(), NS());
        }

        // ---- static client-side files
        if (low == "/fut/loc/pc/leaderboards.eng_us.xml" || low == "/fut/tutorials/cmn")
            return new HttpRes { Status = 404, Body = Encoding.UTF8.GetBytes("not found"), ContentType = "text/plain" };
        if (low == "/fut/packs/loc/storepackdescriptions.en_us.xml")
            return new HttpRes
            {
                Body = Encoding.UTF8.GetBytes(PackLocXml()), ContentType = "application/xml; charset=utf-8",
                Headers = new() { ["Cache-Control"] = "no-store, no-cache, must-revalidate", ["Pragma"] = "no-cache", ["Expires"] = "0" },
            };

        // ---- FIFA 15 game namespace
        if (low == "/ut/game/fifa16/settings")
            return Ok(new JsonObject
            {
                ["configs"] = new JsonArray(
                    new JsonObject { ["type"] = "IsStoreEnabled", ["value"] = "1" },
                    new JsonObject { ["type"] = "cardPackStoreEnabled", ["value"] = "1" },
                    new JsonObject { ["type"] = "pointsPackStoreEnabled", ["value"] = "1" }),
            });
        if (low.StartsWith("/ut/game/fifa16/phishing/trusteddevice"))
            return Ok(new JsonObject
            {
                ["trusted"] = true, ["exists"] = true, ["locked"] = false, ["changed"] = false, ["token"] = "fut15-local-phishing-token",
                ["isTrusted"] = true, ["completed"] = true, ["answerRequired"] = false, ["attempts"] = 0L,
            });
        if (low == "/ut/game/fifa16/match/reset")
        {
            Log.Info("MATCH FLOW reset method=" + method);
            return Ok(new JsonObject { ["reset"] = true });
        }
        if (low == "/ut/game/fifa16/user") return Ok(NativeUser());
        if (low == "/ut/game/fifa16/userdata")
            return Ok(new JsonObject { ["onlineELORating"] = 0L, ["onlineRatedUser"] = false, ["accountResetCount"] = 0L });
        if (low == "/ut/v2/game/fifa16/store/transaction") return Ok(new JsonObject { ["state"] = "NOTRANSACTION" });

        JsonObject Entries(params (long k, long v)[] kv)
        {
            var a = new JsonArray();
            foreach (var (k, v) in kv) a.Add(new JsonObject { ["key"] = k, ["value"] = v });
            return new JsonObject { ["entries"] = a };
        }
        if (low == "/ut/game/fifa16/clientdata/tutorialpopups") return Ok(Entries());
        if (low == "/ut/game/fifa16/clientdata/pilesize") return Ok(Entries((2, 100), (4, 100)));
        if (low == "/ut/game/fifa16/clientdata/userhubdata") return Ok(Entries((0, 13), (1, 12)));
        if (low == "/ut/game/fifa16/clientdata/managerquest") return Ok(Entries((0, 148528), (1, 148528), (2, 0)));
        if (low == "/ut/game/fifa16/clientdata/loanplayerreward") return Ok(Entries());
        if (low == "/ut/game/fifa16/loan/players") return Ok(new JsonObject { ["loans"] = new JsonArray() });

        // ---- squads
        if (low == "/ut/game/fifa16/squad/active" && method == "GET") return Ok(NativeSquad(), NS());
        if (low == "/ut/game/fifa16/squad/list" && method == "GET") return Ok(NativeSquadList(), NS());
        if (low == "/ut/game/fifa16/squad" && method == "GET") return Ok(NativeSquadList(), NS());
        var sm = System.Text.RegularExpressions.Regex.Match(low, @"^/ut/game/fifa16/squad/(\d+)$");
        if (sm.Success)
        {
            long sid = long.Parse(sm.Groups[1].Value);
            if (method == "GET") return Ok(NativeSquad(sid), NS());
            if (method == "PUT" || method == "POST")
            {
                var src = payload ?? new JsonObject();
                St.SaveSquad(sid, src);
                return Ok(NativeSquad(sid), NS());
            }
        }

        if (low == "/ut/game/fifa16/eventfeed") return Ok(new JsonObject { ["event"] = new JsonArray(), ["timestamp"] = U.NowS() });
        if (low == "/ut/game/fifa16/hub")
        {
            var all = St.ListItems();
            return Ok(new JsonObject
            {
                ["auctionCount"] = 0L, ["liveTransfers"] = 0L,
                ["clubPlayers"] = (long)all.Count(x => U.S(x, "pile") == "club" || U.S(x, "pile") == "squad"),
                ["tradePile"] = new JsonObject { ["count"] = (long)St.TradeCount(), ["selling"] = 0L, ["sold"] = 0L, ["maximum"] = 100L },
                ["watchlist"] = new JsonObject { ["count"] = 0L, ["outbid"] = 0L, ["winning"] = 0L },
                ["squad"] = NativeSquad(),
            });
        }
        if (low == "/ut/game/fifa16/clubuser")
        {
            var all = St.ListItems();
            var players = all.Where(x => (U.S(x, "pile") == "club" || U.S(x, "pile") == "squad") && U.S(x, "itemType", "player") == "player")
                .OrderByDescending(x => U.L(x, "rating")).ThenByDescending(x => U.L(x, "rareflag")).ThenBy(x => U.L(x, "id")).ToList();
            var cons = all.Where(x => U.S(x, "pile") == "club" && U.S(x, "itemType") == "development").ToList();
            var pre = new JsonArray();
            foreach (var p in players.Take(50)) pre.Add(NativePlayer(p));
            foreach (var c in cons) pre.Add(NativeConsumableForCache(c));
            foreach (var mg in all.Where(x => U.S(x, "itemType") == "manager")) pre.Add(NativePlayer(mg));
            return Ok(new JsonObject
            {
                ["user"] = new JsonArray(new JsonObject { ["persona"] = Cfg.DisplayName, ["personaId"] = Cfg.PersonaId, ["public"] = true }),
                ["itemData"] = pre, ["total"] = (long)(players.Count + cons.Count), ["count"] = (long)pre.Count, ["start"] = 0L,
            }, NS());
        }
        if (low == "/ut/game/fifa16/leaderboards/options") return Ok(new JsonObject());
        if (low.StartsWith("/ut/game/fifa16/club/stats/")) return Ok(ClubStats());

        // ---- credits / store / packs
        if (low == "/ut/game/fifa16/user/credits") return Ok(CreditsPayload());
        if (low == "/ut/game/fifa16/store/purchasegroup/all" || low == "/ut/game/fifa16/store/purchasegroup/cardpack")
            return Ok(StorePurchaseGroups(), NS());
        if (low == "/ut/game/fifa16/purchased/items")
            return method == "POST" ? OpenPack(payload) : UnassignedItems();

        // ---- item moves
        if (low == "/ut/game/fifa16/item" && (method == "PUT" || method == "POST")) return ItemUpdate(payload);

        // ---- FUT REBORN stage 2: transfer market, trade pile, watchlist, TOTW
        var mk = MarketRoute(method, low, query, payload);
        if (mk != null) return mk;

        // ---- stubs for pages that need a native-shaped empty answer
        if (low == "/ut/game/fifa16/watchlist")
        {
            if (method == "GET")
                return Ok(new JsonObject { ["auctionInfo"] = new JsonArray(), ["itemData"] = new JsonArray(), ["total"] = 0L, ["count"] = 0L, ["winning"] = 0L, ["outbid"] = 0L, ["credits"] = St.GetCredits() });
            return Ok(new JsonObject { ["success"] = true, ["auctionInfo"] = new JsonArray(), ["credits"] = St.GetCredits() });
        }
        if (low == "/ut/game/fifa16/tradepile") return Ok(TradePileEmpty());
        if (low == "/ut/game/fifa16/transfermarket" && method == "GET")
            return Ok(new JsonObject { ["auctionInfo"] = new JsonArray(), ["total"] = 0L, ["count"] = 0L, ["credits"] = St.GetCredits() });
        if (low == "/ut/game/fifa16/tradepile/counts" || low == "/ut/game/fifa16/auctionhouse/counts")
            return Ok(new JsonObject { ["auctionCount"] = 0L, ["maxAuctionsAllowed"] = 100L, ["maximumTradePileSize"] = 100L, ["currentTradePileSize"] = 0L });

        var suffix = low;
        var um = System.Text.RegularExpressions.Regex.Match(low, @"^/ut/(?:game/)?[^/]+/(.*)$");
        if (um.Success) suffix = "/" + um.Groups[1].Value;
        if (suffix == "/usermassinfo" || suffix == "/user/massinfo")
            return Ok(CreditsPayload(new JsonObject
            {
                ["userInfo"] = new JsonObject { ["personaId"] = Cfg.PersonaId, ["clubId"] = (long)Cfg.ClubId, ["credits"] = St.GetCredits(), ["established"] = 2014L },
            }));

        Log.Warn($"Unknown FUT/POW request: {method} {raw} body={Encoding.UTF8.GetString(req.Body, 0, Math.Min(req.Body.Length, 500))}");
        return new HttpRes { Body = U.Json(new JsonObject()), Headers = new() { ["X-LocalFUT15-Unknown"] = "1" } };
    }

    static JsonObject CreditsPayloadClub() => new()
    {
        ["clubId"] = (long)Cfg.ClubId, ["clubName"] = Cfg.ClubName, ["clubAbbr"] = Cfg.ClubAbbr, ["personaId"] = Cfg.PersonaId,
        ["personaName"] = Cfg.DisplayName, ["credits"] = St.GetCredits(),
    };
}




// ------------------------------------------------------------------ entry point

static class Fut15Program
{
    public static State State;
    public static void Init()
    {
        Directory.CreateDirectory(Cfg.Runtime);
        Log.Init(Path.Combine(Cfg.Runtime, "logs"));
        Cfg.Load();
        Catalog.Load();
        State = new State(Cfg.Runtime);
        State.LoadOrCreate();
        Console.WriteLine("============================================================");
        Console.WriteLine($" FUT REBORN (FIFA 16)  engine {Cfg.Version}");
        Console.WriteLine($" Club   : {Cfg.ClubName} / {Cfg.DisplayName} / {State.GetCredits()} coins / {State.GetPoints()} FIFA Points");
        Console.WriteLine($" Saves  : {Cfg.Runtime}");
        Console.WriteLine("============================================================");
    }
}
