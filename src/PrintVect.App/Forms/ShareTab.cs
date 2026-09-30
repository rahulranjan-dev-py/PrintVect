using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Host;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Share my printers" (brief, section 7): the local printers with a Share tick box and an
    /// editable friendly name, the Sharing ON/OFF switch, today's job count and the recent jobs.
    /// </summary>
    internal sealed class ShareTab : UserControl
    {
        private const int MaxJobRows = 50;

        private readonly HostController _controller;
        private CheckBox _toggle;
        private Button _refresh;
        private Label _status;
        private Label _hint;
        private DataGridView _grid;
        private DataGridViewCheckBoxColumn _shareColumn;
        private DataGridViewTextBoxColumn _printerColumn;
        private DataGridViewTextBoxColumn _friendlyColumn;
        private DataGridViewTextBoxColumn _statusColumn;
        private Label _jobsLabel;
        private ListView _jobs;
        private bool _loading;
        private bool _syncing;

        public ShareTab(HostController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;
            BuildLayout();

            _controller.SharingChanged += OnSharingChanged;
            _controller.JobReceived += OnJobChanged;
            _controller.JobFinished += OnJobChanged;

            IList<JobRecord> recent = _controller.Jobs.Recent(MaxJobRows);
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                UpsertJob(recent[i]);
            }
            SyncSharingState();
            LoadPrinters();
        }

        private void BuildLayout()
        {
            SuspendLayout();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            var top = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _toggle = new CheckBox
            {
                Text = Strings.ShareToggle,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 5, 20, 0)
            };
            _toggle.CheckedChanged += OnToggleChanged;
            _refresh = new Button
            {
                Text = Strings.ShareRefresh,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0),
                UseVisualStyleBackColor = true
            };
            _refresh.Click += (s, e) => LoadPrinters();
            top.Controls.Add(_toggle);
            top.Controls.Add(_refresh);

            _status = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            var intro = new Label { Text = Strings.ShareIntro, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            _hint = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), ForeColor = SystemColors.GrayText };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                Margin = new Padding(0, 0, 0, 10)
            };
            _shareColumn = new DataGridViewCheckBoxColumn { HeaderText = Strings.ShareColShare, FillWeight = 12, MinimumWidth = 50 };
            _printerColumn = new DataGridViewTextBoxColumn { HeaderText = Strings.ShareColPrinter, ReadOnly = true, FillWeight = 40 };
            _friendlyColumn = new DataGridViewTextBoxColumn { HeaderText = Strings.ShareColFriendly, FillWeight = 33 };
            _statusColumn = new DataGridViewTextBoxColumn { HeaderText = Strings.ShareColStatus, ReadOnly = true, FillWeight = 15 };
            _grid.Columns.AddRange(new DataGridViewColumn[] { _shareColumn, _printerColumn, _friendlyColumn, _statusColumn });
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.DataError += (s, e) =>
            {
                Log.Warn("Printer grid error: " + (e.Exception == null ? "?" : e.Exception.Message));
                e.ThrowException = false;
            };

            _jobsLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), Font = new Font(Font, FontStyle.Bold) };
            _jobs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Margin = new Padding(0)
            };
            _jobs.Columns.Add(Strings.JobColTime, 70);
            _jobs.Columns.Add(Strings.JobColFrom, 110);
            _jobs.Columns.Add(Strings.JobColDocument, 170);
            _jobs.Columns.Add(Strings.JobColPrinter, 130);
            _jobs.Columns.Add(Strings.JobColState, 70);
            _jobs.Columns.Add(Strings.JobColMessage, 320);

            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(intro, 0, 2);
            layout.Controls.Add(_hint, 0, 3);
            layout.Controls.Add(_grid, 0, 4);
            layout.Controls.Add(_jobsLabel, 0, 5);
            layout.Controls.Add(_jobs, 0, 6);
            Controls.Add(layout);
            ResumeLayout(true);
        }

        public void LoadPrinters()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            _refresh.Enabled = false;
            _grid.Rows.Clear();
            _hint.Text = Strings.ShareLoading;

            _controller.ListPrintersAsync().ContinueWith(task =>
            {
                if (IsDisposed)
                {
                    return;
                }
                try
                {
                    if (task.IsFaulted)
                    {
                        Exception ex = task.Exception == null ? new Exception("?") : task.Exception.GetBaseException();
                        Log.Error("The printer list could not be read.", ex);
                        _hint.Text = string.Format(Strings.SharePrintersError, ex.Message);
                    }
                    else
                    {
                        FillGrid(task.Result);
                    }
                }
                finally
                {
                    _loading = false;
                    _refresh.Enabled = true;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void FillGrid(IList<LocalPrinterInfo> printers)
        {
            _grid.Rows.Clear();
            foreach (LocalPrinterInfo printer in printers)
            {
                SharedPrinter shared = _controller.FindShared(printer.Name);
                _grid.Rows.Add(shared != null, printer.Name, shared == null ? printer.Name : shared.FriendlyName, StatusText(printer.Status));
            }
            _hint.Text = printers.Count == 0 ? Strings.ShareNoPrinters : "";
            Log.Info("Share tab: " + printers.Count + " printer(s) listed.");
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0)
            {
                return;
            }

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            string printer = row.Cells[_printerColumn.Index].Value as string;
            if (string.IsNullOrEmpty(printer))
            {
                return;
            }

            string friendly = ((row.Cells[_friendlyColumn.Index].Value as string) ?? "").Trim();
            if (friendly.Length == 0)
            {
                friendly = printer;
                _loading = true;
                row.Cells[_friendlyColumn.Index].Value = friendly;
                _loading = false;
            }
            bool shared = row.Cells[_shareColumn.Index].Value is bool ticked && ticked;

            bool saved;
            if (e.ColumnIndex == _shareColumn.Index)
            {
                saved = _controller.SetShared(printer, shared, friendly);
            }
            else if (e.ColumnIndex == _friendlyColumn.Index)
            {
                saved = !shared || _controller.SetFriendlyName(printer, friendly);
            }
            else
            {
                return;
            }

            if (!saved)
            {
                MessageBox.Show(this, string.Format(Strings.SharingSaveError, _controller.ConfigPath), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnToggleChanged(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }
            string error;
            if (!_controller.TrySetSharing(_toggle.Checked, out error))
            {
                _syncing = true;
                _toggle.Checked = _controller.IsSharing;
                _syncing = false;
                MessageBox.Show(this, error, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SyncSharingState();
        }

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed)
            {
                SyncSharingState();
            }
        }

        private void SyncSharingState()
        {
            _syncing = true;
            _toggle.Checked = _controller.IsSharing;
            _syncing = false;
            _status.Text = _controller.IsSharing ? string.Format(Strings.ShareStatusOn, _controller.Port) : Strings.ShareStatusOff;
            UpdateJobCount();
        }

        private void OnJobChanged(object sender, JobRecord record)
        {
            if (!IsDisposed)
            {
                UpsertJob(record);
            }
        }

        private void UpsertJob(JobRecord record)
        {
            if (record == null)
            {
                return;
            }

            string[] cells =
            {
                record.ReceivedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                record.Client ?? "",
                record.Doc ?? "",
                record.PrinterFriendly ?? "",
                StateText(record.State),
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
            item.ForeColor = record.State == JobStates.Error ? Color.Firebrick : SystemColors.WindowText;
            UpdateJobCount();
        }

        private void UpdateJobCount()
        {
            _jobsLabel.Text = string.Format(Strings.ShareJobsToday, _controller.Jobs.CountToday(JobStates.Printed))
                              + "     " + Strings.ShareRecentJobs;
        }

        internal static string StatusText(string status)
        {
            return Lookup("PrinterStatus_", status);
        }

        internal static string StateText(string state)
        {
            return Lookup("JobState_", state);
        }

        private static string Lookup(string prefix, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }
            string text = Strings.ResourceManager.GetString(prefix + key.Replace(' ', '_'), Strings.Culture);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        /// <summary>The window's font arrives after construction; keep the bold labels in step with it.</summary>
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_toggle != null)
            {
                _toggle.Font = new Font(Font, FontStyle.Bold);
            }
            if (_jobsLabel != null)
            {
                _jobsLabel.Font = new Font(Font, FontStyle.Bold);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.SharingChanged -= OnSharingChanged;
                _controller.JobReceived -= OnJobChanged;
                _controller.JobFinished -= OnJobChanged;
            }
            base.Dispose(disposing);
        }
    }
}
