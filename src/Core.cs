// AmbientLightCalibrate — ambient-light-driven monitor brightness for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmbientLightCalibrate
{
    /// <summary>Settings and per-monitor brightness maps, stored as JSON in %APPDATA%\AmbientLightCalibrate.</summary>
    public sealed class Config
    {
        public static readonly string Home = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmbientLightCalibrate");
        public static string PathCfg => System.IO.Path.Combine(Home, "config.json");
        public static string PathLog => System.IO.Path.Combine(Home, "log.txt");
        JsonObject _j;

        public static Config Load()
        {
            Directory.CreateDirectory(Home);
            var c = new Config();
            try { c._j = JsonNode.Parse(File.ReadAllText(PathCfg)) as JsonObject; } catch { }
            if (c._j == null && ImportLegacy() is JsonObject legacy) { c._j = legacy; c.Defaults(); c.Save(); }
            c._j ??= new JsonObject();
            c.Defaults();
            return c;
        }

        // earlier builds: %APPDATA%\LuxTray\config.json (only if it is ours: has a "curve"),
        // or ambient_config.json from the Python prototype next to (or above) the exe
        static JsonObject ImportLegacy()
        {
            try
            {
                var old = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuxTray", "config.json");
                if (File.Exists(old) && JsonNode.Parse(File.ReadAllText(old)) is JsonObject o && o.ContainsKey("curve")) return o;
            }
            catch { }
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var f = System.IO.Path.Combine(d.FullName, "ambient_config.json");
                if (File.Exists(f)) try { return JsonNode.Parse(File.ReadAllText(f)) as JsonObject; } catch { }
            }
            return null;
        }

        void Defaults()
        {
            void Def(string k, JsonNode v) { if (!_j.ContainsKey(k)) _j[k] = v; }
            Def("interval_s", 300);
            Def("curve", JsonNode.Parse("[[2,100],[10,120],[50,140],[150,160],[400,185],[1000,220]]"));
            Def("hysteresis_pct", 6);
            Def("max_step_nits", 40);
            Def("monitors", new JsonObject());
            Def("offset_nits", 0);
            Def("tray_auto", false);
            Def("argyll_bin", "");
            Def("instrument", 1);
            Def("display_type", "l");
            _j["hdr_skip"] = "all";      // never change brightness of a monitor that is in HDR mode
        }

        public void Save() =>
            File.WriteAllText(PathCfg, _j.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        double D(string k) => _j[k].GetValue<double>();
        public int IntervalS { get => (int)D("interval_s"); set => _j["interval_s"] = value; }
        public double Hysteresis { get => D("hysteresis_pct"); set => _j["hysteresis_pct"] = value; }
        public double MaxStep { get => D("max_step_nits"); set => _j["max_step_nits"] = value; }
        public double Offset { get => D("offset_nits"); set => _j["offset_nits"] = value; }
        public bool Auto { get => _j["tray_auto"].GetValue<bool>(); set => _j["tray_auto"] = value; }
        public int Instrument => (int)D("instrument");
        public string DisplayType => _j["display_type"].GetValue<string>();
        string _argyll;
        public string ArgyllSetting { get => _j["argyll_bin"].GetValue<string>(); set { _j["argyll_bin"] = value; _argyll = null; } }
        public string ArgyllBin => _argyll ??= Argyll.Find(ArgyllSetting);

        public List<(double lux, double nits)> Curve =>
            _j["curve"].AsArray().Select(p => (p[0].GetValue<double>(), p[1].GetValue<double>())).OrderBy(p => p.Item1).ToList();
        public void SetCurve(IEnumerable<(double lux, double nits)> pts)
        {
            var a = new JsonArray();
            foreach (var p in pts.OrderBy(p => p.lux)) a.Add(new JsonArray(p.lux, p.nits));
            _j["curve"] = a;
        }

        /// <summary>Brightness (VCP 0x10) → measured nits; null if not calibrated or disabled.</summary>
        public List<(double s, double n)> Map(string model)
        {
            var m = _j["monitors"]?[model];
            if (m?["map"] == null || (m["enabled"] != null && !m["enabled"].GetValue<bool>())) return null;
            return m["map"].AsArray().Select(p => (p[0].GetValue<double>(), p[1].GetValue<double>())).OrderBy(p => p.Item1).ToList();
        }

        public void SetMap(string model, List<(double s, double n)> map)
        {
            var mons = _j["monitors"].AsObject();
            var arr = new JsonArray();
            foreach (var (s, n) in map) arr.Add(new JsonArray(s, Math.Round(n, 2)));
            mons[model] = new JsonObject { ["enabled"] = true, ["map"] = arr };
        }

        public double TargetNits(double lux)
        {
            var c = Curve; double lx = Math.Log10(Math.Max(lux, 0.1));
            if (lx <= Math.Log10(c[0].lux)) return c[0].nits + Offset;
            for (int i = 1; i < c.Count; i++)
                if (lx <= Math.Log10(c[i].lux))
                {
                    double t = (lx - Math.Log10(c[i - 1].lux)) / (Math.Log10(c[i].lux) - Math.Log10(c[i - 1].lux));
                    return c[i - 1].nits + t * (c[i].nits - c[i - 1].nits) + Offset;
                }
            return c[^1].nits + Offset;
        }

        public static int SettingFor(double nits, List<(double s, double n)> map)
        {
            var m = map.OrderBy(p => p.n).ToList();
            if (nits <= m[0].n) return (int)m[0].s;
            if (nits >= m[^1].n) return (int)m[^1].s;
            for (int i = 1; i < m.Count; i++)
                if (nits <= m[i].n)
                    return (int)Math.Round(m[i - 1].s + (nits - m[i - 1].n) / (m[i].n - m[i - 1].n) * (m[i].s - m[i - 1].s));
            return (int)m[^1].s;
        }

        public static double NitsFor(double setting, List<(double s, double n)> map)
        {
            for (int i = 1; i < map.Count; i++)
                if (setting <= map[i].s)
                    return map[i - 1].n + (setting - map[i - 1].s) / Math.Max(map[i].s - map[i - 1].s, 1) * (map[i].n - map[i - 1].n);
            return map[^1].n;
        }
    }

    public static class Log
    {
        public static void Write(string msg)
        {
            try { File.AppendAllText(Config.PathLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}\n", Encoding.UTF8); } catch { }
        }
    }

    /// <summary>A connected monitor; Physical is a DDC/CI handle (release with Ddc.Release).</summary>
    public sealed class Monitor
    {
        public string Model, Device, Name;
        public IntPtr Physical;
        public uint Cur, Max;
        public bool HasDdc;
    }

    /// <summary>DDC/CI brightness through dxva2.dll.</summary>
    public static class Ddc
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        delegate bool EnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PHYSICAL_MONITOR[] a);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitor(IntPtr h);
        [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr h, byte code, uint value);

        public static List<Monitor> Enumerate()
        {
            var list = new List<Monitor>();
            var names = DisplayInfo.FriendlyNames();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                GetMonitorInfo(hMon, ref mi);
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                string model = "?";
                if (EnumDisplayDevices(mi.szDevice, 0, ref dd, 0))
                {
                    var m = Regex.Match(dd.DeviceID ?? "", @"MONITOR\\([A-Z0-9]+)\\");
                    model = m.Success ? m.Groups[1].Value : dd.DeviceString;
                }
                var mon = new Monitor { Model = model, Device = mi.szDevice, Name = names.TryGetValue(model, out var nm) && nm.Length > 0 ? nm : model };
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint n) && n > 0)
                {
                    var arr = new PHYSICAL_MONITOR[n];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, n, arr))
                    {
                        mon.Physical = arr[0].h;
                        for (int i = 1; i < n; i++) DestroyPhysicalMonitor(arr[i].h);
                        mon.HasDdc = GetVCPFeatureAndVCPFeatureReply(mon.Physical, 0x10, IntPtr.Zero, out mon.Cur, out mon.Max);
                    }
                }
                list.Add(mon);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void Release(IEnumerable<Monitor> mons)
        {
            foreach (var m in mons) if (m.Physical != IntPtr.Zero) DestroyPhysicalMonitor(m.Physical);
        }

        public static bool Set(Monitor m, uint value) => m.Physical != IntPtr.Zero && SetVCPFeature(m.Physical, 0x10, value);
    }

    /// <summary>EDID names and HDR state through the DisplayConfig API.</summary>
    public static class DisplayInfo
    {
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint N, D; }
        [StructLayout(LayoutKind.Sequential)] struct PATH_TARGET { public LUID adapterId; public uint id, modeInfoIdx; public int outputTechnology, rotation, scaling; public RATIONAL refreshRate; public int scanLineOrdering, targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE sourceInfo; public PATH_TARGET targetInfo; public uint flags; }
        [StructLayout(LayoutKind.Sequential)] struct MODE_INFO { public int infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data; }
        [StructLayout(LayoutKind.Sequential)] struct HEADER { public int type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_NAME
        {
            public HEADER header; public uint flags; public int outputTechnology; public ushort edidManufactureId, edidProductCodeId; public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }
        [StructLayout(LayoutKind.Sequential)] struct COLOR2 { public HEADER header; public uint value; public int encoding; public uint bits; public int activeColorMode; }
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint np, out uint nm);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH_INFO[] p, ref uint nm, [Out] MODE_INFO[] m, IntPtr topo);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref COLOR2 r);

        static IEnumerable<(TARGET_NAME name, PATH_INFO path)> Targets()
        {
            GetDisplayConfigBufferSizes(2, out uint np, out uint nm);
            var paths = new PATH_INFO[np]; var modes = new MODE_INFO[nm];
            if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) yield break;
            foreach (var p in paths.Take((int)np))
            {
                var t = new TARGET_NAME(); t.header.type = 2; t.header.size = (uint)Marshal.SizeOf<TARGET_NAME>();
                t.header.adapterId = p.targetInfo.adapterId; t.header.id = p.targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref t) == 0) yield return (t, p);
            }
        }

        static string ModelOf(string devicePath)
        {
            var m = Regex.Match(devicePath ?? "", @"DISPLAY#([A-Z0-9]+)#", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        public static Dictionary<string, string> FriendlyNames()
        {
            var d = new Dictionary<string, string>();
            try { foreach (var (t, _) in Targets()) { var m = ModelOf(t.monitorDevicePath); if (m != null) d[m] = t.monitorFriendlyDeviceName; } } catch { }
            return d;
        }

        public static bool HdrOn(string model)
        {
            try
            {
                foreach (var (t, p) in Targets())
                {
                    if (ModelOf(t.monitorDevicePath) != model) continue;
                    var c = new COLOR2(); c.header.type = 15; c.header.size = (uint)Marshal.SizeOf<COLOR2>();   // ADVANCED_COLOR_INFO_2
                    c.header.adapterId = p.targetInfo.adapterId; c.header.id = p.targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref c) != 0) return false;
                    return ((c.value >> 5) & 1) == 1;      // highDynamicRangeUserEnabled
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>ArgyllCMS command line tools (spotread for room light, dispread for screen luminance).</summary>
    public static class Argyll
    {
        public static string Find(string configured)
        {
            bool Ok(string d) => !string.IsNullOrWhiteSpace(d) && File.Exists(System.IO.Path.Combine(d, "spotread.exe"));
            if (Ok(configured)) return configured;
            var cands = new List<string>
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Argyll", "bin"),
                @"C:\Program Files\ArgyllCMS\bin", @"C:\ArgyllCMS\bin",
            };
            foreach (var pattern in new[] {
                (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"DisplayCAL\dl"),
                (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"GloamApp\current\argyll_cache"),
                (@"C:\", ""), (@"C:\Program Files", "") })
            {
                var root = System.IO.Path.Combine(pattern.Item1, pattern.Item2);
                try { if (Directory.Exists(root)) cands.AddRange(Directory.GetDirectories(root, "Argyll*").OrderByDescending(x => x).Select(x => System.IO.Path.Combine(x, "bin"))); } catch { }
            }
            foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) cands.Add(p);
            return cands.FirstOrDefault(Ok);
        }

        static (int code, string text) Run(string bin, string exe, string args, string stdin, int timeoutMs)
        {
            var psi = new ProcessStartInfo(System.IO.Path.Combine(bin, exe), args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = System.IO.Path.GetTempPath(),
            };
            using var p = Process.Start(psi);
            if (stdin != null) p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            var err = p.StandardError.ReadToEndAsync();
            var outp = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, ""); }
            return (p.ExitCode, outp.Result + err.Result);
        }

        /// <summary>Room light from the colorimeter in ambient mode (diffuser closed), in lux.</summary>
        public static double? AmbientLux(Config cfg)
        {
            var bin = cfg.ArgyllBin; if (bin == null) return null;
            var (_, text) = Run(bin, "spotread.exe", $"-c{cfg.Instrument} -a -x -O", "\n\n", 90000);
            var m = Regex.Match(text, @"Ambient = ([\d.]+) Lux");
            if (m.Success) return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            m = Regex.Match(text, @"Yxy: ([\d.]+)");        // ambient Y is lux / pi
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * Math.PI : null;
        }

        /// <summary>Argyll's display number for a Windows device name like \\.\DISPLAY2.</summary>
        public static int? DisplayNumber(Config cfg, string device)
        {
            var (_, text) = Run(cfg.ArgyllBin, "dispwin.exe", "-?", null, 20000);
            var dev = device.Replace(@"\\.\", "");
            var m = Regex.Match(text, @"^\s+(\d+) = '" + Regex.Escape(dev) + ",", RegexOptions.Multiline);
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }

        /// <summary>Luminance of a full-white patch on that display (sensor on the screen, diffuser open).</summary>
        public static double? WhiteNits(Config cfg, int display)
        {
            var b = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alc_white");
            File.WriteAllText(b + ".ti1",
                "CTI1\n\nDESCRIPTOR \"white\"\nORIGINATOR \"AmbientLightCalibrate\"\nCOLOR_REP \"RGB\"\nNUMBER_OF_FIELDS 7\nBEGIN_DATA_FORMAT\n" +
                "SAMPLE_ID RGB_R RGB_G RGB_B XYZ_X XYZ_Y XYZ_Z\nEND_DATA_FORMAT\nNUMBER_OF_SETS 1\nBEGIN_DATA\n1 100 100 100 95 100 108\nEND_DATA\n");
            if (File.Exists(b + ".ti3")) File.Delete(b + ".ti3");
            Run(cfg.ArgyllBin, "dispread.exe", $"-d{display} -c{cfg.Instrument} -y {cfg.DisplayType} -Y p \"{b}\"", null, 120000);
            if (!File.Exists(b + ".ti3")) return null;
            var m = Regex.Match(File.ReadAllText(b + ".ti3"), "LUMINANCE_XYZ_CDM2\\s+\"[\\d.]+ ([\\d.]+)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
    }

    public sealed class Result
    {
        public double? Lux, Target;
        public string Error;
        public List<string> Notes = new();
        public DateTime When = DateTime.Now;
    }

    public static class Engine
    {
        // other colour tools that need the sensor exclusively
        static readonly string[] Busy = { "dispcal", "dispread", "colprof", "spotread", "chartread", "Calibrator", "DisplayCAL", "calibrite PROFILER", "i1Profiler" };

        public static string BusyWith() => string.Join(", ", Busy.Where(n => Process.GetProcessesByName(n).Length > 0));

        public static Result AdjustOnce(Config cfg)
        {
            var r = new Result();
            if (cfg.ArgyllBin == null) { r.Error = S.NoArgyll; return r; }
            var busy = BusyWith();
            if (busy.Length > 0) { r.Error = S.BusyWith(busy); Log.Write("skip: " + busy + " running"); return r; }
            try { r.Lux = Argyll.AmbientLux(cfg); } catch (Exception e) { r.Error = S.MeasureFailed; Log.Write("ambient failed: " + e.Message); }
            if (r.Lux == null) { r.Error ??= S.NoSensor; Log.Write("ambient: no reading"); return r; }
            r.Target = cfg.TargetNits(r.Lux.Value);
            var mons = Ddc.Enumerate();
            try
            {
                foreach (var m in mons)
                {
                    var map = cfg.Map(m.Model);
                    if (map == null || !m.HasDdc) continue;
                    if (DisplayInfo.HdrOn(m.Model)) { r.Notes.Add($"{m.Model}: HDR, untouched"); continue; }
                    double now = Config.NitsFor(m.Cur, map);
                    double want = Math.Max(Math.Min(r.Target.Value, now + cfg.MaxStep), now - cfg.MaxStep);
                    if (Math.Abs(want - now) / Math.Max(now, 1) * 100 < cfg.Hysteresis) { r.Notes.Add($"{m.Model}: {now:F0} nits (kept)"); continue; }
                    int s = Config.SettingFor(want, map);
                    Ddc.Set(m, (uint)s);
                    r.Notes.Add($"{m.Model}: brightness {m.Cur} -> {s} (~{now:F0} -> {Config.NitsFor(s, map):F0} nits)");
                }
            }
            finally { Ddc.Release(mons); }
            Log.Write($"room {r.Lux:F1} lx -> target {r.Target:F0} nits | " + string.Join("; ", r.Notes));
            return r;
        }

        /// <summary>Measure Brightness → nits for one monitor; restores its brightness afterwards.</summary>
        public static List<(double s, double n)> CalibrateMap(Config cfg, string model, Action<int> progress)
        {
            var mons = Ddc.Enumerate();
            try
            {
                var m = mons.FirstOrDefault(x => x.Model == model && x.HasDdc) ?? throw new Exception("DDC/CI");
                int disp = Argyll.DisplayNumber(cfg, m.Device) ?? throw new Exception("Argyll display");
                uint orig = m.Cur;
                var map = new List<(double, double)>();
                try
                {
                    foreach (int s in new[] { 0, 15, 30, 50, 70, 100 })
                    {
                        progress(s);
                        Ddc.Set(m, (uint)Math.Round(s * m.Max / 100.0));
                        System.Threading.Thread.Sleep(2500);
                        var n = Argyll.WhiteNits(cfg, disp) ?? throw new Exception(S.NoSensor);
                        map.Add((Math.Round(s * m.Max / 100.0), n));
                    }
                }
                finally { Ddc.Set(m, orig); }
                Log.Write($"calibrated {model}: " + string.Join(" ", map.Select(p => $"{p.Item1}:{p.Item2:F1}")));
                return map;
            }
            finally { Ddc.Release(mons); }
        }
    }
}
