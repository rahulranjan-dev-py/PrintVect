using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Theme;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Discovery;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Office printers" (was "Use shared printers"; the redesign of 0.5.0): the printers from other PCs already on this
    /// PC, one card row each with its status, waiting jobs and Test page / Retry waiting / Remove; the printers found in
    /// the office that are not added yet (UDP discovery runs while this page is visible); and Find a PC by name.
    /// </summary>
    internal sealed class OfficePrintersPage : PageScroller
    {
        private readonly ClientController _controller;
        private readonly IShell _shell;
        private readonly TableLayoutPanel _mineRows;
        private readonly StatusPill _mineCount;
        private readonly TableLayoutPanel _foundRows;
        private readonly Label _lastChecked;
        private readonly InputBox _hostBox;
        private readonly ThemedButton _lookup;
        private readonly Label _lookupStatus;
        private readonly Card _result;
        private readonly TableLayoutPanel _resultRows;
        private readonly System.Windows.Forms.Timer _clock;
        private readonly List<ThemedButton> _addButtons = new List<ThemedButton>();
        private CancellationTokenSource _lookupCancel;
        private ListReply _lookedUp;
        private string _lookedUpAddress;
        private DateTime _shownAt;
        private bool _busy;
        private string _mineShown;
        private string _foundShown;

        public OfficePrintersPage(ClientController controller, IShell shell)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;
            _shell = shell;

            Add(Make.Stack(6, Make.PageTitle(Strings.NavOffice), Make.Intro(Strings.OfficeIntro)), 0);

            // Added to this PC
            _mineCount = new StatusPill("0", PillKind.Count) { Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 0, 0) };
            Add(Heading(Make.SectionTitle(Strings.OfficeAdded), _mineCount, null), 28);
            _mineRows = Make.Stack(0);
            Add(Make.Card(_mineRows, 0, 0), 12);

            // Found in the office, not added yet
            _lastChecked = Make.Label("", ThemeFonts.Secondary, Palette.Faint, Ui.Pad(0, 0, 12, 0));
            _lastChecked.Anchor = AnchorStyles.Left;
            var searchAgain = new ThemedButton(Strings.OfficeSearchAgain, ButtonKind.Secondary, Glyph.Retry) { Margin = Ui.Pad(0) };
            searchAgain.Click += (s, e) =>
            {
                _controller.SearchNow();
                _lastChecked.Text = string.Format(Strings.OfficeLastChecked, DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture));
            };
            FlowLayoutPanel right = Make.Row(_lastChecked, searchAgain);
            right.WrapContents = false;
            Add(Heading(Make.SectionTitle(Strings.OfficeFound), null, right), 28);
            _foundRows = Make.Stack(0);
            Add(Make.Card(_foundRows, 0, 0), 12);

            // Find a PC by name
            Add(Make.SectionTitle(Strings.OfficeFind), 28);
            _hostBox = new InputBox(420) { Margin = Ui.Pad(0, 6, 12, 0) };
            _hostBox.Box.AccessibleName = Strings.OfficeFindLabel;
            _hostBox.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    LookUp();
                }
            };
            _lookup = new ThemedButton(Strings.UseLookup, ButtonKind.Primary, Glyph.Search) { Margin = Ui.Pad(0, 6, 0, 0) };
            _lookup.Click += (s, e) => LookUp();
            Label boxLabel = Make.Label(Strings.OfficeFindLabel, ThemeFonts.SecondarySemi, Palette.Text, Ui.Pad(0));
            var field = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, Margin = Ui.Pad(0), BackColor = Color.Transparent };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            field.Controls.Add(boxLabel, 0, 0);
            field.SetColumnSpan(boxLabel, 2);
            field.Controls.Add(_hostBox, 0, 1);
            field.Controls.Add(_lookup, 1, 1);
            _lookup.Anchor = AnchorStyles.Left;
            _lookup.Margin = Ui.Pad(0, 6, 0, 0);
            _lookupStatus = Make.Muted("");
            _lookupStatus.Visible = false;
            _resultRows = Make.Stack(0);
            _result = Card.Nested();
            _result.AutoSize = true;
            _resultRows.Dock = DockStyle.Fill;
            _result.Controls.Add(_resultRows);
            _result.Visible = false;
            var parts = new List<Control> { field, _lookupStatus, _result };
            Notice twoNetworks = TwoNetworksNotice();
            if (twoNetworks != null) parts.Add(twoNetworks);
            Add(Make.Card(Make.Stack(16, parts.ToArray()), 20, 20), 12);

            _clock = new System.Windows.Forms.Timer { Interval = 5000 };
            _clock.Tick += (s, e) => Tick();

            _controller.JobChanged += OnJobChanged;
            _controller.PrintersChanged += OnPrintersChanged;
            _controller.FoundChanged += OnFoundChanged;
            FillMine();
            FillFound();
            Tick();
        }

        /// <summary>A section title with a count pill after it, or with something on the far right.</summary>
        private static Control Heading(Control title, Control afterTitle, Control farRight)
        {
            FlowLayoutPanel left = Make.Row(title, afterTitle);
            left.WrapContents = false;
            title.Anchor = AnchorStyles.Left;
            if (farRight == null) return left;
            return Make.Heading(left, farRight);
        }

        /// <summary>Discovery only runs while this page can be seen (brief, section 6).</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            bool visible = Visible && !IsDisposed;
            try
            {
                _controller.SetDiscoveryActive(visible);
            }
            catch (Exception ex)
            {
                Log.Error("Discovery could not be switched " + (visible ? "on" : "off") + ".", ex);
            }
            if (visible)
            {
                _shownAt = DateTime.Now;
                _clock.Start();
                Tick();
            }
            else
            {
                _clock.Stop();
            }
        }

        /// <summary>Every few seconds while visible: "Last checked" and the Looking… / Not answering pills.</summary>
        private void Tick()
        {
            DateTime? last = _controller.LastSearchAt;
            _lastChecked.Text = last == null ? "" : string.Format(Strings.OfficeLastChecked, last.Value.ToString("HH:mm", CultureInfo.CurrentCulture));
            if (DateTime.Now - _shownAt < TimeSpan.FromSeconds(25)) FillMine();   // "Looking…" turns into "Not answering" after 12 s
        }

        // ---- Added to this PC ----

        /// <summary>Builds the rows again only when something they show changed, so a click is never lost to a rebuild.</summary>
        private void FillMine()
        {
            IList<RemotePrinter> printers = _controller.Printers;
            IList<DiscoveredHost> found = _controller.FoundHosts;
            string shown = string.Join("|", printers.Select(p => p.PrinterId + "," + p.FriendlyName + "," + p.HostName + ","
                + _controller.PendingCount(p) + "," + PillOf(p, found).Item1)) + "|" + _busy;
            if (shown == _mineShown) return;
            _mineShown = shown;
            Rebuild(_mineRows, printers.Count == 0
                ? new List<Control> { EmptyRow(Glyph.Printer, null, Strings.OfficeNoneAdded) }
                : printers.Select(p => MineRow(p, found)).ToList());
            _mineCount.Set(printers.Count.ToString(CultureInfo.CurrentCulture), PillKind.Count);
        }

        /// <summary>A printer's pill: its status as its PC answered, "Looking…" while the search is young, else "Not answering".</summary>
        private Tuple<string, PillKind> PillOf(RemotePrinter printer, IList<DiscoveredHost> found)
        {
            PrinterInfo seen = found.SelectMany(h => h.Printers).FirstOrDefault(p => string.Equals(p.Id, printer.PrinterId, StringComparison.OrdinalIgnoreCase));
            if (seen != null)
            {
                return Tuple.Create(Texts.PrinterStatus(seen.Status), Texts.PrinterPill(seen.Status));
            }
            bool young = DateTime.Now - _shownAt < TimeSpan.FromSeconds(12) || !_controller.IsDiscovering;
            return Tuple.Create(young ? Strings.OfficeChecking : Strings.OfficeNotAnswering, PillKind.Neutral);
        }

        private Control MineRow(RemotePrinter printer, IList<DiscoveredHost> found)
        {
            int waiting = _controller.PendingCount(printer);
            string name = string.IsNullOrWhiteSpace(printer.FriendlyName) ? printer.LocalPrinterName : printer.FriendlyName;

            Tuple<string, PillKind> status = PillOf(printer, found);
            var pill = new StatusPill(status.Item1, status.Item2) { Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 10, 0) };
            Label waits = Make.Label(string.Format(Strings.OfficeWaiting, waiting), ThemeFonts.Secondary,
                waiting > 0 ? Palette.WarningText : Palette.Muted, Ui.Pad(0));
            waits.Anchor = AnchorStyles.Left;
            Label title = Make.Label(name, ThemeFonts.RowTitle, Palette.Text, Ui.Pad(0));
            title.Anchor = AnchorStyles.Left;
            FlowLayoutPanel top = Make.Row(title, pill, waits);

            Control by = Mixed(Strings.OfficeSharedBy, printer.HostName, Palette.Muted, Palette.Text);
            Control inWindows = Mixed(Strings.OfficeInPrintWindows, printer.LocalPrinterName, Palette.Faint, Palette.Faint);
            TableLayoutPanel text = Make.Stack(4, top, by, inWindows);

            var test = new ThemedButton(Strings.OfficeTestPage, ButtonKind.Secondary, Glyph.Page);
            test.Click += (s, e) => SendTestPage(printer);
            var retry = new ThemedButton(Strings.OfficeRetry, ButtonKind.Secondary, Glyph.Retry) { Enabled = waiting > 0 };
            retry.Click += (s, e) => Retry(printer);
            var remove = new ThemedButton(Strings.UseRemove, ButtonKind.Danger, Glyph.Trash) { Margin = Ui.Pad(0) };
            remove.AccessibleName = string.Format(Strings.OfficeRemoveAccessible, name);
            remove.Click += (s, e) => Remove(printer);
            FlowLayoutPanel buttons = Make.Row(test, retry, remove);
            buttons.WrapContents = false;

            var tile = new IconTile(Glyph.Printer, 42, 22, Palette.SuccessBg, Palette.Accent);
            return new SplitRow(tile, text, buttons, 260, 20);
        }

        /// <summary>"Shared by " in normal text and the PC name in the typewriter font, on one line.</summary>
        private static Control Mixed(string label, string mono, Color labelColour, Color monoColour)
        {
            Label a = Make.Label(label + " ", ThemeFonts.Secondary, labelColour, Ui.Pad(0));
            Label b = Make.Label(mono ?? "", ThemeFonts.Mono, monoColour, Ui.Pad(0, 1, 0, 0));
            return Make.Row(a, b);
        }

        private void SendTestPage(RemotePrinter printer)
        {
            try
            {
                _controller.SendTestPage(printer);
                _shell.Say(string.Format(Strings.UseTestPageSent, printer.LocalPrinterName));
            }
            catch (FileNotFoundException ex)
            {
                Log.Warn(ex.Message);
                MessageBox.Show(FindForm(), ex.Message, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("The test page could not be handed over.", ex);
                _shell.Say(ex.Message);
            }
        }

        private void Retry(RemotePrinter printer)
        {
            try
            {
                int count = _controller.RetryPending(printer);
                _shell.Say(count == 0 ? string.Format(Strings.UseNothingWaiting, printer.LocalPrinterName) : string.Format(Strings.UseRetried, count));
                FillMine();
            }
            catch (Exception ex)
            {
                Log.Error("Retry failed.", ex);
                _shell.Say(ex.Message);
            }
        }

        private async void Remove(RemotePrinter printer)
        {
            if (_busy) return;
            DialogResult answer = MessageBox.Show(FindForm(), string.Format(Strings.UseRemoveConfirm, printer.LocalPrinterName), Strings.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            SetBusy(true);
            try
            {
                ElevateResult result = await _controller.RemovePrinterAsync(printer);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    _shell.Say(string.Format(Strings.UseRemoved, printer.LocalPrinterName));
                    FillFound();
                }
                else
                {
                    MessageBox.Show(FindForm(), string.Format(Strings.UseRemoveFailed, result.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Removing the printer failed.", ex);
                if (!IsDisposed) _shell.Say(string.Format(Strings.UseRemoveFailed, ex.Message));
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        // ---- Found in the office ----

        private void FillFound()
        {
            IList<DiscoveredHost> hosts = _controller.FoundHosts
                .Where(h => !string.Equals(h.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)).ToList();
            string shown = string.Join("|", hosts.SelectMany(h => h.Printers.Select(p => h.Host + "," + p.Id + "," + p.Friendly + "," + p.Status
                + "," + (_controller.FindPrinter(p.Id) != null)))) + "|" + _busy;
            if (shown != _foundShown)
            {
                _foundShown = shown;
                var rows = new List<Control>();
                int printers = 0;
                foreach (DiscoveredHost host in hosts)
                {
                    foreach (PrinterInfo printer in host.Printers)
                    {
                        printers++;
                        if (_controller.FindPrinter(printer.Id) == null)
                        {
                            rows.Add(FoundRow(host, printer));
                        }
                    }
                }
                if (rows.Count == 0)
                {
                    rows.Add(hosts.Count == 0
                        ? EmptyRow(Glyph.Search, Strings.OfficeNoneFound, Strings.OfficeNoneFoundText)
                        : EmptyRow(Glyph.Check, Strings.OfficeNothingNew, string.Format(Strings.OfficeNothingNewText, printers, hosts.Count)));
                }
                Rebuild(_foundRows, rows);
            }
            FillResult();
        }

        private Control FoundRow(DiscoveredHost host, PrinterInfo printer)
        {
            string name = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
            Label title = Make.Label(name, ThemeFonts.RowTitle, Palette.Text, Ui.Pad(0));
            title.Anchor = AnchorStyles.Left;
            var pill = new StatusPill(Texts.PrinterStatus(printer.Status), Texts.PrinterPill(printer.Status)) { Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 0, 0) };
            TableLayoutPanel text = Make.Stack(4, Make.Row(title, pill), Mixed(Strings.OfficeSharedBy, host.Host, Palette.Muted, Palette.Text));
            ThemedButton add = AddButton(() => AddAsync(host.ToListReply(), printer, host.Ip));
            var tile = new IconTile(Glyph.Printer, 42, 22, Palette.NeutralBg, Palette.Muted);
            return new SplitRow(tile, text, add, 240, 20);
        }

        private ThemedButton AddButton(Action add)
        {
            var button = new ThemedButton(Strings.UseAdd, ButtonKind.Primary, Glyph.Plus) { Margin = Ui.Pad(0), Enabled = !_busy };
            button.Click += (s, e) => add();
            _addButtons.RemoveAll(b => b.IsDisposed);
            _addButtons.Add(button);
            return button;
        }

        private static Control EmptyRow(Glyph glyph, string title, string text)
        {
            var tile = new IconTile(glyph, 40, 20, Palette.NeutralBg, Palette.NeutralText, true, 20, 2f);
            Control body = title == null
                ? (Control)Make.Wrap(text, ThemeFonts.Text, Palette.Muted, Ui.Pad(0))
                : Make.Stack(2, Make.Label(title, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)), Make.Wrap(text, ThemeFonts.Text, Palette.Muted, Ui.Pad(0)));
            return new SplitRow(tile, body, null, 200, 16);
        }

        /// <summary>Replaces the rows of a card: 18 / 20 px inside each, a line between them.</summary>
        private static void Rebuild(TableLayoutPanel rows, IList<Control> content)
        {
            rows.SuspendLayout();
            foreach (Control old in rows.Controls.Cast<Control>().ToList())
            {
                old.Dispose();
            }
            rows.Controls.Clear();
            rows.RowStyles.Clear();
            rows.RowCount = 0;
            for (int i = 0; i < content.Count; i++)
            {
                if (i > 0) AddRow(rows, Make.Divider(), new Padding(Ui.S(20), 0, Ui.S(20), 0));
                AddRow(rows, content[i], new Padding(Ui.S(20), Ui.S(i == 0 ? 12 : 18), Ui.S(20), Ui.S(i == content.Count - 1 ? 12 : 18)));
            }
            Ui.Solidify(rows);
            rows.ResumeLayout(true);
        }

        private static void AddRow(TableLayoutPanel rows, Control control, Padding margin)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = margin;
            rows.RowCount++;
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.Controls.Add(control, 0, rows.RowCount - 1);
        }

        // ---- Find a PC by name ----

        private async void LookUp()
        {
            string typed = _hostBox.Box.Text.Trim();
            if (typed.Length == 0 || _busy)
            {
                return;
            }
            SetBusy(true);
            SetStatus(string.Format(Strings.UseLookingUp, typed));
            _lookupCancel = new CancellationTokenSource();
            try
            {
                ListReply reply = await _controller.LookupHostAsync(typed, _lookupCancel.Token);
                if (IsDisposed) return;
                string host = string.IsNullOrEmpty(reply.Host) ? typed : reply.Host;
                _lookedUp = reply;
                _lookedUpAddress = typed;
                SetStatus(reply.Printers.Count == 0 ? string.Format(Strings.UseNoPrintersShared, host) : "");
                Log.Info("Look-up of " + typed + ": " + reply.Printers.Count + " printer(s) on " + host + ".");
                FillFound();
            }
            catch (Exception ex)
            {
                Log.Warn("Look-up of " + typed + " failed: " + ex.Message);
                if (!IsDisposed)
                {
                    _lookedUp = null;
                    SetStatus(string.Format(Strings.UseLookupFailed, ex.Message));
                    FillResult();
                }
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetStatus(string text)
        {
            _lookupStatus.Text = text ?? "";
            _lookupStatus.Visible = _lookupStatus.Text.Length > 0;
        }

        /// <summary>The box under Look up: the PC that answered and each of its printers, with Add or "Already on this PC".</summary>
        private void FillResult()
        {
            if (_lookedUp == null || _lookedUp.Printers.Count == 0)
            {
                _result.Visible = false;
                return;
            }
            string host = string.IsNullOrEmpty(_lookedUp.Host) ? _lookedUpAddress : _lookedUp.Host;
            var rows = new List<Control>();
            Label hostName = Make.Label(host, ThemeFonts.MonoSemi, Palette.Text, Ui.Pad(0, 1, 0, 0));
            Label shares = Make.Label(" " + string.Format(Strings.OfficeShares, _lookedUp.Printers.Count), ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            rows.Add(Make.Row(hostName, shares));
            foreach (PrinterInfo printer in _lookedUp.Printers)
            {
                string name = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
                Control side;
                if (_controller.FindPrinter(printer.Id) != null)
                {
                    var tick = new GlyphBox(Glyph.Check, 16, Palette.AccentText, 2.2f) { Anchor = AnchorStyles.Left, Margin = Ui.Pad(0, 0, 6, 0) };
                    Label already = Make.Label(Strings.OfficeAlready, ThemeFonts.SecondarySemi, Palette.AccentText, Ui.Pad(0, 0, 12, 0));
                    already.Anchor = AnchorStyles.Left;
                    var added = new ThemedButton(Strings.OfficeAddedButton, ButtonKind.Secondary) { Enabled = false, Margin = Ui.Pad(0) };
                    FlowLayoutPanel flow = Make.Row(tick, already, added);
                    flow.WrapContents = false;
                    side = flow;
                }
                else
                {
                    PrinterInfo chosen = printer;
                    ListReply reply = _lookedUp;
                    string typed = _lookedUpAddress;
                    side = AddButton(() => AddAsync(reply, chosen, typed));
                }
                rows.Add(new SplitRow(null, Make.Label(name, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)), side, 160, 12));
            }
            _resultRows.SuspendLayout();
            foreach (Control old in _resultRows.Controls.Cast<Control>().ToList()) old.Dispose();
            _resultRows.Controls.Clear();
            _resultRows.RowStyles.Clear();
            _resultRows.RowCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) AddRow(_resultRows, Make.Divider(), new Padding(0));
                AddRow(_resultRows, rows[i], new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10)));
            }
            Ui.Solidify(_resultRows);
            _resultRows.ResumeLayout(true);
            _result.Visible = true;
        }

        private static Notice TwoNetworksNotice()
        {
            List<string> cards = LocalNetworks.List().Select(n => n.InterfaceName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cards.Count < 2) return null;
            string names = cards.Count == 2 ? cards[0] + " " + Strings.WordAnd + " " + cards[1] : string.Join(", ", cards);
            return new Notice(NoticeKind.Info, Glyph.Info, string.Format(Strings.OfficeTwoNetworks, cards.Count, names));
        }

        private async void AddAsync(ListReply host, PrinterInfo printer, string address)
        {
            if (_busy) return;
            string friendly = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
            SetBusy(true);
            _shell.Say(string.Format(Strings.UseAdding, friendly));
            try
            {
                ElevateResult result = await _controller.AddPrinterAsync(host, printer, address);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    RemotePrinter added = _controller.FindPrinter(printer.Id);
                    _shell.Say(added == null ? result.Message : string.Format(Strings.UseAdded, added.LocalPrinterName, added.HostName));
                    FillFound();
                }
                else
                {
                    MessageBox.Show(FindForm(), string.Format(Strings.UseAddFailed, result.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Adding the printer failed.", ex);
                if (!IsDisposed) _shell.Say(string.Format(Strings.UseAddFailed, ex.Message));
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _mineShown = null;
            _foundShown = null;
            _lookup.Enabled = !busy;
            foreach (ThemedButton button in _addButtons.Where(b => !b.IsDisposed)) button.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void OnFoundChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            FillFound();
            FillMine();   // the status pills come from what the hosts answered
        }

        private void OnPrintersChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            FillMine();
            FillFound();
        }

        private void OnJobChanged(object sender, ClientJobRecord record)
        {
            if (!IsDisposed && ClientJobStates.IsFinal(record.State))
            {
                FillMine();   // the waiting count of that printer may have changed
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.JobChanged -= OnJobChanged;
                _controller.PrintersChanged -= OnPrintersChanged;
                _controller.FoundChanged -= OnFoundChanged;
                _clock.Dispose();
                if (_lookupCancel != null)
                {
                    _lookupCancel.Cancel();
                    _lookupCancel.Dispose();
                    _lookupCancel = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
