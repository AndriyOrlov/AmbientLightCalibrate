// AmbientLightCalibrate — ambient-light-driven monitor brightness for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AmbientLightCalibrate
{
    public partial class App : Application
    {
        public static App Me => (App)Current;
        public Config Cfg;
        public Result Last;
        public bool Busy;
        public DateTime HoldUntil = DateTime.MinValue;   // a manual change pauses the automation for an hour
        public event Action Changed;

        System.Windows.Forms.NotifyIcon _tray;
        FlyoutWindow _flyout;
        readonly AutoResetEvent _wake = new(false);
        static Mutex _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            _single = new Mutex(true, "AmbientLightCalibrate.SingleInstance", out bool first);
            if (!first) { Shutdown(); return; }
            base.OnStartup(e);
            Cfg = Config.Load();
            _tray = new System.Windows.Forms.NotifyIcon { Visible = true };
            _tray.MouseUp += (s, a) => Dispatcher.Invoke(ToggleFlyout);
            UpdateTray();
            new Thread(Loop) { IsBackground = true, Name = "ambient" }.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }

        public void Quit() => Shutdown();

        public void Balloon(string text)
        {
            _tray.BalloonTipTitle = "AmbientLightCalibrate"; _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(4000);
        }

        void ToggleFlyout()
        {
            var sinceHidden = _flyout == null ? double.MaxValue : (DateTime.Now - _flyout.HiddenAt).TotalMilliseconds;
            if (_flyout != null && _flyout.IsVisible) { _flyout.Hide(); return; }
            if (sinceHidden < 300) return;   // the same click that just closed it
            _flyout ??= new FlyoutWindow();
            _flyout.ShowAtTray();
        }

        public bool Auto
        {
            get => Cfg.Auto;
            set { Cfg.Auto = value; Cfg.Save(); HoldUntil = DateTime.MinValue; UpdateTray(); if (value) _wake.Set(); }
        }

        public void MeasureNow() { HoldUntil = DateTime.MinValue; _wake.Set(); }
        public void ManualChange() { HoldUntil = DateTime.Now.AddHours(1); UpdateTray(); }

        public string StatusText()
        {
            if (Cfg.Auto && HoldUntil > DateTime.Now) return S.Manual($"{HoldUntil:HH:mm}");
            if (Last == null) return Cfg.Auto ? S.WaitingFirst : S.AutoOff;
            if (Last.Error != null) return S.Skipped(Last.Error, $"{Last.When:HH:mm}");
            return S.Room(Last.Lux.Value, Last.Target.Value, $"{Last.When:HH:mm}");
        }

        void Loop()
        {
            while (true)
            {
                if (Cfg.Auto && DateTime.Now >= HoldUntil)
                {
                    Dispatcher.Invoke(() => { Busy = true; Changed?.Invoke(); });
                    var r = Engine.AdjustOnce(Config.Load());
                    Dispatcher.Invoke(() => { Cfg = Config.Load(); Last = r; Busy = false; UpdateTray(); });
                }
                _wake.WaitOne(TimeSpan.FromSeconds(Math.Max(Cfg.IntervalS, 30)));
            }
        }

        public void UpdateTray()
        {
            if (_tray == null) return;
            var old = _tray.Icon;
            _tray.Icon = SunIcon(Cfg.Auto && DateTime.Now >= HoldUntil);
            old?.Dispose();
            var t = $"{S.AppTitle} · {StatusText()}";
            _tray.Text = t.Length > 127 ? t[..127] : t;
            Changed?.Invoke();
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        static Icon SunIcon(bool on)
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var col = on ? Color.FromArgb(255, 200, 61) : Color.FromArgb(170, 170, 170);
                using var b = new SolidBrush(col);
                using var p = new Pen(col, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.FillEllipse(b, 10, 10, 12, 12);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, (float)(16 + 10 * Math.Cos(a)), (float)(16 + 10 * Math.Sin(a)),
                                  (float)(16 + 14 * Math.Cos(a)), (float)(16 + 14 * Math.Sin(a)));
                }
            }
            var h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }
}
