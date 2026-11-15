using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LocalFut15;

string ResolveFile(string rel)
{
    var roots = new List<string>();
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var d = new DirectoryInfo(start.TrimEnd('\\', '/'));
        for (int i = 0; i < 5 && d != null; i++, d = d.Parent) roots.Add(d.FullName);
    }
    var envHome = Environment.GetEnvironmentVariable("LOCALFUT_HOME");
    if (!string.IsNullOrWhiteSpace(envHome)) roots.Add(envHome.Trim());
    foreach (var r in roots)
        try { var p = Path.Combine(r, rel); if (File.Exists(p)) return p; } catch { }
    return Path.Combine(AppContext.BaseDirectory, rel);
}
string CertPfx   = ResolveFile(@"certs\leaf.pfx");
const string CertPass  = "kurdish";
string OrigKey   = ResolveFile("orig_gos2015.bin");
string ReplKey   = ResolveFile(@"certs\ca-modulus.bin");
string Log       = Path.Combine(AppContext.BaseDirectory, "capture.log");
object logLock = new();
void WriteLog(string text) { lock (logLock) { try { File.AppendAllText(Log, text); } catch { } } }
const int PatchAfterSeconds = 20;
const bool PatchImageCopy   = true;
const bool PatchRuntimeCopy = false;
const string Fifa16BlazeVer = "15.1.1.0.1";
const string Fifa16Cver     = "2904053";
const string Fifa16Svcn     = "fifa-2016-pc";
const string Fifa16ExeVer   = "16.0.2904053";
const int RedirectorPort = 42230;
const int BlazePort      = 10041;

if (args.Length >= 2 && args[0] == "--validate-players") { PlayersBuilder.Validate(args[1], args.Length > 2 ? args[2] : ""); return; }
if (args.Length >= 2 && args[0] == "--make-players") { PlayersBuilder.WriteCs(args[1], args.Length > 2 ? args[2] : ""); return; }

foreach (var need in new[] { CertPfx, OrigKey, ReplKey })
    if (!File.Exists(need)) { Console.WriteLine("error: required file not found: " + need + "  (keep the certs folder next to the server exe)"); return; }
var cert = new X509Certificate2(CertPfx, CertPass);
byte[] orig = File.ReadAllBytes(OrigKey);
byte[] repl = File.ReadAllBytes(ReplKey);
if (orig.Length != 256 || repl.Length != 256) { Console.WriteLine("Key files must be 256 bytes each."); return; }

string sverValue = Environment.GetEnvironmentVariable("LOCALFUT_SVER") ?? ("Blaze " + Fifa16BlazeVer);
string osdkVer   = Environment.GetEnvironmentVariable("LOCALFUT_OSDKVER") ?? Fifa16BlazeVer;
Console.WriteLine($"[CFG] SVER={sverValue}  OSDK_SERVER_VERSION={osdkVer}  service={Fifa16Svcn}");
byte[] PRE      = BuildPreAuth(sverValue);
byte[] CORECFG  = BuildCoreConfig(osdkVer);
byte[] EMPTYCFG = new Tdf().MapStrStr("CONF", Array.Empty<(string, string)>()).ToArray();
var Replay = new Dictionary<(int, int), byte[]>
{
    [(9, 8)]      = Convert.FromBase64String("0lslA4ZMswEKMTI3LjAuMC4xAIbr7gAAkpzhAQEAmps0AQstVUlPTi8qKioqAML8tAAAzks5AJjqAc5c8wELTE9DQUxGVVQxNQDOuXkBAQDPCPQAAADSmOsDhkyzAQoxMjcuMC4wLjEAwvy0AADOuXkBAQAA1yvwA9Lb8AAB1pkAAIKo1rkHAA=="),
    [(9, 5)]      = Convert.FromBase64String("hkyzAQoxMjcuMC4wLjEAhuvuAACSnOEBAjEAmps0AQEAsvjAAJPV8tYMuvvrAQEAwvy0AIScAc5LOQAAzlzzAQEAzrl5AQEAzwj0AADPSm0BAQA="),
    [(30722, 20)] = Convert.FromBase64String("kh0hA6N5pwAAxkh0A5IsMwAAuh00AADWLDMAAADWHTQAAADXOmQAgqjWuQc="),
    [(2249, 1)]   = Convert.FromBase64String("szz0BAMBpkAAAQtPX1RLZmlsdGVyALL45gAA0vnnAAAA"),
    [(2249, 2)]   = Convert.FromBase64String("snywBAMBpkAAAQpPX1NHX1RDS1IAszl0BAEBC09fVEtmaWx0ZXIAAA=="),
    [(9, 12)]     = Convert.FromBase64String("zthwBQEBAQ5GaXJzdFRpbWVGbGFnAAIwAA=="),
    [(1, 32)]     = Convert.FromBase64String("usz0BAMCkl2pAQEAnkh5AQEAnuhtARNGSUZBMTZQQ0JveENvbnRlbnQApkAAAAGnOO8AAMKZAACCqNa5B8KqZAEBAMMo4QAAwypkAQEAz0h0AAHPTKMAANIZwAEOT05MSU5FX0FDQ0VTUwDSSHkBAQDTnCUAAdY7tAAA2lyAAAAAkl2pAQEAnkh5AQEAnuhtAQlGSUZBMTVQQwCmQAAAAqc47wAAwpkAAIKo1rkHwqpkAQEAwyjhAADDKmQBAQDPSHQAAc9MowAA0hnAAQ5PTkxJTkVfQUNDRVNTANJIeQEBANOcJQAB1ju0AADaXIAAAAA="),
};
var LoginNotifs = new (int, int, byte[])[] {
    (30722, 2, Convert.FromBase64String("kh0hA6N5pwAAxkh0A5IsMwAAuh00AADWLDMAAADWHTQAAADXOXIDhpkAAIKo1rkHhsvjAJPV8tYMl4iiAgCXimQAAKZAAACCqNa5B7obZQEOVGhlS3VyZGlzaEd1eQC6HPABCmNlbV9lYV9pZAC/KmcAgqjWuQfCmSkAAAA=")),
    (30722, 8, Convert.FromBase64String("i1pkAIKo1rkHjnpkCYLgAwKCqNa5B5M7rQEOVGhlS3VyZGlzaEd1eQCuXkABJkxPQ0FMMTZUT0tFTi1GMjBDMTgzRThGOTAzMUMyNTczOEY0QzIAshz0AL62u6wNthpsARBsb2NhbEBsb2NhbGhvc3QAuhzwAQpjZW1fZWFfaWQAwpkAAIKo1rkHwsh0AADWmQAAgqjWuQfXPTAAAOMpZgAA")),
    (30722, 1, Convert.FromBase64String("kh0hA6N5pwAAxkh0A5IsMwAAuh00AADWLDMAAADWHTQAAADXOmQAgqjWuQc=")),
    (30722, 5, Convert.FromBase64String("msnzAAOmQAAAgqjWuQc="))
};


