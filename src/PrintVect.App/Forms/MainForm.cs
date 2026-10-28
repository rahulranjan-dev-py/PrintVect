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
using PrintVect.Core.Logging;
using PrintVect.Core.Update;

namespace PrintVect.App.Forms
{
    internal enum PageId
    {
        Office,
        Share,
        Jobs,
        Settings
    }

    /// <summary>
    /// A page of the window. A page stays shown and laid out behind the open one once it has been opened (0.5.2): opening
    /// it again only brings it to the front. This tells it when it is the one on screen.
    /// </summary>
    internal interface IPage
    {
        /// <summary>True: the page is now on screen. False: another page was opened or the window went to the tray.</summary>
        void SetOpen(bool open);
    }

    /// <summary>What a page may ask of the window around it: a short message, another page, the update.</summary>
    internal interface IShell
    {
        void Say(string text);
        void Go(PageId page);
        void StartUpdate();
    }

    /// <summary>
    /// The single window (brief, section 7) in the redesign of 0.5.0: a side menu with the logo, this PC's name and
    /// sharing state, the four pages and the version; the yellow update strip when a newer PrintVect is ready; the page;
    /// short messages at the bottom right. Built in code rather than with the designer so it stays readable in a diff.
    /// When the colours change (Settings, or Windows in System default) everything inside is built again.
    /// </summary>
    internal sealed class MainForm : Form, IShell
    {
        private const int MessageDisplaySeconds = 8;
        /// <summary>The menu's narrowest width; it grows to fit the longest page name (0.5.6, SidebarWidthFor).</summary>
        private const int SidebarWidth = 248;
        private const int SidebarSidePadding = 14;

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly UpdateController _updates;
        private readonly List<NavItem> _nav = new List<NavItem>();
        private readonly int _sidebarWidth;
        private readonly List<Control> _pages = new List<Control>();

        private Panel _pageHost;
        private Panel _navPanel;
        private StatusPill _sharingPill;
        private LinkLabel _version;
        private TableLayoutPanel _banner;
        private Label _bannerLabel;
        private ThemedButton _bannerUpdate;
        private Version _bannerDismissed;
        private Toast _toast;
        private SettingsPage _settingsPage;
        private int _current = -1;
        private readonly Timer _prepare = new Timer { Interval = 150 };

        public MainForm(AppPaths paths, ConfigStore store, AppConfig config, HostController host, ClientController client,
            UpdateController updates)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            _paths = paths;
            _store = store;
            _config = config;
            _host = host;
            _client = client;
            _updates = updates;

