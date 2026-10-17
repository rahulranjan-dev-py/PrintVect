using System;
using System.Collections.Generic;
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
    /// "Use shared printers" (brief, section 7; the Printers screen of the design): the printers found in the office
    /// by themselves (UDP discovery runs while this page is visible) or by a typed PC name, Add to this PC (virtual
    /// printer through the Elevate helper), and the printers from other PCs already on this PC with Remove, Send test
    /// page and Retry waiting jobs. The jobs sent are on the Jobs page.
    /// </summary>
    internal sealed class UseTab : UserControl
    {
        private readonly ClientController _controller;
        private Label _foundStatus;
        private DataGridView _found;
        private ThemedButton _add;
        private InputBox _hostBox;
        private ThemedButton _lookup;
        private Label _lookupStatus;
        private DataGridView _mine;
        private ThemedButton _remove;
        private ThemedButton _testPage;
        private ThemedButton _retry;
        private Label _mineHint;
        private CancellationTokenSource _lookupCancel;
        private bool _busy;

        private sealed class FoundEntry
        {
            public DiscoveredHost Host;
            public PrinterInfo Printer;
        }

        public UseTab(ClientController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;
            BackColor = Palette.Background;
            BuildLayout();
            _controller.JobChanged += OnJobChanged;
            _controller.PrintersChanged += OnPrintersChanged;
            _controller.FoundChanged += OnFoundChanged;
            FillFound();
            FillMine();
        }

        private void BuildLayout()
        {
            SuspendLayout();
            TableLayoutPanel layout = Make.Column(60, 40);
            layout.BackColor = Palette.Background;
            layout.Controls.Add(CreateFoundCard(), 0, 0);
            layout.Controls.Add(CreateMineCard(), 0, 1);
            Controls.Add(layout);
            ResumeLayout(true);
        }

        private Card CreateFoundCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0, 0, 0, 16) };
            TableLayoutPanel column = Make.Column(0, 0, 0, 100, 0, 0);

            _foundStatus = Make.Wrap("", ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0, 0, 0, 8));
            Label intro = Make.Muted(Strings.UseIntro);

            _found = Grids.Create();
            _found.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.UseColPrinter, 42), Grids.Text(Strings.UseColHost, 28, mono: true), Grids.Text(Strings.UseColStatus, 30)
            });
            _found.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) AddSelected();
            };

            _add = new ThemedButton(Strings.UseAdd, ButtonKind.Primary) { Margin = Ui.Pad(0, 0, 24, 0) };
            _add.Click += (s, e) => AddSelected();
            Label hostLabel = Make.Label(Strings.UseHostLabel, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0, 13, 8, 0));
            hostLabel.Dock = DockStyle.None;
            _hostBox = new InputBox(190);
            _hostBox.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    LookUp();
                }
            };
            _lookup = new ThemedButton(Strings.UseLookup, ButtonKind.Secondary);
            _lookup.Click += (s, e) => LookUp();
            _lookupStatus = Make.Wrap("", ThemeFonts.Secondary, Palette.Text, Ui.Pad(0, 10, 0, 0));

            column.Controls.Add(Make.Title(Strings.UseFoundLabel), 0, 0);
            column.Controls.Add(intro, 0, 1);
            column.Controls.Add(_foundStatus, 0, 2);
            column.Controls.Add(_found, 0, 3);
            column.Controls.Add(Make.Row(_add, hostLabel, _hostBox, _lookup), 0, 4);
            column.Controls.Add(_lookupStatus, 0, 5);
            card.Controls.Add(column);
            return card;
        }

        private Card CreateMineCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            TableLayoutPanel column = Make.Column(0, 100, 0, 0);

            _mine = Grids.Create();
            _mine.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.UseColLocalPrinter, 44), Grids.Text(Strings.UseColHost, 20, mono: true),
                Grids.Text(Strings.UseColAddress, 22, mono: true), Grids.Text(Strings.UseColWaiting, 14)
            });

            _remove = new ThemedButton(Strings.UseRemove, ButtonKind.Secondary);
            _remove.Click += (s, e) => RemoveSelected();
            _testPage = new ThemedButton(Strings.UseTestPage, ButtonKind.Secondary);
            _testPage.Click += (s, e) => SendTestPage();
            _retry = new ThemedButton(Strings.UseRetry, ButtonKind.Secondary);
            _retry.Click += (s, e) => RetrySelected();
            _mineHint = Make.Wrap("", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0, 10, 0, 0));

            column.Controls.Add(Make.Title(Strings.UseMyPrinters), 0, 0);
            column.Controls.Add(_mine, 0, 1);
            column.Controls.Add(Make.Row(_testPage, _retry, _remove), 0, 2);
            column.Controls.Add(_mineHint, 0, 3);
            card.Controls.Add(column);
            return card;
        }

        /// <summary>Discovery only runs while this page can be seen (brief, section 6).</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            try
            {
                _controller.SetDiscoveryActive(Visible && !IsDisposed);
            }
            catch (Exception ex)
            {
                Log.Error("Discovery could not be switched " + (Visible ? "on" : "off") + ".", ex);
            }
        }

        private void FillFound()
        {
            string selectedKey = SelectedFoundKey();
            IList<DiscoveredHost> hosts = _controller.FoundHosts;
            _found.Rows.Clear();
            DataGridViewRow reselect = null;
            int printers = 0;
            foreach (DiscoveredHost host in hosts)
            {
                if (string.Equals(host.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // this PC's own printers: it prints to them directly
                }
                foreach (PrinterInfo printer in host.Printers)
                {
                    printers++;
                    bool onThisPc = _controller.FindPrinter(printer.Id) != null;
                    string shown = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
                    string status = ShareTab.StatusText(printer.Status) + (onThisPc ? ", " + Strings.UseOnThisPc : "");
                    int index = _found.Rows.Add(shown, host.Host, status);
                    DataGridViewRow row = _found.Rows[index];
                    row.Tag = new FoundEntry { Host = host, Printer = printer };
                    if (onThisPc)
                    {
                        row.DefaultCellStyle.ForeColor = Palette.Muted;
                        row.DefaultCellStyle.SelectionForeColor = Palette.Muted;
                    }
                    if (FoundKey(host, printer) == selectedKey) reselect = row;
                }
            }
            Grids.SelectRow(_found, reselect);
            int otherPcs = hosts.Count(h => !string.Equals(h.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
            _foundStatus.Text = otherPcs == 0 ? Strings.UseFoundNone : string.Format(Strings.UseFoundCount, printers, otherPcs);
        }

        private static string FoundKey(DiscoveredHost host, PrinterInfo printer)
        {
            return host.Host + "|" + printer.Id;
        }

        private FoundEntry SelectedFound()
        {
            return _found.SelectedRows.Count == 0 ? null : _found.SelectedRows[0].Tag as FoundEntry;
        }

        private string SelectedFoundKey()
        {
            FoundEntry entry = SelectedFound();
            return entry == null ? null : FoundKey(entry.Host, entry.Printer);
        }

        private async void LookUp()
        {
            string typed = _hostBox.Box.Text.Trim();
            if (typed.Length == 0 || _busy)
            {
                return;
            }
            SetBusy(true);
            _lookupStatus.Text = string.Format(Strings.UseLookingUp, typed);
            _lookupCancel = new CancellationTokenSource();
            try
            {
                ListReply reply = await _controller.LookupHostAsync(typed, _lookupCancel.Token);
                if (IsDisposed) return;
                string host = string.IsNullOrEmpty(reply.Host) ? typed : reply.Host;
                _lookupStatus.Text = reply.Printers.Count == 0
                    ? string.Format(Strings.UseNoPrintersShared, host)
                    : string.Format(Strings.UseFoundPrinters, host, reply.Printers.Count);
                Log.Info("Look-up of " + typed + ": " + reply.Printers.Count + " printer(s) on " + host + ".");
                FillFound();
                SelectHost(host);
            }
            catch (Exception ex)
            {
                Log.Warn("Look-up of " + typed + " failed: " + ex.Message);
                if (!IsDisposed)
                {
                    _lookupStatus.Text = string.Format(Strings.UseLookupFailed, ex.Message);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SelectHost(string hostName)
        {
            foreach (DataGridViewRow row in _found.Rows)
            {
                var entry = (FoundEntry)row.Tag;
                if (string.Equals(entry.Host.Host, hostName, StringComparison.OrdinalIgnoreCase))
                {
                    Grids.SelectRow(_found, row);   // also scrolls it into view
                    return;
                }
            }
        }

        private async void AddSelected()
        {
            if (_busy) return;
            FoundEntry entry = SelectedFound();
            if (entry == null)
            {
                _lookupStatus.Text = Strings.UseSelectFound;
                return;
            }
            PrinterInfo printer = entry.Printer;
            string friendly = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
            SetBusy(true);
            _lookupStatus.Text = string.Format(Strings.UseAdding, friendly);
            try
            {
                ElevateResult result = await _controller.AddPrinterAsync(entry.Host.ToListReply(), printer, entry.Host.Ip);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    RemotePrinter added = _controller.FindPrinter(printer.Id);
                    _lookupStatus.Text = added == null ? result.Message : string.Format(Strings.UseAdded, added.LocalPrinterName, added.HostName);
                    FillFound();
                }
                else
                {
                    _lookupStatus.Text = string.Format(Strings.UseAddFailed, result.Message);
                    MessageBox.Show(this, _lookupStatus.Text, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Adding the printer failed.", ex);
                if (!IsDisposed)
                {
                    _lookupStatus.Text = string.Format(Strings.UseAddFailed, ex.Message);
                }
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private RemotePrinter SelectedMine()
        {
            if (_mine.SelectedRows.Count == 0)
            {
                _mineHint.Text = Strings.UseSelectMine;
                return null;
            }
            return (RemotePrinter)_mine.SelectedRows[0].Tag;
        }

        private async void RemoveSelected()
        {
            if (_busy) return;
            RemotePrinter printer = SelectedMine();
            if (printer == null) return;
            DialogResult answer = MessageBox.Show(this, string.Format(Strings.UseRemoveConfirm, printer.LocalPrinterName), Strings.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                ElevateResult result = await _controller.RemovePrinterAsync(printer);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    _mineHint.Text = string.Format(Strings.UseRemoved, printer.LocalPrinterName);
                    FillFound();
                }
                else
                {
                    _mineHint.Text = string.Format(Strings.UseRemoveFailed, result.Message);
                    MessageBox.Show(this, _mineHint.Text, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Removing the printer failed.", ex);
                if (!IsDisposed) _mineHint.Text = string.Format(Strings.UseRemoveFailed, ex.Message);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SendTestPage()
        {
            RemotePrinter printer = SelectedMine();
            if (printer == null) return;
            try
            {
                _controller.SendTestPage(printer);
                _mineHint.Text = string.Format(Strings.UseTestPageSent, printer.LocalPrinterName);
            }
            catch (FileNotFoundException ex)
            {
                Log.Warn(ex.Message);
                _mineHint.Text = ex.Message;
                MessageBox.Show(this, ex.Message, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("The test page could not be handed over.", ex);
                _mineHint.Text = ex.Message;
            }
        }

        private void RetrySelected()
        {
            RemotePrinter printer = SelectedMine();
            if (printer == null) return;
            try
            {
                int count = _controller.RetryPending(printer);
                _mineHint.Text = count == 0 ? string.Format(Strings.UseNothingWaiting, printer.LocalPrinterName) : string.Format(Strings.UseRetried, count);
                FillMine();
            }
            catch (Exception ex)
            {
                Log.Error("Retry failed.", ex);
                _mineHint.Text = ex.Message;
            }
        }

        private void FillMine()
        {
            string selectedId = _mine.SelectedRows.Count == 0 ? null : ((RemotePrinter)_mine.SelectedRows[0].Tag).PrinterId;
            _mine.Rows.Clear();
            DataGridViewRow reselect = null;
            IList<RemotePrinter> printers = _controller.Printers;
            foreach (RemotePrinter printer in printers)
            {
                int waiting = _controller.PendingCount(printer);
                int index = _mine.Rows.Add(
                    printer.LocalPrinterName ?? "",
                    printer.HostName ?? "",
                    (printer.HostIp ?? "") + ":" + printer.Port,
                    waiting == 0 ? "" : waiting.ToString(CultureInfo.CurrentCulture));
                DataGridViewRow row = _mine.Rows[index];
                row.Tag = printer;
                if (waiting > 0)
                {
                    row.Cells[3].Style.ForeColor = Palette.Warning;
                    row.Cells[3].Style.SelectionForeColor = Palette.Warning;
                }
                if (printer.PrinterId == selectedId) reselect = row;
            }
            Grids.SelectRow(_mine, reselect);
            if (printers.Count == 0)
            {
                _mineHint.Text = Strings.UseNoRemotePrinters;
            }
        }

        private void OnFoundChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) FillFound();
        }

        private void OnPrintersChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) FillMine();
        }

        private void OnJobChanged(object sender, ClientJobRecord record)
        {
            if (!IsDisposed && ClientJobStates.IsFinal(record.State))
            {
                FillMine();   // the waiting count of that printer may have changed
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _lookup.Enabled = !busy;
            _add.Enabled = !busy;
            _remove.Enabled = !busy;
            UseWaitCursor = busy;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.JobChanged -= OnJobChanged;
                _controller.PrintersChanged -= OnPrintersChanged;
                _controller.FoundChanged -= OnFoundChanged;
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