_ = Task.Run(PatchLoop);
_ = Serve(RedirectorPort, HandleRedirector);
_ = Serve(BlazePort, HandleBlaze);
_ = ServeHttp(42232, EaswHandler);
_ = ServeHttp(17502, QosHandler);
_ = ServeHttp(8080,  UtHandler);
Console.WriteLine("[FUT REBORN] persona " + UtBackend.Persona + ", club saved to save\\club.json");
Console.WriteLine($"Redirector on {RedirectorPort}, Blaze on {BlazePort}. Auto-patching fifa16.exe. Waiting for FIFA 16...");
await Task.Delay(-1);

async Task PatchLoop()
{
    var firstSeen = new Dictionary<int, DateTime>();
    var finished = new HashSet<int>();
    var waitMsg = new HashSet<int>();
    while (true)
    {
        foreach (var p in Process.GetProcessesByName("fifa16"))
        {
            if (finished.Contains(p.Id)) continue;
            if (!firstSeen.ContainsKey(p.Id))
            {
                firstSeen[p.Id] = DateTime.Now;
                try
                {
                    string? pv = p.MainModule?.FileVersionInfo.ProductVersion;
                    Console.WriteLine($"[PATCH] fifa16.exe product version {pv} (this server is built for {Fifa16ExeVer}){(pv == Fifa16ExeVer ? "" : "  <-- DIFFERENT build, key offset/replies may not match")}");
                }
                catch { }
            }
            try
            {
                double age = (DateTime.Now - p.StartTime).TotalSeconds;
                if (age < PatchAfterSeconds)
                {
                    if (!waitMsg.Contains(p.Id)) { waitMsg.Add(p.Id); Console.WriteLine($"[PATCH] fifa16 PID {p.Id} found; waiting until it has run {PatchAfterSeconds}s (do not open Ultimate Team yet)..."); }
                    continue;
                }
                int wantedPlaces = (PatchImageCopy ? 1 : 0) + (PatchRuntimeCopy ? 1 : 0);
                int places = TryPatch(p, orig, repl);
                if (places >= wantedPlaces) { finished.Add(p.Id); Console.WriteLine($"[PATCH] fifa16 PID {p.Id} patched ({places} places). Ready - open Ultimate Team."); }
                else if ((DateTime.Now - firstSeen[p.Id]).TotalMinutes > 10) finished.Add(p.Id);
            }
            catch (Exception ex) { Console.WriteLine("[PATCH] " + ex.Message); await Task.Delay(5000); }
        }
        await Task.Delay(2000);
    }
}


