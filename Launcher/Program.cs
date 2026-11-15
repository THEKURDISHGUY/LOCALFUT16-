using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Entry
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

class LauncherForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

    readonly WebView2 wv = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(2, 3, 4) };
    static readonly string Cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FUTRebornLauncher");
    string gamePath = "", serverDir = "";
    Process server; bool busy;

    const string Inject = @"
(function(){
 document.documentElement.style.overflow='hidden';document.body.style.overflow='hidden';
 var post=function(m){chrome.webview.postMessage(m)};
 var st=document.getElementById('stage'),rx=0,ry=0,lx=0,ly=0,spin=false;
 document.addEventListener('mousedown',function(e){
   if(e.button!==0)return; if(e.target.closest('button,.ctl i'))return;
   if(e.target.closest('.stage')){spin=true;lx=e.clientX;ly=e.clientY;
     var m=/rotateY\(([-\d.]+)deg\) rotateX\(([-\d.]+)deg\)/.exec(st.style.transform||'');ry=m?+m[1]:ry;rx=m?+m[2]:rx;e.preventDefault();return;}
   post('drag');
 });
 document.addEventListener('mousemove',function(e){ if(!spin)return; e.stopPropagation();
   ry+=(e.clientX-lx)*0.7; rx-=(e.clientY-ly)*0.7; lx=e.clientX; ly=e.clientY;
   st.style.transform='rotateY('+ry+'deg) rotateX('+rx+'deg)'; },true);
 document.addEventListener('mouseup',function(){spin=false},true);
 window.addEventListener('blur',function(){spin=false});
 var d=document.querySelectorAll('.ctl i'); 
 if(d[0]){d[0].style.cursor='pointer';d[0].onclick=function(){post('min')}}
 if(d[2]){d[2].style.cursor='pointer';d[2].onclick=function(){post('close')}}
 var msg=document.getElementById('msg'),pr=document.getElementById('prog'),bar=document.getElementById('bar'),pth=document.getElementById('path');
 document.getElementById('br').onclick=function(){post('browse')};
 document.getElementById('play').onclick=function(){
   if(!window.ok){msg.textContent='Select your fifa16.exe first';return}
   post('play');
 };
 window.__path=function(p){pth.textContent=p;pth.className='path ok';window.ok=true};
 window.__st=function(n,t){pr.className='prog on';bar.style.width=n+'%';msg.textContent=t};
 window.__end=function(t){msg.textContent=t};
})();";

    public LauncherForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(932, 592);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(2, 3, 4);
        Text = "FUT Reborn Launcher";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
        Controls.Add(wv);
        Load += async (_, __) =>
        {
            float k = DeviceDpi / 96f;
            ClientSize = new Size((int)Math.Ceiling(932 * k), (int)Math.Ceiling(592 * k));
            CenterToScreen();
            await Init();
        };
        FormClosing += (_, __) => StopServer();
    }

    async Task Init()
    {
        Directory.CreateDirectory(Cfg);
        try { foreach (var l in File.ReadAllLines(Path.Combine(Cfg, "config.txt"))) { if (l.StartsWith("game=")) gamePath = l[5..]; if (l.StartsWith("server=")) serverDir = l[7..]; } } catch { }
        if (!File.Exists(gamePath)) { gamePath = DetectGame(); if (gamePath != "") SaveCfg(); }
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Cfg, "wv"));
        await wv.EnsureCoreWebView2Async(env);
        var s = wv.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false; s.AreDevToolsEnabled = false; s.IsZoomControlEnabled = false; s.IsStatusBarEnabled = false;
        wv.CoreWebView2.WebMessageReceived += (_, e) => OnMsg(e.TryGetWebMessageAsString());
        wv.CoreWebView2.NavigationCompleted += async (_, __) =>
        {
            await wv.CoreWebView2.ExecuteScriptAsync(Inject);
            if (File.Exists(gamePath)) await Js("window.__path(" + JsonSerializer.Serialize(gamePath) + ")");
        };
        string html;
        using (var st = typeof(LauncherForm).Assembly.GetManifestResourceStream("ui.html")) using (var r = new StreamReader(st)) html = r.ReadToEnd();
        wv.CoreWebView2.NavigateToString(html);
    }

    Task Js(string code) => wv.CoreWebView2.ExecuteScriptAsync(code);
    Task Status(int n, string t) => Js($"window.__st({n},{JsonSerializer.Serialize(t)})");
    void SaveCfg() { try { File.WriteAllText(Path.Combine(Cfg, "config.txt"), "game=" + gamePath + "\nserver=" + serverDir + "\n"); } catch { } }

    async void OnMsg(string m)
    {
        switch (m)
        {
            case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); break;
            case "min": WindowState = FormWindowState.Minimized; break;
            case "close": Close(); break;
            case "browse":
                using (var d = new OpenFileDialog { Title = "Select your fifa16.exe", Filter = "FIFA 16 (fifa16.exe)|fifa16.exe|Executable (*.exe)|*.exe", FileName = "fifa16.exe" })
                    if (d.ShowDialog(this) == DialogResult.OK) { gamePath = d.FileName; SaveCfg(); await Js("window.__path(" + JsonSerializer.Serialize(gamePath) + ")"); }
                break;
            case "play": if (!busy) { busy = true; try { await Play(); } finally { busy = false; } } break;
        }
    }

    static bool PortOpen(int port)
    {
        try { using var c = new TcpClient(); return c.ConnectAsync("127.0.0.1", port).Wait(300); } catch { return false; }
    }

    static string DetectGame()
    {
        try
        {
            foreach (var key in new[] { @"SOFTWARE\WOW6432Node\EA Games\FIFA 16", @"SOFTWARE\EA Games\FIFA 16", @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\FIFA 16" })
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                {
                    var dir = k?.GetValue("Install Dir") as string;
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "fifa16.exe"))) return Path.Combine(dir, "fifa16.exe");
                }
        }
        catch { }
        var subs = new[] { @"FIFA 16", @"Games\FIFA 16", @"EA Games\FIFA 16", @"Origin Games\FIFA 16", @"Program Files\EA Games\FIFA 16", @"Program Files (x86)\EA Games\FIFA 16", @"Program Files\Origin Games\FIFA 16", @"Program Files (x86)\Origin Games\FIFA 16", @"Program Files (x86)\Steam\steamapps\common\FIFA 16", @"Program Files\EA Games\FIFA 16\Game" };
        foreach (var dr in DriveInfo.GetDrives())
        {
            if (dr.DriveType != DriveType.Fixed) continue;
            foreach (var sub in subs)
            {
                var p = Path.Combine(dr.Name, sub, "fifa16.exe");
                if (File.Exists(p)) return p;
            }
        }
        return "";
    }

    static readonly string[] HostNames = { "gosredirector.ea.com", "winter15.gosredirector.ea.com", "utas.s2.fut.ea.com", "utas.s3.fut.ea.com", "utas.fut.ea.com", "fut.ea.com", "spring14.gosredirector.ea.com" };

    static string EnsureHosts()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");
            try { var at = File.GetAttributes(path); if ((at & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, at & ~FileAttributes.ReadOnly); } catch { }
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var l = line.Split('#')[0].Trim(); if (l.Length == 0) continue;
                var p = l.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2 && p[0] == "127.0.0.1") for (int i = 1; i < p.Length; i++) have.Add(p[i]);
            }
            var add = HostNames.Where(h => !have.Contains(h)).ToList();
            if (add.Count == 0) return null;
            var sb = new System.Text.StringBuilder();
            if (text.Length > 0 && !text.EndsWith("\n")) sb.Append("\r\n");
            sb.Append("# FUT Reborn\r\n");
            foreach (var h in add) sb.Append("127.0.0.1 " + h + "\r\n");
            File.AppendAllText(path, sb.ToString());
            try { Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000); } catch { }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // returns (file, args, workingDir) or null. No questions asked.
    (string file, string args, string wd)? DetectServer()
    {
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(serverDir)) roots.Add(serverDir);
        var d = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\'));
        for (int i = 0; i < 4 && d != null; i++, d = d.Parent) roots.Add(d.FullName);
        var subs = new[] { "", "Server", @"Server\LocalFUT16", "LocalFUT16" };
        // 1) any folder name / any layout: look for the server exe near the launcher
        var baseDir = AppContext.BaseDirectory.TrimEnd('\\');
        var par = Directory.GetParent(baseDir);
        foreach (var (r, depth) in new[] { (baseDir, 4), (par?.FullName, 2) })
        {
            if (string.IsNullOrEmpty(r)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(r, "LocalFUT16.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true }))
                    return (f, "", Path.GetDirectoryName(f));
            }
            catch { }
        }
        foreach (var r in roots) foreach (var sub in subs)
        {
            var p = Path.Combine(r, sub); var exe = Path.Combine(p, "LocalFUT16.exe");
            if (File.Exists(exe)) return (exe, "", p);
        }
        // source-folder fallback (any folder name): a folder that contains LocalFUT16.csproj
        foreach (var (r, depth) in new[] { (baseDir, 3), (par?.FullName, 3) })
        {
            if (string.IsNullOrEmpty(r)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(r, "LocalFUT16.csproj", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true }))
                    return ("dotnet", "run -c Release", Path.GetDirectoryName(f));
            }
            catch { }
        }
        return null;
    }

    async Task Play()
    {
        if (!File.Exists(gamePath)) { await Js("window.__end('fifa16.exe not found - Browse again')"); return; }
        await Status(5, "Setting up connection...");
        var herr = EnsureHosts();
        if (herr != null) { await Js("window.__end(" + JsonSerializer.Serialize("Cannot edit hosts file (run as admin / pause antivirus): " + herr) + ")"); return; }
        await Status(8, "Starting FUT Reborn server...");
        if (!PortOpen(42230))
        {
            var found = DetectServer();
            if (found == null) { await Js("window.__end('Server files not found - keep the Server folder next to the launcher')"); return; }
            var sv = found.Value;
            try
            {
                var log = Path.Combine(Cfg, "server.log");
                var psi = new ProcessStartInfo(sv.file, sv.args) { WorkingDirectory = sv.wd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.Environment["FIFA16_DIR"] = Path.GetDirectoryName(gamePath);
                server = Process.Start(psi);
                var sw = new StreamWriter(log, false) { AutoFlush = true };
                DataReceivedEventHandler h = (_, e) => { if (e.Data != null) lock (sw) try { sw.WriteLine(e.Data); } catch { } };
                server.OutputDataReceived += h; server.ErrorDataReceived += h;
                server.BeginOutputReadLine(); server.BeginErrorReadLine();
            }
            catch (Exception ex) { await Js("window.__end(" + JsonSerializer.Serialize("Cannot start server: " + ex.Message) + ")"); return; }
            for (int i = 0; i < 600; i++)
            {
                if (PortOpen(42230)) break;
                if (server.HasExited)
                {
                    await Task.Delay(600);
                    string why = "Server failed";
                    try
                    {
                        using var fs = new FileStream(Path.Combine(Cfg, "server.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var lines = new StreamReader(fs).ReadToEnd().Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        var err = lines.FirstOrDefault(x => x.Contains("error", StringComparison.OrdinalIgnoreCase) || x.Contains("exception", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault() ?? "no output";
                        why = "Server failed: " + (err.Length > 110 ? err.Substring(0, 110) : err);
                    }
                    catch { }
                    await Js("window.__end(" + JsonSerializer.Serialize(why) + ")"); server = null; return;
                }
                await Status(10 + Math.Min(60, i / 2), "Starting FUT Reborn server...");
                await Task.Delay(200);
            }
            if (!PortOpen(42230)) { await Js("window.__end('Server did not start in time')"); StopServer(); return; }
        }
        await Status(75, "Syncing your club...");
        await Task.Delay(800);
        await Status(90, "Launching FIFA 16...");
        try { Process.Start(new ProcessStartInfo(gamePath) { WorkingDirectory = Path.GetDirectoryName(gamePath), UseShellExecute = true }); }
        catch (Exception ex) { await Js("window.__end(" + JsonSerializer.Serialize("Cannot start game: " + ex.Message) + ")"); return; }
        await Status(100, "Have fun! FIFA 16 is running");
    }

    void StopServer() { try { if (server != null && !server.HasExited) server.Kill(true); } catch { } }
}
