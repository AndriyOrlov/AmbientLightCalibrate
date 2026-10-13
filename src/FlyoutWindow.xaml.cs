// AmbientLightCalibrate — ambient-light-driven monitor brightness for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AmbientLightCalibrate
{
    public partial class FlyoutWindow : Window
    {
        public DateTime HiddenAt = DateTime.MinValue;
        static readonly int[] PresetNits = { 80, 100, 120, 140, 160, 200 };
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static App A => App.Me;

        readonly List<(string model, Slider slider, TextBlock text, List<(double s, double n)> map)> _rows = new();
        readonly Dictionary<string, int> _pending = new();
        readonly DispatcherTimer _apply = new() { Interval = TimeSpan.FromMilliseconds(180) };
        readonly DispatcherTimer _offset = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly List<(TextBox lux, TextBox nits)> _curve = new();
        bool _loading;
        string _calibModel, _calibName;
        bool _calibRunning;

        public FlyoutWindow()
        {
            InitializeComponent();
            foreach (var n in PresetNits)
            {
                var b = new Button { Content = n.ToString(), Style = (Style)FindResource("Pill"), ToolTip = S.PresetTip(n) };
                b.Click += (s, e) => AllSlider.Value = n;
                Presets.Children.Add(b);
            }
            _apply.Tick += (s, e) => Flush();
            _offset.Tick += (s, e) => { _offset.Stop(); A.Cfg.Offset = OffsetSlider.Value; A.Cfg.Save(); A.Reapply(); };
            Deactivated += (s, e) => { Hide(); HiddenAt = DateTime.Now; };
            SourceInitialized += (s, e) => Backdrop();
            SizeChanged += (s, e) => Anchor();
            A.Changed += () => { if (IsVisible) RefreshStatus(); };
        }

        // ------------------------------------------------------------ window chrome
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        void Backdrop()
        {
            var h = new WindowInteropHelper(this).Handle;
            int on = 1, round = 2, acrylic = 3;
            DwmSetWindowAttribute(h, 20, ref on, 4);        // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(h, 33, ref round, 4);     // DWMWA_WINDOW_CORNER_PREFERENCE = round
            DwmSetWindowAttribute(h, 38, ref acrylic, 4);   // DWMWA_SYSTEMBACKDROP_TYPE = transient (acrylic)
        }

        // glued to the bottom-right corner above the taskbar, growing upwards
        void Anchor()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 12;
            Top = Math.Max(wa.Top + 12, wa.Bottom - ActualHeight - 12);
        }

        public void ShowAtTray()
        {
            bool keepSettings = SettingsPage.Visibility == Visibility.Visible && (_calibModel != null || _calibRunning);
            if (!keepSettings) { ShowMain(); LoadMain(); }
            MaxHeight = SystemParameters.WorkArea.Height - 24;
            Show(); UpdateLayout(); Anchor();
            Activate();
        }

        // ------------------------------------------------------------ main page
        void LoadMain()
        {
            _loading = true;
            AutoSwitch.IsChecked = A.Cfg.Auto;
            OffsetSlider.Value = A.Cfg.Offset;
            OffsetText.Text = $"{A.Cfg.Offset:+0;-0;0} {S.NitsUnit}";
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) StartupSwitch.IsChecked = k?.GetValue("AmbientLightCalibrate") != null;

            MonitorRows.Children.Clear(); _rows.Clear();
            var mons = Ddc.Enumerate();
            try
            {
                foreach (var m in mons)
                {
                    var map = A.Cfg.Map(m.Model);
                    if (map == null || !m.HasDdc) continue;
                    bool hdr = DisplayInfo.HdrOn(m.Model);
                    var g = Row3(out var name, out var sl, out var tx);
                    name.Text = m.Name; name.ToolTip = m.Model;
                    sl.Minimum = map.Min(p => p.n); sl.Maximum = map.Max(p => p.n);
                    sl.Value = Config.NitsFor(m.Cur, map); sl.IsEnabled = !hdr;
                    tx.Text = hdr ? S.Hdr : $"{sl.Value:F0} {S.NitsUnit}";
                    sl.ToolTip = hdr ? S.HdrTip : $"Brightness {m.Cur}";
                    var model = m.Model;
                    sl.ValueChanged += (s, e) =>
                    {
                        int set = Config.SettingFor(sl.Value, map);
                        tx.Text = $"{Config.NitsFor(set, map):F0} {S.NitsUnit}"; sl.ToolTip = $"Brightness {set}";
                        if (_loading) return;
                        _pending[model] = set; _apply.Stop(); _apply.Start();
                    };
                    MonitorRows.Children.Add(g);
                    _rows.Add((model, sl, tx, map));
                }
            }
            finally { Ddc.Release(mons); }
            if (_rows.Count == 0)
                MonitorRows.Children.Add(new TextBlock { Text = S.NoMonitors, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap });
            var en = _rows.Where(r => r.slider.IsEnabled).ToList();
            AllSlider.Value = en.Count > 0 ? en.Average(r => r.slider.Value) : 120;
            AllText.Text = $"{AllSlider.Value:F0} {S.NitsUnit}";
            RefreshStatus();
            RefreshReminder();
            _loading = false;
        }

        void RefreshReminder()
        {
            var due = Reminder.Overdue(A.Cfg);
            RecalCard.Visibility = due.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecalText.Text = string.Join("\n", due.Select(d => S.RecalItem(d.name, d.hdr, d.days)));
        }

        void Snooze_Click(object s, RoutedEventArgs e)
        {
            A.Cfg.SnoozeUntil = DateTime.Now.AddDays(7); A.Cfg.Save(); RefreshReminder();
        }

        static Grid Row3(out TextBlock name, out Slider slider, out TextBlock value)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            slider = new Slider { IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            value = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1); Grid.SetColumn(value, 2);
            g.Children.Add(name); g.Children.Add(slider); g.Children.Add(value);
            return g;
        }

        void RefreshStatus()
        {
            StatusText.Text = A.StatusText();
            BusyBar.Visibility = A.Busy ? Visibility.Visible : Visibility.Collapsed;
            AutoSwitch.IsChecked = A.Cfg.Auto;
        }

        void Flush()
        {
            _apply.Stop();
            var todo = new Dictionary<string, int>(_pending); _pending.Clear();
            if (todo.Count == 0) return;
            A.ManualChange();
            Task.Run(() => Engine.SetManual(todo));
        }

        /// <summary>Live view of a brightness written by the automation (each step of a fade).</summary>
        public void ShowBrightness(string model, uint setting)
        {
            if (!IsLoaded) return;
            var row = _rows.FirstOrDefault(r => r.model == model);
            if (row.slider == null || _pending.Count > 0) return;
            bool was = _loading; _loading = true;
            try
            {
                row.slider.Value = Config.NitsFor(setting, row.map);
                var en = _rows.Where(r => r.slider.IsEnabled).ToList();
                if (en.Count > 0) AllSlider.Value = en.Average(r => r.slider.Value);
            }
            finally { _loading = was; }
        }

        void AllSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AllText == null) return;
            AllText.Text = $"{AllSlider.Value:F0} {S.NitsUnit}";
            if (_loading) return;
            foreach (var r in _rows.Where(r => r.slider.IsEnabled))
                r.slider.Value = Math.Clamp(AllSlider.Value, r.slider.Minimum, r.slider.Maximum);
        }

        void OffsetSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OffsetText == null) return;
            OffsetText.Text = $"{OffsetSlider.Value:+0;-0;0} {S.NitsUnit}";
            if (_loading) return;
            _offset.Stop(); _offset.Start();      // the offset keeps automation on and applies at once
        }

        void AutoSwitch_Click(object s, RoutedEventArgs e) { A.Auto = AutoSwitch.IsChecked == true; RefreshStatus(); }
        void Measure_Click(object s, RoutedEventArgs e) { if (!A.Cfg.Auto) A.Auto = true; else A.MeasureNow(); RefreshStatus(); }

        void StartupSwitch_Click(object s, RoutedEventArgs e)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (StartupSwitch.IsChecked == true) k.SetValue("AmbientLightCalibrate", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("AmbientLightCalibrate", false);
        }

        void Log_Click(object s, RoutedEventArgs e)
        {
            if (System.IO.File.Exists(Config.PathLog)) Process.Start(new ProcessStartInfo(Config.PathLog) { UseShellExecute = true });
        }

        void Exit_Click(object s, RoutedEventArgs e) => A.Quit();

        // ------------------------------------------------------------ settings page
        void ShowMain() { SettingsPage.Visibility = Visibility.Collapsed; MainPage.Visibility = Visibility.Visible; }

        void Settings_Click(object s, RoutedEventArgs e)
        {
            var c = A.Cfg;
            IntervalBox.Text = c.IntervalS.ToString(CultureInfo.InvariantCulture);
            HystBox.Text = c.Hysteresis.ToString(CultureInfo.InvariantCulture);
            StepBox.Text = c.MaxStep.ToString(CultureInfo.InvariantCulture);
            ArgyllBox.Text = c.ArgyllSetting;
            ArgyllFound.Text = c.ArgyllBin != null ? "✓ " + c.ArgyllBin : "✗ " + S.NoArgyll;
            CurveRows.Children.Clear(); _curve.Clear();
            foreach (var p in c.Curve) AddCurveRow(p.lux.ToString(CultureInfo.InvariantCulture), p.nits.ToString(CultureInfo.InvariantCulture));
            BuildCalibRows();
            ReminderBox.Items.Clear();
            foreach (var n in new[] { 0, 1, 2, 3 }) ReminderBox.Items.Add(new ComboBoxItem { Content = n == 0 ? S.ReminderOff : S.EveryMonths(n), Tag = n });
            ReminderBox.SelectedIndex = Math.Clamp(c.ReminderMonths, 0, 3);
            ProfileDatesText.Text = string.Join("\n", DisplayInfo.ProfileDates().Select(p =>
                S.ProfileLine(string.IsNullOrEmpty(p.name) ? p.model : p.name, p.hdr, p.created, (int)(DateTime.Now - p.created).TotalDays)));
            SaveError.Text = "";
            MainPage.Visibility = Visibility.Collapsed; SettingsPage.Visibility = Visibility.Visible;
            Scroller.ScrollToTop();
        }

        void BuildCalibRows()
        {
            CalibRows.Children.Clear();
            var mons = Ddc.Enumerate();
            try
            {
                foreach (var m in mons)
                {
                    var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var map = A.Cfg.Map(m.Model);
                    var info = new StackPanel();
                    info.Children.Add(new TextBlock { Text = m.Name, ToolTip = m.Model });
                    info.Children.Add(new TextBlock
                    {
                        Style = (Style)FindResource("Caption"),
                        Text = !m.HasDdc ? S.NoDdc : map == null ? S.NotCalibrated : S.PointsRange(map.Count, map.Min(p => p.n), map.Max(p => p.n)),
                    });
                    var b = new Button { Content = S.Calibrate, Padding = new Thickness(12, 4, 12, 4), IsEnabled = m.HasDdc && !_calibRunning, VerticalAlignment = VerticalAlignment.Center };
                    var (model, name) = (m.Model, m.Name);
                    b.Click += (s, e) =>
                    {
                        _calibModel = model; _calibName = name;
                        CalibPromptText.Text = S.PlaceSensor(name);
                        CalibPrompt.Visibility = Visibility.Visible; CalibStart.IsEnabled = true; CalibStatus.Text = "";
                    };
                    Grid.SetColumn(b, 1);
                    g.Children.Add(info); g.Children.Add(b);
                    CalibRows.Children.Add(g);
                }
            }
            finally { Ddc.Release(mons); }
        }

        void CalibCancel_Click(object s, RoutedEventArgs e)
        {
            _calibModel = null; CalibPrompt.Visibility = Visibility.Collapsed;
        }

        void CalibStart_Click(object s, RoutedEventArgs e)
        {
            if (_calibModel == null || _calibRunning) return;
            string model = _calibModel, name = _calibName;
            _calibRunning = true; CalibStart.IsEnabled = false;
            var cfg = A.Cfg;
            Task.Run(() =>
            {
                try
                {
                    var map = Engine.CalibrateMap(cfg, model, set => Dispatcher.Invoke(() => CalibStatus.Text = S.Calibrating(name, set)));
                    Dispatcher.Invoke(() =>
                    {
                        A.Cfg.SetMap(model, map); A.Cfg.Save();
                        CalibStatus.Text = S.CalibDone(name); A.Balloon(S.CalibDone(name));
                    });
                }
                catch (Exception ex)
                {
                    Log.Write($"calibration of {model} failed: {ex.Message}");
                    Dispatcher.Invoke(() => { CalibStatus.Text = S.CalibFailed(ex.Message); A.Balloon(S.CalibFailed(ex.Message)); });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        _calibRunning = false; _calibModel = null;
                        CalibPrompt.Visibility = Visibility.Collapsed; BuildCalibRows();
                    });
                }
            });
        }

        void AddCurveRow(string lux, string nits)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var l = new TextBox { Text = lux }; var n = new TextBox { Text = nits };
            Grid.SetColumn(n, 2); g.Children.Add(l); g.Children.Add(n);
            CurveRows.Children.Add(g); _curve.Add((l, n));
        }

        void AddPoint_Click(object s, RoutedEventArgs e) { AddCurveRow("", ""); _curve[^1].lux.Focus(); }

        void Back_Click(object s, RoutedEventArgs e) { _calibModel = null; CalibPrompt.Visibility = Visibility.Collapsed; ShowMain(); LoadMain(); }

        static bool Num(string t, out double v) =>
            double.TryParse(t.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        void Save_Click(object s, RoutedEventArgs e)
        {
            if (!Num(IntervalBox.Text, out double iv) || iv < 10) { SaveError.Text = S.ErrInterval; return; }
            if (!Num(HystBox.Text, out double hy) || hy < 0) { SaveError.Text = S.ErrNumber; return; }
            if (!Num(StepBox.Text, out double st) || st < 0) { SaveError.Text = S.ErrNumber; return; }
            var pts = new List<(double, double)>();
            foreach (var (l, n) in _curve)
            {
                if (string.IsNullOrWhiteSpace(l.Text) && string.IsNullOrWhiteSpace(n.Text)) continue;
                if (!Num(l.Text, out double lx) || !Num(n.Text, out double nt) || lx <= 0 || nt <= 0) { SaveError.Text = S.ErrCurve; return; }
                pts.Add((lx, nt));
            }
            if (pts.Count < 2) { SaveError.Text = S.ErrCurve2; return; }
            var c = A.Cfg;
            c.IntervalS = (int)Math.Round(iv); c.Hysteresis = hy; c.MaxStep = st;
            c.SetCurve(pts); c.ArgyllSetting = ArgyllBox.Text.Trim();
            if (ReminderBox.SelectedItem is ComboBoxItem ri) c.ReminderMonths = (int)ri.Tag;
            c.Save();
            Log.Write($"settings: interval {c.IntervalS}s, hysteresis {hy}%, step {st} nits, curve {string.Join(" ", pts.Select(p => $"{p.Item1}:{p.Item2}"))}");
            ShowMain(); LoadMain(); if (c.Auto) A.MeasureNow();
        }
    }
}