string GetRosterXml() =>
    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<squad>\n  <squadInfoSet>\n" +
    "    <squadInfo platform=\"pc64\">\n      <dbSchemaCRC>596426fe3525c25b90593b800b9ce5c3c61ccab5</dbSchemaCRC>\n" +
    "      <dbMajor>1</dbMajor><dbMinor>0</dbMinor>\n      <dbMajorCRC>0</dbMajorCRC><dbMinorCRC>0</dbMinorCRC>\n" +
    "      <dbMajorLoc></dbMajorLoc><dbMinorLoc></dbMinorLoc>\n      <dbFUTVer>1</dbFUTVer><dbFUTCRC>0</dbFUTCRC><dbFUTLoc></dbFUTLoc>\n    </squadInfo>\n" +
    "  </squadInfoSet>\n</squad>";

async Task ServeHttp(int port, Func<string, string, byte[], (int status, string ctype, byte[] body, Dictionary<string, string>? hdrs)> handler)
{
    var l = new TcpListener(IPAddress.Any, port);
    try { l.Start(); Console.WriteLine($"Listening on port {port} (HTTP)"); }
    catch (Exception ex) { Console.WriteLine($"[ERROR] cannot listen on port {port}: {ex.Message}"); return; }
    while (true)
    {
        var c = await l.AcceptTcpClientAsync();
        _ = Task.Run(async () =>
        {
            string who = $"{port}/{c.Client.RemoteEndPoint}";
            try
            {
                var ns = c.GetStream();
                ns.ReadTimeout = 15000;
                while (true)
                {
                    var buf = new List<byte>();
                    var tmp = new byte[65536];
                    int hdrEnd = -1;
                    while (hdrEnd < 0)
                    {
                        int n = await ns.ReadAsync(tmp);
                        if (n <= 0) return;
                        buf.AddRange(tmp.AsSpan(0, n).ToArray());
                        string s = Encoding.Latin1.GetString(buf.ToArray());
                        hdrEnd = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    }
                    string head = Encoding.Latin1.GetString(buf.ToArray(), 0, hdrEnd);
                    var lines = head.Split("\r\n");
                    var rl = lines[0].Split(' ');
                    string method = rl[0], target = rl.Length > 1 ? rl[1] : "/";
                    int clen = 0;
                    foreach (var h in lines.Skip(1))
                        if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(h[15..].Trim(), out clen);
                    int have = buf.Count - (hdrEnd + 4);
                    while (have < clen)
                    {
                        int n = await ns.ReadAsync(tmp);
                        if (n <= 0) break;
                        buf.AddRange(tmp.AsSpan(0, n).ToArray());
                        have = buf.Count - (hdrEnd + 4);
                    }
                    byte[] body = buf.Skip(hdrEnd + 4).Take(clen).ToArray();
                    string bodyTxt = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 600));
                    var (status, ctype, resp, hdrs) = handler(method, target, body);
                    Console.WriteLine($"[{who}] HTTP {method} {target} -> {status} ({resp.Length}B)" + (clen > 0 ? $"  body: {bodyTxt}" : ""));
                    WriteLog($"{DateTime.Now:O} HTTP:{port} {method} {target} body={bodyTxt}\n\n");
                    var sb = new StringBuilder();
                    sb.Append($"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 404 ? "Not Found" : "Status")}\r\nContent-Type: {ctype}\r\nContent-Length: {resp.Length}\r\nConnection: close\r\n");
                    if (hdrs != null) foreach (var kv in hdrs) sb.Append($"{kv.Key}: {kv.Value}\r\n");
                    sb.Append("\r\n");
                    await ns.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
                    await ns.WriteAsync(resp);
                    await ns.FlushAsync();
                    return;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[{who}] http ended: {ex.Message}"); }
            finally { c.Close(); }
        });
    }
}

(int, string, byte[], Dictionary<string, string>?) XmlRes(string xml, int status = 200, string ct = "application/xml; charset=utf-8", Dictionary<string, string>? h = null)
    => (status, ct, Encoding.UTF8.GetBytes(xml), h);

(int, string, byte[], Dictionary<string, string>?) EaswHandler(string method, string target, byte[] body)
{
    int qi = target.IndexOf('?');
    string path = (qi >= 0 ? target[..qi] : target).ToLowerInvariant();
    if (method == "POST")
    {
        if (path == "/extension" || path.EndsWith("/extension"))
            return XmlRes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<result/>\n", 200, "text/xml",
                new() { ["EASW-Session"] = "fut16-local-easw-session", ["EASW-Token"] = "fut16-local-easw-token" });
        return XmlRes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<result/>\n", 200, "text/xml");
    }
    if (path.EndsWith("/roster")) return XmlRes(GetRosterXml(), 200, "text/xml");
    return XmlRes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<result/>\n", 200, "text/xml");
}

