using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.App.Update;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Update;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The single window (brief, section 7), in the owner's Graphite design since M6b (docs/design/design-spec.md):
    /// a 56 px top bar with the tabs and the sharing pill, the yellow update strip when a newer PrintVect is ready,
    /// the page, and a slim status bar. Built in code rather than with the designer so it stays readable in a diff.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int MessageDisplaySeconds = 8;

        private readonly HostController _controller;
        private readonly UpdateController _updates;
        private readonly List<NavTab> _tabs = new List<NavTab>();
        private readonly List<Control> _pages = new List<Control>();

        private Panel _pageHost;
        private Panel _tabStrip;
        private SettingsTab _settingsTab;
        private StatusPill _sharingPill;
        private TableLayoutPanel _banner;
        private Label _bannerLabel;
        private ThemedButton _bannerUpdate;
        private Version _bannerDismissed;
        private Label _messageLabel;
        private Timer _messageTimer;
        private int _current = -1;

        public MainForm(AppPaths paths, ConfigStore store, AppConfig config, HostController controller, ClientController client,
            UpdateController updates)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            _controller = controller;
            _updates = updates;
            BuildLayout(paths, store, config, client);
            RefreshBanner();
            UpdateSharingStatus(_controller.IsSharing);
            _controller.SharingChanged += (s, e) => UpdateSharingStatus(_controller.IsSharing);
            _updates.Changed += (s, e) => RefreshBanner();
        }

        public void UpdateSharingStatus(bool sharingOn)
        {
            _sharingPill.Text = sharingOn ? Strings.StatusSharingOn : Strings.StatusSharingOff;
            _sharingPill.Kind = sharingOn ? PillKind.Connected : PillKind.Offline;
        }

        /// <summary>Opens the Settings page, where the Updates card lives (the update balloon leads here).</summary>
        public void ShowSettings()
        {
            if (!IsDisposed)
            {
                Select(_pages.IndexOf(_settingsTab));
            }
        }

        private void BuildLayout(AppPaths paths, ConfigStore store, AppConfig config, ClientController client)
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;   // sizes come from Ui.S, fonts are in points
            Text = Strings.AppName;
            Icon = AppIcon.Window;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = ThemeFonts.Text;
            ClientSize = new Size(Ui.S(1040), Ui.S(680));
            MinimumSize = new Size(Ui.S(880), Ui.S(600));
            KeyPreview = true;

            _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background, Padding = Ui.Pad(24, 20, 24, 20) };
            var share = new ShareTab(_controller) { Dock = DockStyle.Fill };
            share.Message += (s, text) => ShowMessage(text);
            var jobs = new JobsTab(_controller, client) { Dock = DockStyle.Fill };
            jobs.Message += (s, text) => ShowMessage(text);
            var use = new UseTab(client) { Dock = DockStyle.Fill };
            _settingsTab = new SettingsTab(paths, store, config, _controller, client, _updates) { Dock = DockStyle.Fill };
            _settingsTab.Message += (s, text) => ShowMessage(text);

            Panel topBar = CreateTopBar();
            AddPage(Strings.TabShare, share);
            AddPage(Strings.TabJobs, jobs);
            AddPage(Strings.TabUse, use);
            AddPage(Strings.TabSettings, _settingsTab);

            _messageTimer = new Timer { Interval = MessageDisplaySeconds * 1000 };
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _messageLabel.Text = "";
            };

            Controls.Add(_pageHost);
            Controls.Add(CreateBanner());
            Controls.Add(topBar);
            Controls.Add(CreateStatusBar());
            Ui.Solidify(this);
            Select(0);
            ResumeLayout(true);
        }

        private Panel CreateTopBar()
        {
            var bar = new BorderedPanel(bottom: true) { Dock = DockStyle.Top, Height = Ui.S(56), BackColor = Palette.TopBar, Padding = Ui.Pad(20, 0, 20, 0) };

            var logo = new PictureBox
            {
                Image = new Icon(AppIcon.Window, Ui.S(24), Ui.S(24)).ToBitmap(),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Dock = DockStyle.Left,
                Width = Ui.S(32),
                BackColor = Palette.TopBar
            };
            var name = new Label
            {
                Text = Strings.AppName,
                Font = ThemeFonts.CardTitle,
                ForeColor = Palette.Text,
                BackColor = Palette.TopBar,
                Dock = DockStyle.Left,
                AutoSize = false,
                Width = TextRenderer.MeasureText(Strings.AppName, ThemeFonts.CardTitle).Width + Ui.S(28),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = Ui.Pad(6, 0, 0, 0)
            };

            _sharingPill = new StatusPill { Anchor = AnchorStyles.Right, Kind = PillKind.Offline };
            var right = new TableLayoutPanel { Dock = DockStyle.Right, AutoSize = true, ColumnCount = 1, RowCount = 1, BackColor = Palette.TopBar };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(_sharingPill, 0, 0);

            _tabStrip = new Panel { Dock = DockStyle.Fill, BackColor = Palette.TopBar };
            bar.Controls.Add(_tabStrip);
            bar.Controls.Add(right);
            bar.Controls.Add(name);
            bar.Controls.Add(logo);
            return bar;
        }

        private void AddPage(string title, Control page)
        {
            int index = _pages.Count;
            var tab = new NavTab(title);
            tab.Click += (s, e) => Select(index);
            _tabs.Add(tab);
            _tabStrip.Controls.Add(tab);
            tab.BringToFront();   // docked left: the last one added sits rightmost
            _pages.Add(page);
            page.Visible = false;
            _pageHost.Controls.Add(page);
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _pages.Count || index == _current)
            {
                return;
            }
            for (int i = 0; i < _pages.Count; i++)
            {
                _tabs[i].Active = i == index;
            }
            // Show the new page before hiding the old one so the window never flashes empty.
            _pages[index].Visible = true;
            if (_current >= 0)
            {
                _pages[_current].Visible = false;
            }
            _current = index;
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / PageUp move between pages, like the old tabs.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int count = _pages.Count;
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown))
            {
                Select((_current + 1) % count);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp))
            {
                Select((_current + count - 1) % count);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>A strip above the pages that appears when a newer PrintVect is ready (M6a).</summary>
        private TableLayoutPanel CreateBanner()
        {
            _banner = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Palette.Warning,
                Padding = Ui.Pad(24, 8, 24, 8),
                Visible = false
            };
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _bannerLabel = Make.Label("", ThemeFonts.TextSemi, Palette.OnWarning, Ui.Pad(0, 0, 8, 0));
            _bannerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _bannerUpdate = new ThemedButton(Strings.SettingsUpdateNow, ButtonKind.OnWarning) { Anchor = AnchorStyles.None };
            _bannerUpdate.Click += (s, e) => _settingsTab.StartUpdate();
            var later = new ThemedButton(Strings.UpdateBannerLater, ButtonKind.OnWarning) { Anchor = AnchorStyles.None, Margin = Ui.Pad(0) };
            later.Click += (s, e) => DismissBanner();

            _banner.Controls.Add(_bannerLabel, 0, 0);
            _banner.Controls.Add(_bannerUpdate, 1, 0);
            _banner.Controls.Add(later, 2, 0);
            return _banner;
        }

        private Panel CreateStatusBar()
        {
            var bar = new BorderedPanel(bottom: false) { Dock = DockStyle.Bottom, Height = Ui.S(30), BackColor = Palette.TopBar, Padding = Ui.Pad(24, 0, 24, 0) };
            _messageLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = ThemeFonts.Secondary,
                ForeColor = Palette.Text,
                BackColor = Palette.TopBar,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            var version = new Label
            {
                Text = string.Format(Strings.StatusVersion, AppInfo.Version),
                Dock = DockStyle.Right,
                AutoSize = true,
                Font = ThemeFonts.Secondary,
                ForeColor = Palette.Muted,
                BackColor = Palette.TopBar,
                Padding = Ui.Pad(0, 7, 0, 0)
            };
            bar.Controls.Add(_messageLabel);
            bar.Controls.Add(version);
            return bar;
        }

        private void RefreshBanner()
        {
            if (IsDisposed || _banner == null)
            {
                return;
            }
            ReleaseInfo available = _updates.Available;
            bool showBanner = available != null && (_bannerDismissed == null || _bannerDismissed != available.Version);
            if (showBanner)
            {
                _bannerLabel.Text = string.Format(Strings.UpdateBannerText, AppVersions.Format(available.Version));
                _bannerUpdate.Enabled = _updates.Phase == UpdatePhase.Idle;
            }
            _banner.Visible = showBanner;
        }

        private void DismissBanner()
        {
            ReleaseInfo available = _updates.Available;
            _bannerDismissed = available == null ? null : available.Version;
            RefreshBanner();
        }

        private void ShowMessage(string text)
        {
            _messageLabel.Text = text;
            _messageTimer.Stop();
            _messageTimer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.UseDarkTitleBar(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _messageTimer != null)
            {
                _messageTimer.Dispose();
                _messageTimer = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>A panel with a 1 px border line along its bottom (top bar) or top (status bar).</summary>
        private sealed class BorderedPanel : Panel
        {
            private readonly bool _bottom;

            public BorderedPanel(bool bottom)
            {
                _bottom = bottom;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(Palette.Border))
                {
                    int y = _bottom ? Height - 1 : 0;
                    e.Graphics.DrawLine(pen, 0, y, Width, y);
                }
            }
        }
    }
}
