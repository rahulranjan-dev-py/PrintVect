using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Use shared printers" (brief, section 7), M2 version: look a host up by PC name or IP,
    /// add one of its printers to this PC (virtual printer through the Elevate helper), remove it,
    /// send the test page, retry waiting jobs, and watch the jobs sent from this PC. Automatic
    /// discovery of hosts arrives in M3.
    /// </summary>
    internal sealed class UseTab : UserControl
    {
        private const int MaxJobRows = 50;

        private readonly ClientController _controller;
        private TextBox _hostBox;
        private Button _lookup;
        private Label _lookupStatus;
        private ListView _found;
        private Button _add;
        private Label _mineLabel;
        private ListView _mine;
        private Button _remove;
        private Button _testPage;
        private Button _retry;
        private Label _mineHint;
        private Label _jobsLabel;
        private ListView _jobs;
        private ListReply _lastReply;
        private string _lastTyped;
        private CancellationTokenSource _lookupCancel;
        private bool _busy;

        public UseTab(ClientController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;
            BuildLayout();
            _controller.JobChanged += OnJobChanged;
            _controller.PrintersChanged += OnPrintersChanged;

            IList<ClientJobRecord> recent = _controller.Jobs.Recent(MaxJobRows);
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                UpsertJob(recent[i]);
            }
            FillMine();
            UpdateJobCount();
        }

        private void BuildLayout()
        {
            SuspendLayout();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // intro
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // host box
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // lookup status
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); // found printers
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // add button
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // my printers label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));  // my printers
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // jobs label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));  // jobs

            var intro = new Label { Text = Strings.UseIntro, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };

            var hostRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            var hostLabel = new Label { Text = Strings.UseHostLabel, AutoSize = true, Margin = new Padding(0, 6, 6, 0) };
            _hostBox = new TextBox { Width = 200, Margin = new Padding(0, 2, 8, 0) };
            _hostBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    LookUp();
                }
            };
            _lookup = CreateButton(Strings.UseLookup, (s, e) => LookUp());
            hostRow.Controls.AddRange(new Control[] { hostLabel, _hostBox, _lookup });

            _lookupStatus = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };

            _found = CreateList();
            _found.Columns.Add(Strings.UseColPrinter, 300);
            _found.Columns.Add(Strings.UseColStatus, 120);
            _found.Margin = new Padding(0, 0, 0, 4);
            _found.DoubleClick += (s, e) => AddSelected();

            var addRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            _add = CreateButton(Strings.UseAdd, (s, e) => AddSelected());
            addRow.Controls.Add(_add);

            _mineLabel = new Label { Text = Strings.UseMyPrinters, AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            _mine = CreateList();
            _mine.Columns.Add(Strings.UseColLocalPrinter, 260);
            _mine.Columns.Add(Strings.UseColHost, 120);
            _mine.Columns.Add(Strings.UseColAddress, 130);
            _mine.Columns.Add(Strings.UseColWaiting, 90);
            _mine.Margin = new Padding(0, 0, 0, 4);

            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 10) };
            _remove = CreateButton(Strings.UseRemove, (s, e) => RemoveSelected());
            _testPage = CreateButton(Strings.UseTestPage, (s, e) => SendTestPage());
            _retry = CreateButton(Strings.UseRetry, (s, e) => RetrySelected());
            _mineHint = new Label { AutoSize = true, Margin = new Padding(6, 6, 0, 0), ForeColor = SystemColors.GrayText };
            buttons.Controls.AddRange(new Control[] { _remove, _testPage, _retry, _mineHint });

            _jobsLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            _jobs = CreateList();
            _jobs.Columns.Add(Strings.JobColTime, 70);
            _jobs.Columns.Add(Strings.JobColDocument, 170);
            _jobs.Columns.Add(Strings.JobColPrinter, 120);
            _jobs.Columns.Add(Strings.JobColHost, 100);
            _jobs.Columns.Add(Strings.JobColState, 70);
            _jobs.Columns.Add(Strings.JobColMessage, 330);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(hostRow, 0, 1);
            layout.Controls.Add(_lookupStatus, 0, 2);
            layout.Controls.Add(_found, 0, 3);
            layout.Controls.Add(addRow, 0, 4);
            layout.Controls.Add(_mineLabel, 0, 5);
            layout.Controls.Add(_mine, 0, 6);
            layout.Controls.Add(buttons, 0, 7);
            layout.Controls.Add(_jobsLabel, 0, 8);
            layout.Controls.Add(_jobs, 0, 9);
            Controls.Add(layout);
            ResumeLayout(true);
        }

        private static ListView CreateList()
        {
            return new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = new Padding(0)
            };
        }

        private static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = true
            };
            button.Click += onClick;
            return button;
        }

        private async void LookUp()
        {
            string typed = _hostBox.Text.Trim();
            if (typed.Length == 0 || _busy)
            {
                return;
            }
            SetBusy(true);
            _found.Items.Clear();
            _lastReply = null;
            _lookupStatus.Text = string.Format(Strings.UseLookingUp, typed);
            _lookupCancel = new CancellationTokenSource();
            try
            {
                ListReply reply = await _controller.LookupHostAsync(typed, _lookupCancel.Token);
                if (IsDisposed) return;
                _lastReply = reply;
                _lastTyped = typed;
                string host = string.IsNullOrEmpty(reply.Host) ? typed : reply.Host;
                foreach (PrinterInfo printer in reply.Printers)
                {
                    string shown = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
                    var item = new ListViewItem(new[] { shown, ShareTab.StatusText(printer.Status) }) { Tag = printer };
                    _found.Items.Add(item);
                }
                _lookupStatus.Text = reply.Printers.Count == 0
                    ? string.Format(Strings.UseNoPrintersShared, host)
                    : string.Format(Strings.UseFoundPrinters, host, reply.Printers.Count);
                if (_found.Items.Count > 0)
                {
                    _found.Items[0].Selected = true;
                }
                Log.Info("Look-up of " + typed + ": " + reply.Printers.Count + " printer(s) on " + host + ".");
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

        private async void AddSelected()
        {
            if (_busy) return;
            if (_lastReply == null || _found.SelectedItems.Count == 0)
            {
                _lookupStatus.Text = Strings.UseSelectFound;
                return;
            }
            var printer = (PrinterInfo)_found.SelectedItems[0].Tag;
            string friendly = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly;
            SetBusy(true);
            _lookupStatus.Text = string.Format(Strings.UseAdding, friendly);
            try
            {
                ElevateResult result = await _controller.AddPrinterAsync(_lastReply, printer, _lastTyped);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    RemotePrinter added = _controller.FindPrinter(printer.Id);
                    _lookupStatus.Text = added == null ? result.Message : string.Format(Strings.UseAdded, added.LocalPrinterName, added.HostName);
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
            if (_mine.SelectedItems.Count == 0)
            {
                _mineHint.Text = Strings.UseSelectMine;
                return null;
            }
            return (RemotePrinter)_mine.SelectedItems[0].Tag;
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
            string selectedId = _mine.SelectedItems.Count == 0 ? null : ((RemotePrinter)_mine.SelectedItems[0].Tag).PrinterId;
            _mine.BeginUpdate();
            _mine.Items.Clear();
            IList<RemotePrinter> printers = _controller.Printers;
            foreach (RemotePrinter printer in printers)
            {
                int waiting = _controller.PendingCount(printer);
                var item = new ListViewItem(new[]
                {
                    printer.LocalPrinterName ?? "",
                    printer.HostName ?? "",
                    (printer.HostIp ?? "") + ":" + printer.Port,
                    waiting == 0 ? "" : waiting.ToString(CultureInfo.CurrentCulture)
                }) { Tag = printer };
                if (printer.PrinterId == selectedId) item.Selected = true;
                _mine.Items.Add(item);
            }
            _mine.EndUpdate();
            if (printers.Count == 0)
            {
                _mineHint.Text = Strings.UseNoRemotePrinters;
            }
            else if (_mine.SelectedItems.Count == 0)
            {
                _mine.Items[0].Selected = true;
            }
        }

        private void OnPrintersChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) FillMine();
        }

        private void OnJobChanged(object sender, ClientJobRecord record)
        {
            if (IsDisposed) return;
            UpsertJob(record);
            if (ClientJobStates.IsFinal(record.State))
            {
                FillMine();
            }
        }

        private void UpsertJob(ClientJobRecord record)
        {
            if (record == null) return;
            string[] cells =
            {
                record.StartedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                record.Doc ?? "",
                record.PrinterFriendly ?? "",
                record.HostName ?? "",
                ShareTab.StateText(record.State),
                record.Message ?? ""
            };

            ListViewItem item = _jobs.Items.ContainsKey(record.JobId) ? _jobs.Items[record.JobId] : null;
            if (item == null)
            {
                item = new ListViewItem(cells) { Name = record.JobId };
                _jobs.Items.Insert(0, item);
                while (_jobs.Items.Count > MaxJobRows)
                {
                    _jobs.Items.RemoveAt(_jobs.Items.Count - 1);
                }
            }
            else
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    item.SubItems[i].Text = cells[i];
                }
            }
            item.ForeColor = record.State == ClientJobStates.Error ? Color.Firebrick
                : record.State == ClientJobStates.Pending ? Color.DarkOrange : SystemColors.WindowText;
            UpdateJobCount();
        }

        private void UpdateJobCount()
        {
            _jobsLabel.Text = Strings.UseRecentJobs + "     " + string.Format(Strings.UseJobsToday, _controller.Jobs.CountToday(ClientJobStates.Printed));
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _lookup.Enabled = !busy;
            _add.Enabled = !busy;
            _remove.Enabled = !busy;
            UseWaitCursor = busy;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_mineLabel != null) _mineLabel.Font = new Font(Font, FontStyle.Bold);
            if (_jobsLabel != null) _jobsLabel.Font = new Font(Font, FontStyle.Bold);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.JobChanged -= OnJobChanged;
                _controller.PrintersChanged -= OnPrintersChanged;
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