(int, string, byte[], Dictionary<string, string>?) QosHandler(string method, string target, byte[] body)
{
    int qi = target.IndexOf('?');
    string path = (qi >= 0 ? target[..qi] : target).ToLowerInvariant();
    var q = new Dictionary<string, string>();
    if (qi >= 0) foreach (var kv in target[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)) { var p = kv.Split('=', 2); q[p[0]] = p.Length > 1 ? p[1] : ""; }
    const long ip = 16777343;
    var nostore = new Dictionary<string, string> { ["Cache-Control"] = "no-store" };
    if (path == "/qos/qos")
    {
        string qtyp = q.TryGetValue("qtyp", out var t) ? t : "1";
        return qtyp == "2"
            ? XmlRes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><qos><numprobes>10</numprobes><qosport>17502</qosport><probesize>1200</probesize><qosip>{ip}</qosip><requestid>1234</requestid><reqsecret>5678</reqsecret></qos>", 200, "application/xml; charset=utf-8", nostore)
            : XmlRes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><qos><numprobes>0</numprobes><qosport>17502</qosport><probesize>0</probesize><qosip>{ip}</qosip><requestid>1</requestid><reqsecret>0</reqsecret></qos>", 200, "application/xml; charset=utf-8", nostore);
    }
    if (path == "/qos/firewall")
        return XmlRes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><firewall><ips><ips>{ip}</ips><ips>{ip}</ips></ips><numinterfaces>2</numinterfaces><ports><ports>17502</ports><ports>17502</ports></ports><requestid>1234</requestid><reqsecret>5678</reqsecret></firewall>", 200, "application/xml; charset=utf-8", nostore);
    if (path == "/qos/firetype")
        return XmlRes("<?xml version=\"1.0\" encoding=\"utf-8\"?><firetype><firetype>2</firetype></firetype>", 200, "application/xml; charset=utf-8", nostore);
    return XmlRes("not found", 404, "text/plain", nostore);
}

(int, string, byte[], Dictionary<string, string>?) UtHandler(string method, string target, byte[] body)
    => UtBackend.Handle(method, target, body);

async Task Serve(int port, Func<SslStream, string, Task> handler)
{
    var l = new TcpListener(IPAddress.Any, port);
    try { l.Start(); Console.WriteLine($"Listening on port {port}"); }
    catch (Exception ex) { Console.WriteLine($"[ERROR] cannot listen on port {port}: {ex.Message}"); return; }
    while (true)
    {
        var c = await l.AcceptTcpClientAsync();
        _ = Task.Run(async () =>
        {
            string who = $"{port}/{c.Client.RemoteEndPoint}";
            try
            {
                using var ssl = new SslStream(c.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12
                });
                Console.WriteLine($"[{who}] TLS OK");
                await handler(ssl, who);
            }
            catch (Exception ex) { Console.WriteLine($"[{who}] ended: {ex.Message}"); }
            finally { c.Close(); }
        });
    }
}

async Task HandleRedirector(SslStream ssl, string who)
{
    var buf = new byte[32768];
    int len = 0;
    while (true)
    {
        int n = await ssl.ReadAsync(buf.AsMemory(len));
        if (n == 0) return;
        len += n;
        int he = buf.AsSpan(0, len).IndexOf("\r\n\r\n"u8);
        if (he < 0) continue;
        string headers = Encoding.ASCII.GetString(buf, 0, he);
        int cl = 0;
        foreach (var line in headers.Split("\r\n"))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                cl = int.Parse(line[15..].Trim());
        if (len - (he + 4) < cl) continue;

        Console.WriteLine($"[{who}] {headers.Split("\r\n")[0]}");
        string xml = """
<?xml version="1.0" encoding="UTF-8"?>
<serverinstanceinfo>
  <address member="0">
    <valu>
      <hostname>127.0.0.1</hostname>
      <ip>2130706433</ip>
      <port>10041</port>
    </valu>
  </address>
  <secure>1</secure>
  <trialservicename></trialservicename>
  <defaultdnsaddress>0</defaultdnsaddress>
</serverinstanceinfo>
""";
        byte[] body = Encoding.UTF8.GetBytes(xml);
        string head = $"HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await ssl.WriteAsync(Encoding.ASCII.GetBytes(head));
        await ssl.WriteAsync(body);
        await ssl.FlushAsync();
        Console.WriteLine($"[{who}] replied: connect to 127.0.0.1:{BlazePort}");
        return;
    }
}

