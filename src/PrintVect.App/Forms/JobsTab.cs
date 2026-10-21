using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Client;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Jobs" (the Queue screen of the design): the jobs other PCs sent to this PC's printers, and the jobs this PC
    /// sent to printers of other PCs, newest first, each list with today's printed count. Right-click a job (or press
    /// Delete) to retry, cancel or remove it; the buttons above each list cancel every waiting job or clear the
    /// finished ones (0.4.1, owner's request). Clearing never touches a printer or a document, only the list.
    /// </summary>
    internal sealed class JobsTab : UserControl
    {
        private const int MaxJobRows = 50;

        private readonly HostController _host;
        private readonly ClientController _client;
        private DataGridView _received;
        private DataGridView _sent;
        private Label _receivedCount;
        private Label _sentCount;
        private ThemedButton _cancelAllWaiting;
        private ThemedButton _clearSent;
        private ThemedButton _clearReceived;
        private ToolStripMenuItem _sentRetry;
        private ToolStripMenuItem _sentCancel;
        private ToolStripMenuItem _sentRemove;
        private ToolStripMenuItem _receivedCancel;
        private ToolStripMenuItem _receivedRemove;

        public JobsTab(HostController host, ClientController client)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (client == null) throw new ArgumentNullException(nameof(client));
            _host = host;
            _client = client;
            BackColor = Palette.Background;
            BuildLayout();

            IList<JobRecord> received = _host.Jobs.Recent(MaxJobRows);
            for (int i = received.Count - 1; i >= 0; i--)
            {
                UpsertReceived(received[i]);
            }
            IList<ClientJobRecord> sent = _client.Jobs.Recent(MaxJobRows);
            for (int i = sent.Count - 1; i >= 0; i--)
            {
                UpsertSent(sent[i]);
            }
            UpdateCounts();
            _host.JobChanged += OnHostJobChanged;
            _host.JobRemoved += OnHostJobRemoved;
            _client.JobChanged += OnClientJobChanged;
            _client.JobRemoved += OnClientJobRemoved;
        }

        /// <summary>Raised with a short text for the status bar ("2 waiting job(s) cancelled").</summary>
        public event EventHandler<string> Message;

        private void BuildLayout()
        {
            SuspendLayout();
            TableLayoutPanel layout = Make.Column(50, 50);
            layout.BackColor = Palette.Background;

            _received = Grids.Create();
            _received.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.JobColTime, 9), Grids.Text(Strings.JobColFrom, 15, mono: true), Grids.Text(Strings.JobColDocument, 22),
                Grids.Text(Strings.JobColPrinter, 17), Grids.Text(Strings.JobColState, 10), Grids.Text(Strings.JobColMessage, 30)
            });
            _receivedCount = CountLabel();
            _clearReceived = SmallButton(Strings.JobsClearList, (s, e) => ClearReceived());
            _received.ContextMenuStrip = ReceivedMenu();
            Wire(_received, OnReceivedDelete);
            layout.Controls.Add(JobCard(Strings.ShareRecentJobs, _received, true, _receivedCount, _clearReceived), 0, 0);

            _sent = Grids.Create();
            _sent.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.JobColTime, 9), Grids.Text(Strings.JobColDocument, 22), Grids.Text(Strings.JobColPrinter, 17),
                Grids.Text(Strings.JobColHost, 14, mono: true), Grids.Text(Strings.JobColState, 10), Grids.Text(Strings.JobColMessage, 31)
            });
            _sentCount = CountLabel();
            _cancelAllWaiting = SmallButton(Strings.JobsCancelAllWaiting, (s, e) => CancelAllWaiting());
            _clearSent = SmallButton(Strings.JobsClearList, (s, e) => ClearSent());
            _sent.ContextMenuStrip = SentMenu();
            Wire(_sent, OnSentDelete);
            layout.Controls.Add(JobCard(Strings.UseRecentJobs, _sent, false, _sentCount, _cancelAllWaiting, _clearSent), 0, 1);

            Controls.Add(layout);
            ResumeLayout(true);
        }

        private static Label CountLabel()
        {
            Label label = Make.Label("", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0, 9, 16, 0));
            label.Dock = DockStyle.None;
            return label;
        }

        private static ThemedButton SmallButton(string text, EventHandler onClick)
        {
            var button = new ThemedButton(text, ButtonKind.Secondary) { MinimumHeight = 36, Margin = Ui.Pad(8, 0, 0, 0) };
            button.Click += onClick;
            return button;
        }

        private static Card JobCard(string title, DataGridView grid, bool first, params Control[] right)
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = first ? Ui.Pad(0, 0, 0, 16) : Ui.Pad(0) };
            FlowLayoutPanel tools = Make.Row(right);
            tools.Margin = Ui.Pad(0);
            tools.Dock = DockStyle.None;
            tools.WrapContents = false;
            TableLayoutPanel column = Make.Column(0, 100);
            column.Controls.Add(Make.Heading(Make.Title(title), tools), 0, 0);
            column.Controls.Add(grid, 0, 1);
            card.Controls.Add(column);
            return card;
        }

        /// <summary>A right-click selects the row under the mouse first, so the menu acts on the job you pointed at.</summary>
        private static void Wire(DataGridView grid, Action onDelete)
        {
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                {
                    Grids.SelectRow(grid, grid.Rows[e.RowIndex]);
                }
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    e.Handled = true;
                    onDelete();
                }
            };
        }

        /// <summary>The job id of the row the menu was opened on: the one under the mouse, or the selected one (menu key).</summary>
        private static string MenuJobId(DataGridView grid)
        {
            System.Drawing.Point point = grid.PointToClient(Cursor.Position);
            DataGridView.HitTestInfo hit = grid.HitTest(point.X, point.Y);
            if (hit.RowIndex >= 0)
            {
                return grid.Rows[hit.RowIndex].Tag as string;
            }
            return grid.Focused && grid.CurrentRow != null ? grid.CurrentRow.Tag as string : null;
        }

        private static string SelectedJobId(DataGridView grid)
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as string;
        }

        private ContextMenuStrip SentMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = ThemeFonts.Text, ShowImageMargin = false };
            _sentRetry = new ToolStripMenuItem(Strings.JobsMenuRetry, null, (s, e) => RetrySent(SelectedJobId(_sent)));
            _sentCancel = new ToolStripMenuItem(Strings.JobsMenuCancel, null, (s, e) => CancelSent(SelectedJobId(_sent)));
            _sentRemove = new ToolStripMenuItem(Strings.JobsMenuRemove, null, (s, e) => RemoveSent(SelectedJobId(_sent)));
            var copy = new ToolStripMenuItem(Strings.JobsMenuCopy, null, (s, e) => CopyDetails(_sent));
            menu.Items.AddRange(new ToolStripItem[] { _sentRetry, _sentCancel, new ToolStripSeparator(), _sentRemove, copy });
            menu.Opening += (s, e) =>
            {
                string jobId = MenuJobId(_sent);
                ClientJobRecord record = jobId == null ? null : _client.Jobs.Find(jobId);
                if (record == null)
                {
                    e.Cancel = true;
                    return;
                }
                bool waiting = record.State == ClientJobStates.Pending;
                _sentRetry.Enabled = waiting;
                _sentCancel.Enabled = waiting;
                _sentRemove.Enabled = ClientJobStates.IsDone(record.State);
            };
            return menu;
        }

        private ContextMenuStrip ReceivedMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = ThemeFonts.Text, ShowImageMargin = false };
            _receivedCancel = new ToolStripMenuItem(Strings.JobsMenuCancel, null, (s, e) => CancelReceived(SelectedJobId(_received)));
            _receivedRemove = new ToolStripMenuItem(Strings.JobsMenuRemove, null, (s, e) => RemoveReceived(SelectedJobId(_received)));
            var copy = new ToolStripMenuItem(Strings.JobsMenuCopy, null, (s, e) => CopyDetails(_received));
            menu.Items.AddRange(new ToolStripItem[] { _receivedCancel, new ToolStripSeparator(), _receivedRemove, copy });
            menu.Opening += (s, e) =>
            {
                string jobId = MenuJobId(_received);
                JobRecord record;
                if (jobId == null || !_host.Jobs.TryGet(jobId, out record))
                {
                    e.Cancel = true;
                    return;
                }
                bool finished = JobStates.IsFinal(record.State);
                _receivedCancel.Enabled = !finished;
                _receivedRemove.Enabled = finished;
            };
            return menu;
        }

        // ---- Sent from this PC ----

        private void RetrySent(string jobId)
        {
            ClientJobRecord record = jobId == null ? null : _client.Jobs.Find(jobId);
            if (record == null) return;
            if (_client.RetryJob(jobId))
            {
                Say(string.Format(Strings.JobsRetrying, record.Doc));
            }
        }

        private void CancelSent(string jobId)
        {
            ClientJobRecord record = jobId == null ? null : _client.Jobs.Find(jobId);
            if (record == null) return;
            if (_client.CancelWaiting(jobId))
            {
                Say(string.Format(Strings.JobsCancelled, record.Doc));
            }
        }

        private void RemoveSent(string jobId)
        {
            if (jobId != null) _client.RemoveJob(jobId);
        }

        /// <summary>Delete key: a waiting job is cancelled, a finished one leaves the list; a moving job is left alone.</summary>
        private void OnSentDelete()
        {
            string jobId = SelectedJobId(_sent);
            ClientJobRecord record = jobId == null ? null : _client.Jobs.Find(jobId);
            if (record == null) return;
            if (record.State == ClientJobStates.Pending) CancelSent(jobId);
            else if (ClientJobStates.IsDone(record.State)) RemoveSent(jobId);
        }

        private void CancelAllWaiting()
        {
            int waiting = _client.Jobs.All().Count(r => r.State == ClientJobStates.Pending);
            if (waiting == 0) return;
            if (!Confirm(string.Format(Strings.JobsConfirmCancelAll, waiting))) return;
            Say(string.Format(Strings.JobsCancelledAll, _client.CancelAllWaiting()));
        }

        private void ClearSent()
        {
            if (!Confirm(Strings.JobsConfirmClear)) return;
            Say(string.Format(Strings.JobsCleared, _client.ClearDoneJobs()));
        }

        // ---- Received from other PCs ----

        private void CancelReceived(string jobId)
        {
            JobRecord record;
            if (jobId == null || !_host.Jobs.TryGet(jobId, out record)) return;
            string problem = _host.CancelJob(jobId);
            if (problem == null)
            {
                Say(string.Format(Strings.JobsHostCancelled, record.Doc, record.Client));
            }
            else
            {
                MessageBox.Show(FindForm(), problem, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RemoveReceived(string jobId)
        {
            if (jobId != null) _host.RemoveJob(jobId);
        }

        private void OnReceivedDelete()
        {
            string jobId = SelectedJobId(_received);
            JobRecord record;
            if (jobId != null && _host.Jobs.TryGet(jobId, out record) && JobStates.IsFinal(record.State))
            {
                RemoveReceived(jobId);
            }
        }

        private void ClearReceived()
        {
            if (!Confirm(Strings.JobsConfirmClear)) return;
            Say(string.Format(Strings.JobsCleared, _host.ClearFinishedJobs()));
        }

        // ---- shared ----

        private bool Confirm(string question)
        {
            return MessageBox.Show(FindForm(), question, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void Say(string text)
        {
            Message?.Invoke(this, text);
        }

        /// <summary>"Copy details": the row as "column: value" lines plus the job id, to paste into a message to support.</summary>
        private void CopyDetails(DataGridView grid)
        {
            DataGridViewRow row = grid.CurrentRow;
            if (row == null) return;
            var text = new StringBuilder();
            foreach (DataGridViewColumn column in grid.Columns)
            {
                text.Append(column.HeaderText).Append(": ").AppendLine(Convert.ToString(row.Cells[column.Index].Value, CultureInfo.CurrentCulture));
            }
            text.Append(Strings.JobsCopyId).Append(": ").AppendLine(row.Tag as string);
            try
            {
                Clipboard.SetText(text.ToString());
                Say(Strings.JobsCopied);
            }
            catch (Exception ex)
            {
                Log.Warn("Job details could not be copied: " + ex.Message);
                Say(Strings.DiagCopyFailed);
            }
        }

        private void OnHostJobChanged(object sender, JobRecord record)
        {
            if (IsDisposed) return;
            UpsertReceived(record);
            UpdateCounts();
        }

        private void OnClientJobChanged(object sender, ClientJobRecord record)
        {
            if (IsDisposed) return;
            UpsertSent(record);
            UpdateCounts();
        }

        private void OnHostJobRemoved(object sender, string jobId)
        {
            if (IsDisposed) return;
            RemoveRow(_received, jobId);
            UpdateCounts();
        }

        private void OnClientJobRemoved(object sender, string jobId)
        {
            if (IsDisposed) return;
            RemoveRow(_sent, jobId);
            UpdateCounts();
        }

        private static void RemoveRow(DataGridView grid, string jobId)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (string.Equals(row.Tag as string, jobId, StringComparison.Ordinal))
                {
                    grid.Rows.Remove(row);
                    return;
                }
            }
        }

        private void UpsertReceived(JobRecord record)
        {
            if (record == null) return;
            Upsert(_received, record.JobId, Grids.StateColour(record.State, JobStates.Printed, null, JobStates.Error),
                record.ReceivedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                record.Client ?? "",
                record.Doc ?? "",
                record.PrinterFriendly ?? "",
                ShareTab.StateText(record.State),
                record.Message ?? "");
        }

        private void UpsertSent(ClientJobRecord record)
        {
            if (record == null) return;
            Upsert(_sent, record.JobId, record.State == ClientJobStates.Cancelled ? Palette.Muted
                    : Grids.StateColour(record.State, ClientJobStates.Printed, ClientJobStates.Pending, ClientJobStates.Error),
                record.StartedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                record.Doc ?? "",
                record.PrinterFriendly ?? "",
                record.HostName ?? "",
                ShareTab.StateText(record.State),
                record.Message ?? "");
        }

        /// <summary>Updates the job's row (found by its id in the row's Tag) or adds it at the top.</summary>
        private static void Upsert(DataGridView grid, string jobId, System.Drawing.Color stateColour, params string[] cells)
        {
            DataGridViewRow row = null;
            foreach (DataGridViewRow candidate in grid.Rows)
            {
                if (string.Equals(candidate.Tag as string, jobId, StringComparison.Ordinal))
                {
                    row = candidate;
                    break;
                }
            }
            if (row == null)
            {
                grid.Rows.Insert(0, cells);
                row = grid.Rows[0];
                row.Tag = jobId;
                while (grid.Rows.Count > MaxJobRows)
                {
                    grid.Rows.RemoveAt(grid.Rows.Count - 1);
                }
            }
            else
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    row.Cells[i].Value = cells[i];
                }
            }
            DataGridViewCell state = row.Cells[cells.Length - 2];
            state.Style.ForeColor = stateColour;
            state.Style.SelectionForeColor = stateColour;
            state.Style.Font = ThemeFonts.TextSemi;
        }

        /// <summary>Today's counts, and the buttons only when they have something to do.</summary>
        private void UpdateCounts()
        {
            _receivedCount.Text = string.Format(Strings.ShareJobsToday, _host.Jobs.CountToday(JobStates.Printed));
            _sentCount.Text = string.Format(Strings.UseJobsToday, _client.Jobs.CountToday(ClientJobStates.Printed));
            IList<ClientJobRecord> sent = _client.Jobs.All();
            _cancelAllWaiting.Enabled = sent.Any(r => r.State == ClientJobStates.Pending);
            _clearSent.Enabled = sent.Any(r => ClientJobStates.IsDone(r.State));
            _clearReceived.Enabled = _host.Jobs.All().Any(r => JobStates.IsFinal(r.State));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _host.JobChanged -= OnHostJobChanged;
                _host.JobRemoved -= OnHostJobRemoved;
                _client.JobChanged -= OnClientJobChanged;
                _client.JobRemoved -= OnClientJobRemoved;
            }
            base.Dispose(disposing);
        }
    }
}
