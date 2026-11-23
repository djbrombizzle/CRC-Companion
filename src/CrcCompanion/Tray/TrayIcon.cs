using System;
using System.Collections.Generic;
using WinForms = System.Windows.Forms;

namespace CrcCompanion.Tray
{
    public interface ITrayHost
    {
        string ActiveProfile { get; }
        IReadOnlyList<string> Profiles();
        double UiScale { get; }
        string WeatherSource { get; }
        void SetWeatherSource(string source);
        string Cid { get; }
        bool AutoSwitchProfile { get; }
        bool MacroTypedFlid { get; }
        string SessionStatus { get; }
        void SetCid();
        void ToggleAutoSwitch();
        void ToggleMacroTypedFlid();
        void SwitchProfile(string name);
        void NewProfile();
        void DuplicateProfile();
        void RenameProfile();
        void DeleteProfile();
        void SetScale(double scale);
        void ResetPositions();
        void OpenProfilesFolder();
        void Exit();
    }

    /// <summary>System tray icon: profile switching and app-level settings.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private static readonly double[] Scales = { 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5 };

        private readonly ITrayHost _host;
        private readonly WinForms.NotifyIcon _icon;
        private readonly WinForms.ContextMenuStrip _menu;

        public TrayIcon(ITrayHost host)
        {
            _host = host;
            _menu = new WinForms.ContextMenuStrip();
            _menu.Opening += (_, _) => Rebuild();
            _icon = new WinForms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = _menu,
                Visible = true,
            };
            UpdateText();
            Rebuild();
        }

        public void UpdateText()
        {
            var t = "CRC Companion - " + _host.ActiveProfile;
            _icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        private void Rebuild()
        {
            _menu.Items.Clear();
            _menu.Items.Add(new WinForms.ToolStripMenuItem(_host.SessionStatus) { Enabled = false });
            _menu.Items.Add(new WinForms.ToolStripMenuItem("Profile: " + _host.ActiveProfile) { Enabled = false });
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            Add(_host.Cid.Length == 0 ? "Set VATSIM CID..." : $"VATSIM CID: {_host.Cid}...", _host.SetCid);
            var auto = new WinForms.ToolStripMenuItem("Switch profile when I log on") { Checked = _host.AutoSwitchProfile };
            auto.Click += (_, _) => _host.ToggleAutoSwitch();
            _menu.Items.Add(auto);
            var typed = new WinForms.ToolStripMenuItem("Macros: use FLID typed in CRC") { Checked = _host.MacroTypedFlid };
            typed.Click += (_, _) => _host.ToggleMacroTypedFlid();
            _menu.Items.Add(typed);
            _menu.Items.Add(new WinForms.ToolStripSeparator());

            foreach (var name in _host.Profiles())
            {
                var n = name;
                var item = new WinForms.ToolStripMenuItem(n) { Checked = n == _host.ActiveProfile };
                item.Click += (_, _) => _host.SwitchProfile(n);
                _menu.Items.Add(item);
            }

            _menu.Items.Add(new WinForms.ToolStripSeparator());
            Add("New profile...", _host.NewProfile);
            Add("Duplicate current...", _host.DuplicateProfile);
            Add("Rename current...", _host.RenameProfile);
            var del = Add("Delete current", _host.DeleteProfile);
            del.Enabled = _host.Profiles().Count > 1;

            _menu.Items.Add(new WinForms.ToolStripSeparator());
            var scale = new WinForms.ToolStripMenuItem("Size");
            foreach (var s in Scales)
            {
                var v = s;
                var item = new WinForms.ToolStripMenuItem($"{v * 100:0}%") { Checked = Math.Abs(_host.UiScale - v) < 0.001 };
                item.Click += (_, _) => _host.SetScale(v);
                scale.DropDownItems.Add(item);
            }
            _menu.Items.Add(scale);

            var wx = new WinForms.ToolStripMenuItem("Weather source");
            var real = new WinForms.ToolStripMenuItem("Real world (aviationweather.gov)") { Checked = _host.WeatherSource == Core.WeatherSources.Real };
            real.Click += (_, _) => _host.SetWeatherSource(Core.WeatherSources.Real);
            var vatsim = new WinForms.ToolStripMenuItem("VATSIM (metar.vatsim.net)") { Checked = _host.WeatherSource == Core.WeatherSources.Vatsim };
            vatsim.Click += (_, _) => _host.SetWeatherSource(Core.WeatherSources.Vatsim);
            wx.DropDownItems.Add(real);
            wx.DropDownItems.Add(vatsim);
            _menu.Items.Add(wx);
            Add("Reset view positions", _host.ResetPositions);
            Add("Open profiles folder", _host.OpenProfilesFolder);

            _menu.Items.Add(new WinForms.ToolStripSeparator());
            Add("Exit", _host.Exit);
        }

        private WinForms.ToolStripMenuItem Add(string text, Action action)
        {
            var item = new WinForms.ToolStripMenuItem(text);
            item.Click += (_, _) => action();
            _menu.Items.Add(item);
            return item;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