async Task HandleBlaze(SslStream ssl, string who)
{
    var acc = new List<byte>();
    var b = new byte[65536];
    int mmSession = 0;
    int mmCycle = 0;
    while (true)
    {
        int n = await ssl.ReadAsync(b);
        if (n <= 0) break;
        acc.AddRange(b.AsSpan(0, n).ToArray());
        while (acc.Count >= 16)
        {
            int payload = (acc[0] << 24) | (acc[1] << 16) | (acc[2] << 8) | acc[3];
            int meta = (acc[4] << 8) | acc[5];
            if (payload > 16777216 || meta > 1048576) { Console.WriteLine("bad frame, dropping buffer"); acc.Clear(); break; }
            int total = 16 + payload + meta;
            if (acc.Count < total) break;
            var pkt = acc.GetRange(0, total).ToArray();
            acc.RemoveRange(0, total);
            int comp = (pkt[6] << 8) | pkt[7];
            int cmd = (pkt[8] << 8) | pkt[9];
            int msg = (pkt[10] << 16) | (pkt[11] << 8) | pkt[12];
            int type = pkt[13] >> 5;
            string hex = BitConverter.ToString(pkt);
            WriteLog($"{DateTime.Now:O} BLAZE c=0x{comp:X4} cmd=0x{cmd:X4} msg={msg} type={type}\n{hex}\n\n");

            if (type == 4) { var pr = Build(0, 0, msg, 5, Array.Empty<byte>()); await ssl.WriteAsync(pr); await ssl.FlushAsync(); continue; }
            if (type != 0) { Console.WriteLine($"[{who}] (type {type}) c=0x{comp:X4} cmd=0x{cmd:X4} msg={msg} req={payload}B"); continue; }

            string name; byte[] reply; bool mmCancelled = false, gameReport = false;
            if (comp == 9 && cmd == 7) { name = "Util.PreAuth"; reply = PRE; }
            else if (comp == 9 && cmd == 2) { name = "Util.Ping"; reply = BuildPing(); }
            else if (comp == 9 && cmd == 1)
            {
                string t = Encoding.Latin1.GetString(pkt, 16, payload);
                if (t.Contains("OSDK_CORE")) { name = "Util.FetchClientConfig OSDK_CORE (FIFA16 config)"; reply = CORECFG; }
                else { name = "Util.FetchClientConfig"; reply = EMPTYCFG; }
            }
            else if (comp == 1 && (cmd == 0x0A || cmd == 0x46 || cmd == 0x28 || cmd == 0x32 || cmd == 0x3C || cmd == 0x98))
            {
                string mode = Environment.GetEnvironmentVariable("LOCALFUT_AUTH46") ?? "login16";
                if (mode == "empty" || cmd == 0x46) reply = Array.Empty<byte>();
                else reply = BuildLogin();
                name = $"Authentication 0x{cmd:X2} (mode={mode})";
            }
            else if (comp == 9 && cmd == 10)
            {
                string ut = Encoding.Latin1.GetString(pkt, 16, payload);
                string ukey = ut.Contains("AchievementCache") ? "AchievementCache" : ut.Contains("FirstTimeFlag") ? "FirstTimeFlag" : "";
                name = "Util.UserSettingsLoad " + ukey; reply = new Tdf().Str("DATA", "").Str("KEY", ukey).ToArray();
            }
            else if (comp == 4 && cmd == 0x000D)
            {
                mmSession++;
                name = $"GameManager.startMatchmaking (MSID {mmSession})";
                reply = new Tdf().Int("MSID", mmSession).ToArray();
            }
            else if (comp == 4 && cmd == 0x000E)
            {
                name = "GameManager.cancelMatchmaking";
                reply = Array.Empty<byte>();
                mmCancelled = true;
            }
            else if (comp == 0x1C && (cmd == 1 || cmd == 2 || cmd == 3))
            {
                Console.WriteLine($"[GAMEREPORT] c=0x001C cmd={cmd} (full time) -> acknowledging + report-finished notify");
                name = "GameReporting.SubmitOffline"; reply = Array.Empty<byte>(); gameReport = true;
            }
            else if (Replay.TryGetValue((comp, cmd), out var rp)) { name = $"replay c=0x{comp:X4} cmd=0x{cmd:X4}"; reply = rp; }
            else { name = "UNHANDLED (empty reply)"; reply = Array.Empty<byte>(); }

            var frame = Build(comp, cmd, msg, 1, reply);
            await ssl.WriteAsync(frame);
            await ssl.FlushAsync();
            Console.WriteLine($"[{who}] c=0x{comp:X4} cmd=0x{cmd:X4} msg={msg} req={payload}B -> {name}, reply={reply.Length}B");
            if (gameReport)
            {
                long rid = 0;
                try { rid = LocalFut15.Tdf.GetIntLast(pkt.AsSpan(16, payload).ToArray(), "GRID") ?? 0; } catch { }
                var gnp = LocalFut15.Blaze.GameReportingResult(rid);
                await Task.Delay(20);
                await ssl.WriteAsync(Build(28, 114, 0, 2, gnp));
                await ssl.FlushAsync();
                Console.WriteLine($"   [NOTIFY] game report finished -> c=28 cmd=114 reportId={rid} ({gnp.Length}B)");
            }
            if (mmCancelled)
            {
                ushort[] cands = { 0x000A, 0x0066, 0x0067, 0x000C, 0x0064, 0x000B };
                string mt = "auto";
                try { var mf = Path.Combine(AppContext.BaseDirectory, "mm_try.txt"); if (File.Exists(mf)) mt = File.ReadAllText(mf).Trim(); } catch { }
                int ncmd;
                if (mt.Equals("auto", StringComparison.OrdinalIgnoreCase)) ncmd = cands[(mmCycle++) % cands.Length];
                else { try { ncmd = Convert.ToInt32(mt.Replace("0x", ""), 16); } catch { ncmd = 0x000A; } }
                var nf = new Tdf().Int("MAXF", 0).Int("MSID", mmSession).Int("USID", 1000000002).ToArray();
                await ssl.WriteAsync(Build(4, ncmd, 0, 2, nf));
                await ssl.FlushAsync();
                Console.WriteLine($"   [NOTIFY] matchmaking cancelled -> sent GameManager notify cmd=0x{ncmd:X4} (MSID {mmSession}). Did the loading logo go away?");
            }
            if (comp == 1 && cmd == 0x0A)
            {
                foreach (var (nc, nk, nd) in LoginNotifs)
                {
                    await ssl.WriteAsync(Build(nc, nk, 0, 2, nd));
                    await ssl.FlushAsync();
                    Console.WriteLine($"   [NOTIFY] c={nc} cmd={nk} {nd.Length}B");
                }
            }
            if (payload > 0)
                Console.WriteLine("   request strings: " + Strs(pkt, 16));
            if (comp == 9 && cmd == 7)
            {
                string t0 = Encoding.Latin1.GetString(pkt, 16, payload);
                Console.WriteLine($"   [VER] client Blaze SDK {Fifa16BlazeVer}: {(t0.Contains(Fifa16BlazeVer) ? "match" : "DIFFERENT")}, CVER {Fifa16Cver}: {(t0.Contains(Fifa16Cver) ? "match" : "DIFFERENT")}, service {Fifa16Svcn}: {(t0.Contains(Fifa16Svcn) ? "match" : "DIFFERENT")}");
            }
            if (comp == 9 && cmd == 7 || comp == 1)
                Console.WriteLine("   request hex: " + hex);
            if (name.StartsWith("UNHANDLED"))
                Console.WriteLine("   request hex: " + (hex.Length > 700 ? hex[..700] + "..." : hex));
        }
    }
    Console.WriteLine($"[{who}] Blaze client disconnected");
}