            AutoScaleMode = AutoScaleMode.None;   // sizes come from Ui.S, fonts are in points
            Text = Strings.AppName;
            Icon = AppIcon.Window;
            StartPosition = FormStartPosition.CenterScreen;
            Font = ThemeFonts.Text;
            KeyPreview = true;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Ui.S(1220), screen.Width - Ui.S(40)), Math.Min(Ui.S(800), screen.Height - Ui.S(60)));
            _sidebarWidth = SidebarWidthFor(Strings.NavOffice, Strings.TabShare, Strings.TabJobs, Strings.TabSettings);
            Log.Info("Side menu: " + _sidebarWidth + " px wide (at least " + Ui.S(SidebarWidth) + ").");
            // The pages keep the room they had with 0.5.4's 232 px menu at the smallest window.
            MinimumSize = new Size(Math.Min(Ui.S(920 - 232) + _sidebarWidth, screen.Width), Math.Min(Ui.S(600), screen.Height));

            Build(PageId.Office);
            _prepare.Tick += (s, e) => PrepareNextPage();
            _host.SharingChanged += OnSharingChanged;
            _updates.Changed += OnUpdatesChanged;
            ThemeManager.Changed += OnThemeChanged;
        }

        /// <summary>
        /// Brings the window up (from the tray, at start, from a second copy of PrintVect). Windows puts a window on the
        /// screen before it has drawn what is inside it, so opening it showed a moment of empty white boxes (owner's
        /// photo, 0.5.1). The window is now kept off the screen (cloaked) until everything in it is drawn.
        /// </summary>
        public void Reveal()
        {
            bool appearing = !Visible || WindowState == FormWindowState.Minimized;
            bool cloaked = appearing && NativeTheme.Cloak(this, true);
            try
            {
                if (!Visible)
                {
                    Show();
                }
                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                }
                if (cloaked)
                {
                    NativeTheme.DrawNow(this);
                }
            }
            finally
            {
                if (cloaked) NativeTheme.Cloak(this, false);
            }
            Activate();
            BringToFront();
        }

        /// <summary>Opens the Settings page, where the Updates card lives (the update balloon leads here).</summary>
        public void ShowSettings()
        {
            Go(PageId.Settings);
        }

        public void Say(string text)
        {
            if (_toast != null && !_toast.IsDisposed) _toast.Say(text);
        }

        public void Go(PageId page)
        {
            if (!IsDisposed) Select((int)page);
        }

        public void StartUpdate()
        {
            if (_settingsPage != null && !_settingsPage.IsDisposed) _settingsPage.StartUpdate();
        }

        private void Build(PageId page)
        {
            SuspendLayout();
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            _nav.Clear();
            _pages.Clear();
            _current = -1;

            var main = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background };
            _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background };
            _toast = new Toast(MessageDisplaySeconds);
            _pageHost.Controls.Add(_toast);
            _pageHost.Resize += (s, e) =>
            {
                if (_current >= 0) _pages[_current].Bounds = _pageHost.DisplayRectangle;   // only the open page follows the window
                _toast.Place();
                if (Visible)
                {
                    _prepare.Stop();   // the others catch up once the resizing has stopped
                    _prepare.Start();
                }
            };

            var office = new OfficePrintersPage(_client, this);
            var share = new SharePage(_paths, _store, _config, _host, _client, this);
            var jobs = new JobsPage(_host, _client, _config, this);
            _settingsPage = new SettingsPage(_paths, _store, _config, _host, _client, _updates, this);

            Control sidebar = CreateSidebar();
            AddPage(Strings.NavOffice, Glyph.Printer, office);
            AddPage(Strings.TabShare, Glyph.Share, share);
            AddPage(Strings.TabJobs, Glyph.Jobs, jobs);
            AddPage(Strings.TabSettings, Glyph.Settings, _settingsPage);

            main.Controls.Add(_pageHost);
            main.Controls.Add(CreateBanner());
            Controls.Add(main);
            Controls.Add(sidebar);
            Ui.Solidify(this);
            _toast.BringToFront();
            UpdateSharingStatus();
            RefreshUpdateState();
            Select((int)page);
            ResumeLayout(true);
        }

        /// <summary>
        /// 248 px, or wider where Windows draws a page name wider than that (0.5.6: on the owner's PC the open page's
        /// "Printers on other PCs" still ended in "…" at 248 px, a few pixels more than measured on Linux).
        /// </summary>
        private static int SidebarWidthFor(params string[] titles)
        {
            int widest = 0;
            foreach (string title in titles)
            {
                widest = Math.Max(widest, NavItem.WidthToFit(title));
            }
            return Math.Max(Ui.S(SidebarWidth), widest + 2 * Ui.S(SidebarSidePadding));
        }

        private Control CreateSidebar()
        {
            var bar = new SidePanel { Dock = DockStyle.Left, Width = _sidebarWidth, BackColor = Palette.Sidebar,
                                      Padding = Ui.Pad(SidebarSidePadding, 20, SidebarSidePadding, 18) };

            var brand = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Margin = Ui.Pad(0), Padding = Ui.Pad(6, 0, 0, 0), BackColor = Palette.Sidebar };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var logo = new IconTile(Glyph.Printer, 34, 20, Palette.Accent, Palette.OnAccent) { Margin = Ui.Pad(0, 0, 10, 0) };
            Label name = Make.Label(Strings.AppName, ThemeFonts.Brand, Palette.Text, Ui.Pad(0));
            name.Anchor = AnchorStyles.Left;
            brand.Controls.Add(logo, 0, 0);
            brand.Controls.Add(name, 1, 0);

            // This PC: its name and whether it shares its printers, on every page.
            _sharingPill = new StatusPill(Strings.StatusSharingOff, PillKind.Neutral);
            Label pcName = Make.Label(Environment.MachineName, ThemeFonts.MonoName, Palette.Text, Ui.Pad(0));
            TableLayoutPanel thisPc = Make.Stack(6, Make.Caption(Strings.ShareThisPcCaption), pcName, _sharingPill);
            Card pcCard = Make.Card(thisPc, 12, 12);
            pcCard.Radius = 8;
            pcCard.Dock = DockStyle.Top;

            var items = new Panel { Dock = DockStyle.Top, BackColor = Palette.Sidebar, Height = 4 * Ui.S(NavItem.HeightPx) };
            _navPanel = items;

            _version = Make.Link("", Palette.Faint, () => Go(PageId.Settings), ThemeFonts.Tiny);
            _version.Dock = DockStyle.Fill;
            _version.LinkBehavior = LinkBehavior.HoverUnderline;
            var versionLine = new Panel { Dock = DockStyle.Bottom, Height = _version.PreferredHeight + Ui.S(2), Padding = Ui.Pad(8, 0, 0, 0), BackColor = Palette.Sidebar };
            versionLine.Controls.Add(_version);

            bar.Controls.Add(items);
            bar.Controls.Add(Spacer(22));
            bar.Controls.Add(pcCard);
            bar.Controls.Add(Spacer(22));
            bar.Controls.Add(brand);
            bar.Controls.Add(versionLine);
            return bar;
        }

        private static Control Spacer(int heightPx)
        {
            return new Panel { Dock = DockStyle.Top, Height = Ui.S(heightPx), BackColor = Palette.Sidebar };
        }

        private void AddPage(string title, Glyph glyph, Control page)
        {
            int index = _pages.Count;
            var item = new NavItem(title, glyph);
            item.Click += (s, e) => Select(index);
            _nav.Add(item);
            _navPanel.Controls.Add(item);
            item.BringToFront();   // docked top: the last one added sits lowest
            _pages.Add(page);
            page.Visible = false;   // until it is first opened
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
                _nav[i].Active = i == index;
            }
            NativeTheme.DrawNow(_navPanel);   // the click shows at once, before the page is ready
            Control page = _pages[index];
            Control old = _current >= 0 ? _pages[_current] : null;
            if (old != null && old.ContainsFocus)
            {
                _nav[index].Focus();   // else Windows hands the focus to a box on the new page and scrolls it there
            }
            // A page that was hidden lays out every row again when it is shown, which took long enough to notice after
            // each click (owner, 0.5.1). So a page opened once stays shown behind the open one, switched off (no Tab,
            // no keys), and opening it again only brings it to the front. Drawing waits until the page is finished, so
            // a page opened for the first time or at a new window size does not jump while it is laid out.
            using (NativeTheme.FreezeDrawing(_pageHost))
            {
                if (page.Bounds != _pageHost.DisplayRectangle) page.Bounds = _pageHost.DisplayRectangle;
                page.Enabled = true;
                page.Visible = true;
                page.BringToFront();
                _toast.BringToFront();
                _current = index;
                if (old != null)
                {
                    old.Enabled = false;
                    ((IPage)old).SetOpen(false);
                }
                ((IPage)page).SetOpen(Visible);
            }
        }

        /// <summary>
        /// Lays out one page that is not ready yet (never shown, or sized for an earlier window size) behind the open one,
        /// switched off and out of sight, so that the first click on it is as quick as the next ones. A timer runs this
        /// only when the window has nothing else to do (Windows handles clicks, keys and drawing before timers), one page
        /// per tick, after the window opens and after it is resized.
        /// </summary>
        private void PrepareNextPage()
        {
            if (IsDisposed || !Visible || _current < 0)
            {
                _prepare.Stop();
                return;
            }
            for (int i = 0; i < _pages.Count; i++)
            {
                Control page = _pages[i];
                if (i == _current || (page.Visible && page.Bounds == _pageHost.DisplayRectangle)) continue;
                page.Enabled = false;
                page.Bounds = _pageHost.DisplayRectangle;
                page.Visible = true;
                _pages[_current].BringToFront();
                _toast.BringToFront();
                return;
            }
            _prepare.Stop();
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / PageUp move between pages, like the old tabs.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int count = _pages.Count;
            if (count > 0 && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown)))
            {
                Select((_current + 1) % count);
                return true;
            }
            if (count > 0 && (keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp)))
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
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Palette.WarningBg,
                Padding = Ui.Pad(36, 10, 36, 10),
                Visible = false
            };
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var icon = new GlyphBox(Glyph.Info, 18, Palette.WarningText) { Anchor = AnchorStyles.Left, Margin = Ui.Pad(0, 0, 10, 0) };
            _bannerLabel = Make.Label("", ThemeFonts.TextSemi, Palette.WarningText, Ui.Pad(0, 0, 8, 0));
            _bannerLabel.Anchor = AnchorStyles.Left;
            _bannerUpdate = new ThemedButton(Strings.SettingsUpdateNow, ButtonKind.Primary) { Anchor = AnchorStyles.None };
            _bannerUpdate.Click += (s, e) => StartUpdate();
            var later = new ThemedButton(Strings.UpdateBannerLater, ButtonKind.Secondary) { Anchor = AnchorStyles.None, Margin = Ui.Pad(0) };
            later.Click += (s, e) => DismissBanner();

            _banner.Controls.Add(icon, 0, 0);
            _banner.Controls.Add(_bannerLabel, 1, 0);
            _banner.Controls.Add(_bannerUpdate, 2, 0);
            _banner.Controls.Add(later, 3, 0);
            return _banner;
        }

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) UpdateSharingStatus();
        }

        private void UpdateSharingStatus()
        {
            bool on = _host.IsSharing;
            _sharingPill.Set(on ? Strings.StatusSharingOn : Strings.StatusSharingOff, on ? PillKind.Success : PillKind.Neutral);
        }

        private void OnUpdatesChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) RefreshUpdateState();
        }

        /// <summary>The version line under the menu and the update strip above the page.</summary>
        private void RefreshUpdateState()
        {
            if (_banner == null) return;
            ReleaseInfo available = _updates.Available;
            UpdateCheckResult last = _updates.LastResult;
            string state = available != null ? Strings.NavVersionUpdate
                : last != null && last.Outcome == UpdateCheckOutcome.UpToDate ? Strings.NavVersionLatest : null;
            _version.Text = string.Format(state == null ? Strings.StatusVersion : Strings.NavVersionLine, AppInfo.Version, state);
            _version.LinkColor = available != null ? Palette.AccentText : Palette.Faint;
            _version.ActiveLinkColor = _version.LinkColor;

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
            RefreshUpdateState();
        }

        /// <summary>New colours: build the whole window again on the same page (after the click that chose them).</summary>
        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (!IsHandleCreated)
            {
                Rebuild();
                return;
            }
            BeginInvoke((Action)Rebuild);
        }

        private void Rebuild()
        {
            if (IsDisposed) return;
            PageId page = _current < 0 ? PageId.Office : (PageId)_current;
            Log.Info("Colours changed: building the window again (page " + page + ").");
            if (_current >= 0) ((IPage)_pages[_current]).SetOpen(false);
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (Control c in old) c.Dispose();
            Build(page);
            ResumeLayout(true);
            NativeTheme.ApplyTitleBar(this, true);
            Invalidate(true);
            if (Visible)
            {
                LoadDiagnostics();
                _prepare.Start();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                // The pages behind the open one are hidden with the window, so showing it again lays out the open page
                // only; the others are laid out again behind it afterwards (PrepareNextPage).
                for (int i = 0; i < _pages.Count; i++)
                {
                    if (i != _current) _pages[i].Visible = false;
                }
            }
            if (_current >= 0) ((IPage)_pages[_current]).SetOpen(Visible);
            if (Visible)
            {
                LoadDiagnostics();
                _prepare.Start();
            }
            else
            {
                _prepare.Stop();
            }
        }

        private void LoadDiagnostics()
        {
            if (_settingsPage != null && !_settingsPage.IsDisposed) _settingsPage.LoadDiagnostics();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyTitleBar(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _prepare.Dispose();
                ThemeManager.Changed -= OnThemeChanged;
                _host.SharingChanged -= OnSharingChanged;
                _updates.Changed -= OnUpdatesChanged;
            }
            base.Dispose(disposing);
        }

        /// <summary>The side menu: its colour with a 1 px line along its right edge.</summary>
        private sealed class SidePanel : Panel
        {
            public SidePanel()
            {
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(Palette.Border))
                {
                    e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
                }
            }
        }
    }
}
