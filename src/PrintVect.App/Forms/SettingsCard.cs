using System;
using System.Globalization;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Config;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The settings a clerk may change (M6b-2), each saved at once and applied without a restart: the office PIN (PIN
    /// screen), Start with Windows (through PrintVect.Elevate, Windows asks once), the copies question, and how long sent
    /// files are kept. The ports are shown but not editable here: every PC must use the same ones.
    /// </summary>
    internal sealed class SettingsCard : Card
    {
        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly Action<string> _message;

        private readonly Label _pinState;
        private readonly ThemedButton _pinButton;
        private readonly ThemedButton _pinRemove;
        private readonly ToggleSwitch _autostart;
        private readonly ToggleSwitch _askCopies;
        private readonly Label _keepHours;
        private bool _syncing;

        public SettingsCard(AppPaths paths, ConfigStore store, AppConfig config, HostController host, ClientController client,
            Action<string> message)
        {
            _paths = paths;
            _store = store;
            _config = config;
            _host = host;
            _client = client;
            _message = message ?? (m => { });
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Ui.Pad(0, 0, 0, 16);

            _pinState = Make.Body("");
            _pinButton = new ThemedButton("", ButtonKind.Secondary) { MinimumHeight = 36 };
            _pinButton.Click += (s, e) => ChangePin();
            _pinRemove = new ThemedButton(Strings.SettingsPinRemove, ButtonKind.Secondary) { MinimumHeight = 36 };
            _pinRemove.Click += (s, e) => RemovePin();
            FlowLayoutPanel pinButtons = Make.Row(_pinButton, _pinRemove);
            pinButtons.Margin = Ui.Pad(0, 0, 0, 16);

            _autostart = new ToggleSwitch(Strings.SettingsAutostart) { WrapWidth = 290, Font = ThemeFonts.Text, Margin = Ui.Pad(0, 0, 0, 12) };
            _autostart.CheckedChanged += (s, e) => ChangeAutostart();
            _askCopies = new ToggleSwitch(Strings.SettingsAskCopies) { WrapWidth = 290, Font = ThemeFonts.Text, Margin = Ui.Pad(0, 0, 0, 16) };
            _askCopies.CheckedChanged += (s, e) =>
            {
                if (!_syncing && !_client.SetAskCopies(_askCopies.Checked)) SaveFailed();
            };

            var less = new ThemedButton("−", ButtonKind.Secondary) { MinimumHeight = 36, Margin = Ui.Pad(0) };
            less.Click += (s, e) => ChangeKeepHours(-1);
            var more = new ThemedButton("+", ButtonKind.Secondary) { MinimumHeight = 36, Margin = Ui.Pad(0) };
            more.Click += (s, e) => ChangeKeepHours(+1);
            _keepHours = new Label
            {
                AutoSize = false,
                Size = new System.Drawing.Size(Ui.S(110), Ui.S(36)),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font = ThemeFonts.TextSemi,
                ForeColor = Palette.Text,
                Margin = Ui.Pad(4, 0, 4, 0)
            };
            FlowLayoutPanel keep = Make.Row(less, _keepHours, more);
            keep.Margin = Ui.Pad(0, 0, 0, 16);

            TableLayoutPanel column = Make.Column(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            column.AutoSize = true;
            column.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            column.Dock = DockStyle.Top;
            column.Controls.Add(Make.Title(Strings.TabSettings), 0, 0);
            column.Controls.Add(Make.Muted(Strings.SettingsIntro), 0, 1);
            column.Controls.Add(Make.Caption(Strings.SettingsPinCaption), 0, 2);
            column.Controls.Add(_pinState, 0, 3);
            column.Controls.Add(pinButtons, 0, 4);
            column.Controls.Add(_autostart, 0, 5);
            column.Controls.Add(_askCopies, 0, 6);
            column.Controls.Add(Make.Caption(Strings.SettingsKeepCaption), 0, 7);
            column.Controls.Add(keep, 0, 8);
            column.Controls.Add(Make.Muted(string.Format(Strings.SettingsPortsNote, _config.DiscoveryPort, _config.JobPort)), 0, 9);
            column.Controls.Add(Make.Wrap(string.Format(Strings.SettingsConfigFile, _store.Path), ThemeFonts.Mono, Palette.Muted, Ui.Pad(0)), 0, 10);
            Controls.Add(column);

            _client.SettingsChanged += OnClientSettingsChanged;
            ShowValues();
        }

        private void OnClientSettingsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ShowValues();
        }

        private void ShowValues()
        {
            _syncing = true;
            try
            {
                bool hasPin = !string.IsNullOrEmpty(_config.Pin);
                _pinState.Text = hasPin ? Strings.SettingsPinOn : Strings.SettingsPinOff;
                _pinButton.Text = hasPin ? Strings.SettingsPinChange : Strings.SettingsPinSet;
                _pinRemove.Visible = hasPin;

                bool? autostart = Autostart.IsOn();
                _autostart.Checked = autostart ?? _config.StartWithWindows;
                _autostart.Enabled = autostart.HasValue;

                _askCopies.Checked = _client.AskCopies;
                _keepHours.Text = string.Format(CultureInfo.CurrentCulture, Strings.SettingsKeepHours, _config.KeepSentFilesHours);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void ChangePin()
        {
            using (var dialog = new PinDialog(!string.IsNullOrEmpty(_config.Pin)))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    SavePin(dialog.Pin, Strings.SettingsPinSaved);
                }
            }
        }

        private void RemovePin()
        {
            if (MessageBox.Show(FindForm(), Strings.SettingsPinRemoveConfirm, Strings.AppName, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                SavePin("", Strings.SettingsPinRemoved);
            }
        }

        /// <summary>The same PIN guards this PC's printers (host) and goes with every job this PC sends (client).</summary>
        private void SavePin(string pin, string done)
        {
            _config.Pin = pin ?? "";
            _host.ApplyPin(_config.Pin);
            _client.ApplyPin(_config.Pin);
            Log.Info("Office PIN " + (_config.Pin.Length == 0 ? "removed" : "set (" + _config.Pin.Length + " characters)") + " in Settings.");
            if (Save()) _message(done);
            ShowValues();
        }

        private void ChangeKeepHours(int delta)
        {
            int hours = Math.Max(1, Math.Min(AppConfig.MaxKeepSentFilesHours, _config.KeepSentFilesHours + delta));
            if (hours == _config.KeepSentFilesHours)
            {
                return;
            }
            _config.KeepSentFilesHours = hours;
            _client.ApplyKeepSentFilesHours(hours);
            Save();
            ShowValues();
        }

        /// <summary>The Run entry is per machine, so PrintVect.Elevate changes it; Windows asks for permission once.</summary>
        private async void ChangeAutostart()
        {
            if (_syncing)
            {
                return;
            }
            bool on = _autostart.Checked;
            _autostart.Enabled = false;
            try
            {
                var arguments = new ElevateArguments { Command = ElevateCommands.Autostart };
                arguments.Options[ElevateCommands.OptionOn] = on ? "yes" : "no";
                ElevateResult result = await ElevateLauncher.RunAsync(_paths, arguments);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    _config.StartWithWindows = on;
                    Save();
                }
                _message(result.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Changing Start with Windows failed.", ex);
                _message(ex.Message);
            }
            finally
            {
                if (!IsDisposed) ShowValues();   // shows what Windows really has now
            }
        }

        private bool Save()
        {
            try
            {
                _store.Save(_config);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + _store.Path, ex);
                SaveFailed();
                return false;
            }
        }

        private void SaveFailed()
        {
            MessageBox.Show(FindForm(), string.Format(Strings.SharingSaveError, _store.Path), Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.SettingsChanged -= OnClientSettingsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