static byte[] BuildPing()
{
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    return new Tdf().Int("STIM", now).Int("TIME", now).ToArray();
}

static byte[] BuildPreAuth(string sver)
{
    var t = new Tdf();
    t.Str("ASRC", "303107");
    t.IntList("CIDS", new long[] { 1, 9, 35, 30722 });
    t.Open("CONF");
    t.MapStrStr("CONF", new (string, string)[]
    {
        ("connIdleTimeout", "120s"), ("defaultRequestTimeout", "80s"), ("pingPeriod", "20s"),
        ("voipHeadsetUpdateRate", "1000"), ("xlspConnectionIdleTimeout", "300"),
        ("blazeServerClientId", "FOS-SERVER"), ("blazeSdkClientId", "FOS-SERVER"), ("blazeSdkClientSecret", ""),
        ("nucleusConnect", "http://127.0.0.1:8080/"), ("identityRedirectUri", "http://127.0.0.1:8080/localfut/callback"),
        });
    t.Close();
    t.Int("EEFA", 1);
    t.Str("ESRC", "303107");
    t.Str("INST", Fifa16Svcn);
    t.Int("MAID", 89);
    t.Int("MINR", 0);
    t.Str("NASP", "cem_ea_id");
    t.Str("PILD", "");
    t.Str("PLAT", "pc");
    t.Open("QOSS");
      t.Open("BWPS"); t.Str("PSA", "127.0.0.1"); t.Int("PSP", 17502); t.Str("SNA", "qos"); t.Close();
      t.Int("LNP", 10);
      t.MapStrStructBegin("LTPS", 1); t.Key("ea-sjc"); t.Str("PSA", "127.0.0.1"); t.Int("PSP", 17502); t.Str("SNA", "ea-sjc"); t.Close();
      t.Int("SVID", 1161889797);
      t.Int("TIME", 5000);
    t.Close();
    t.Str("RSRC", "303107");
    t.Str("SVER", sver);
    return t.ToArray();
}

static byte[] BuildCoreConfig(string osdkVer)
{
    const string UT = "http://127.0.0.1:8080/ut/";
    const string H  = "http://127.0.0.1:8080";
    const string S  = "http://127.0.0.1:42232";
    var cfg = new (string, string)[]
    {
        ("AUTH_TYPE", "LOCAL"),
        ("CONTENT_URL", S + "/content"), ("CTL_UPDATE_URL", S + "/ctl"),
        ("FIFA_POW_CONTENT_SERVER_URL", H + "/"), ("FIFA_POW_NUCLEUS_PROXY_URL", H + "/"), ("FIFA_POW_URL", H + "/"),
        ("FUT_URI", UT), ("FUT_RS4_BASE_URL", UT), ("FUTBOOTCFGFILE_URL", UT), ("FUT/ENABLED", "1"),
        ("FUTDYNAMICMESSAGES_CUSTOMURL", "127.0.0.1:8080"), ("FUTDYNAMICMESSAGES_URL_BASE", H),
        ("NUCLEUS_LOGIN_ENABLED", "0"),
        ("nucleusHost", "127.0.0.1"), ("nucleusPort", "8080"), ("nucleusPortSSL", "8080"), ("nucleusBaseUri", "/"),
        ("environment", "prod"), ("useNucleusSSL", "0"),
        ("ONLINE/FUTDYNAMICMESSAGES_CUSTOMURL", "127.0.0.1:8080"), ("ONLINE/FUTDYNAMICMESSAGES_TUTORIAL_MSG_URL", H + "/"),
        ("ONLINE/SERVER_RS4", H), ("FIFA_RS4_URL", H), ("FIFA_RS4_TIMEOUT", "30"),
        ("ORIGIN_LOGIN_ENABLED", "0"), ("OSDK_AUTH_REQUIRED", "0"), ("OSDK_ONLINE_ENABLED", "1"),
        ("OSDK_SERVER_VERSION", osdkVer),
        ("CLIENT_TIMEOUT", "90"), ("REQUEST_TIMEOUT", "80"), ("AUTOLOGIN", "1"), ("ALLOW_OFFLINE", "1"), ("EMAIL_OPT_IN", "0"),
        ("EASW/ENABLED", "1"),
        ("OSDK_EASW_ALLOWED_LOCALES", "en_US,en_GB,en_US.UTF-8,enUS,enGB,fr_FR,de_DE,es_ES,it_IT,pt_BR,nl_NL"),
        ("OSDK_EASW_CONNECT_RETRY_PERIOD", "30"),
        ("OSDK_EASW_AUTH_URL", S + "/easw/auth"), ("OSDK_EASW_EVENT_URL", S + "/easw/event"),
        ("OSDK_EASW_MEDIA_URL", S + "/easw/media"), ("OSDK_EASW_REQ_URL", S + "/easw/req"),
        ("ROSTER_URL", S + "/roster"), ("ROSTERUPDATE_URL", S + "/roster"), ("ROUTINGCFGFILE_URL", S + "/routing"),
        ("USE_TOKEN_AUTH", "0"),
        ("FUT_AUTH_URL", UT + "auth"), ("FUT_HUB_URL", UT + "game/fifa16/hub"),
        ("UT_AUTH_URL", UT + "auth"),  ("UT_HUB_URL", UT + "game/fifa16/hub"),
        ("FUT_OFFLINE_MODE", "0"),
        ("UTAS_BASE_URL", UT), ("UTAS_AUTH_URL", UT + "auth"),
    };
    return new Tdf().MapStrStr("CONF", cfg).Str("ID", "OSDK_CORE").Str("ITYP", "").Str("TOKN", "").ToArray();
}

static byte[] BuildLogin()
{
    const string key = "LOCAL16TOKEN-F20C183E8F9031C25738F4C2";
    var t = new Tdf();
    t.Int("CNTX", 0).Int("ERRC", 0).Str("SKEY", key).Int("ANON", 0).Int("NTOS", 0);
    t.Open("SESS");
      t.Int("1CON", 0).Int("BUID", 1000000002).Int("FRST", 0).Str("KEY", key);
      t.Int("LLOG", DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Str("MAIL", "local@localhost");
      t.Open("PDTL");
        t.Str("DSNM", "TheKurdishGuy").Int("LAST", 0).Int("PID", 1000000002);
        t.Int("PLAT", 4);
        t.Int("STAS", 0).Int("XREF", 1000000001);
      t.Close();
      t.Int("UID", 1000000001);
    t.Close();
    t.Int("SPAM", 0).Int("UNDR", 0);
    return t.ToArray();
}

static byte[] Build(int comp, int cmd, int msg, int type, byte[] payload)
{
    var r = new byte[16 + payload.Length];
    r[0] = (byte)(payload.Length >> 24); r[1] = (byte)(payload.Length >> 16);
    r[2] = (byte)(payload.Length >> 8);  r[3] = (byte)payload.Length;
    r[6] = (byte)(comp >> 8); r[7] = (byte)comp;
    r[8] = (byte)(cmd >> 8);  r[9] = (byte)cmd;
    r[10] = (byte)(msg >> 16); r[11] = (byte)(msg >> 8); r[12] = (byte)msg;
    r[13] = (byte)(type << 5);
    Array.Copy(payload, 0, r, 16, payload.Length);
    return r;
}

static string Strs(byte[] b, int from)
{
    var sb = new StringBuilder(); var cur = new StringBuilder();
    for (int i = from; i < b.Length; i++)
    {
        char ch = (char)b[i];
        if (ch >= 32 && ch < 127) cur.Append(ch);
        else { if (cur.Length >= 3) sb.Append(cur).Append(" | "); cur.Clear(); }
    }
    if (cur.Length >= 3) sb.Append(cur);
    return sb.ToString();
}

static int TryPatch(Process p, byte[] orig, byte[] repl, bool patchImage = PatchImageCopy, bool patchRuntime = PatchRuntimeCopy)
{
    IntPtr h = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, p.Id);
    if (h == IntPtr.Zero) throw new Exception("OpenProcess failed - run as Administrator");
    try
    {
        bool Wanted(ulong a) => a >= 0x100000000UL ? patchImage : patchRuntime;
        foreach (var a in Scan(h, orig))
        {
            if (!Wanted(a)) { Console.WriteLine($"[PATCH] skipped copy @0x{a:X}"); continue; }
            VirtualProtectEx(h, (IntPtr)a, (UIntPtr)orig.Length, 0x40, out uint old);
            bool ok = WriteProcessMemory(h, (IntPtr)a, repl, repl.Length, out int w);
            VirtualProtectEx(h, (IntPtr)a, (UIntPtr)orig.Length, old, out _);
            Console.WriteLine($"[PATCH] wrote {w} bytes @0x{a:X}: {(ok ? "SUCCESS" : "FAILED")}");
        }
        return Scan(h, repl).Count(Wanted);
    }
    finally { CloseHandle(h); }
}

static List<ulong> Scan(IntPtr h, byte[] pat)
{
    var hits = new List<ulong>();
    ulong addr = 0, end = 0x7FFFFFFFFFFF;
    while (addr < end && VirtualQueryEx(h, (IntPtr)addr, out var m, (UIntPtr)Marshal.SizeOf<MBI>()) != UIntPtr.Zero)
    {
        ulong rs = m.BaseAddress, re = m.BaseAddress + m.RegionSize;
        if (m.State == 0x1000 && m.Protect != 0 && (m.Protect & 0x101) == 0 && re > rs)
        {
            ulong off = rs;
            while (off < re)
            {
                int chunk = (int)Math.Min(re - off, 16UL * 1024 * 1024);
                var buf = new byte[chunk];
                if (ReadProcessMemory(h, (IntPtr)off, buf, chunk, out int rd) && rd >= pat.Length)
                {
                    int s = 0;
                    while (s <= rd - pat.Length)
                    {
                        int i = buf.AsSpan(s, rd - s).IndexOf(pat);
                        if (i < 0) break;
                        hits.Add(off + (ulong)(s + i));
                        s += i + 1;
                    }
                }
                if (off + (ulong)chunk >= re) break;
                off += (ulong)chunk - (ulong)(pat.Length - 1);
            }
        }
        addr = m.BaseAddress + m.RegionSize;
    }
    return hits;
}

[DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool i, int pid);
[DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out int r);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int n, out int w);
[DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualProtectEx(IntPtr h, IntPtr a, UIntPtr n, uint p, out uint old);
[DllImport("kernel32.dll")] static extern UIntPtr VirtualQueryEx(IntPtr h, IntPtr a, out MBI m, UIntPtr l);
[StructLayout(LayoutKind.Sequential)]
struct MBI { public ulong BaseAddress, AllocationBase; public uint AllocationProtect, _a1; public ulong RegionSize; public uint State, Protect, Type, _a2; }

sealed class Tdf
{
    readonly List<byte> o = new();
    public byte[] ToArray() => o.ToArray();
    void Tag(string t, int type)
    {
        int v = 0;
        for (int i = 0; i < 4; i++) v = (v << 6) | (i < t.Length ? ((t[i] - 32) & 0x3F) : 0);
        o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); o.Add((byte)type);
    }
    void VarInt(long val)
    {
        if (val < 0x40) { o.Add((byte)val); return; }
        o.Add((byte)(0x80 | (val & 0x3F))); val >>= 6;
        while (val >= 0x80) { o.Add((byte)(0x80 | (val & 0x7F))); val >>= 7; }
        o.Add((byte)val);
    }
    void RawStr(string s) { var b = Encoding.UTF8.GetBytes(s); VarInt(b.Length + 1); o.AddRange(b); o.Add(0); }
    public Tdf Int(string t, long v) { Tag(t, 0); VarInt(v); return this; }
    public Tdf Str(string t, string s) { Tag(t, 1); RawStr(s); return this; }
    public Tdf Open(string t) { Tag(t, 3); return this; }
    public Tdf Close() { o.Add(0); return this; }
    public Tdf IntList(string t, long[] vals) { Tag(t, 4); o.Add(0); VarInt(vals.Length); foreach (var v in vals) VarInt(v); return this; }
    public Tdf MapStrStr(string t, (string, string)[] items)
    {
        Tag(t, 5); o.Add(1); o.Add(1); VarInt(items.Length);
        foreach (var (k, v) in items) { RawStr(k); RawStr(v); }
        return this;
    }
    public Tdf MapStrStructBegin(string t, int n) { Tag(t, 5); o.Add(1); o.Add(3); VarInt(n); return this; }
    public Tdf Key(string k) { RawStr(k); return this; }
}
