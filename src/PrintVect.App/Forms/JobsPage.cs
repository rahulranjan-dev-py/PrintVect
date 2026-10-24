using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Jobs" in the redesign of 0.5.0: one list at a time, Sent from this PC or Received from other PCs, newest first,
    /// with today's printed and waiting counts, the copies, a status pill and the job's message in grey under the
    /// document. Right-click a job (or press Delete) to retry, cancel or remove it; Cancel all waiting and Clear list
    /// act on the list shown (0.4.1). An empty list says what to do next. The list fills the window and scrolls itself.
    /// </summary>
    internal sealed class JobsPage : Panel, IPage
    {
        private const int MaxJobRows = 50;
        private const int SentTab = 0, ReceivedTab = 1;
        private static int _lastTab = -1;   // kept while PrintVect runs, also when the colours change

        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly AppConfig _config;
        private readonly IShell _shell;
        private readonly Segmented _switch;
        private readonly Label _counts;
        private readonly ThemedButton _cancelAllWaiting;
        private readonly ThemedButton _clear;
        private readonly DataGridView _sent;
        private readonly DataGridView _received;
        private readonly Panel _empty;
        private readonly Label _emptyTitle;
        private readonly Label _emptyText;
        private readonly ThemedButton _emptyButton;
        private readonly Control _footer;
        private readonly Label _keepNote;
        private ToolStripMenuItem _sentRetry;
        private ToolStripMenuItem _sentCancel;
        private ToolStripMenuItem _sentRemove;
        private ToolStripMenuItem _receivedCancel;
        private ToolStripMenuItem _receivedRemove;

        public JobsPage(HostController host, ClientController client, AppConfig config, IShell shell)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (client == null) throw new ArgumentNullException(nameof(client));
            _host = host;
            _client = client;
            _config = config;
            _shell = shell;
            BackColor = Palette.Background;
            ApplyPadding();

            _switch = new Segmented(Strings.UseRecentJobs, Strings.ShareRecentJobs) { Anchor = AnchorStyles.Left };
            _switch.SelectedIndexChanged += (s, e) => ShowTab(_switch.SelectedIndex);
            _counts = Make.Label("", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0, 0, 16, 0));
            _counts.Anchor = AnchorStyles.Left;
            _cancelAllWaiting = new ThemedButton(Strings.JobsCancelAllWaiting, ButtonKind.Secondary) { Margin = Ui.Pad(0, 0, 8, 0) };
            _cancelAllWaiting.Click += (s, e) => CancelAllWaiting();
            _clear = new ThemedButton(Strings.JobsClearList, ButtonKind.Secondary) { Margin = Ui.Pad(0) };
            _clear.Click += (s, e) => ClearList();
            FlowLayoutPanel tools = Make.Row(_counts, _cancelAllWaiting, _clear);
            tools.WrapContents = false;

            _sent = Grids.Create();
            _sent.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.JobColTime, 12, false, 128), Grids.TwoLines(Strings.JobColDocument, 34), Grids.Text(Strings.JobColPrinter, 16),
                Grids.Text(Strings.JobColSentTo, 14, true), Grids.Text(Strings.JobColCopies, 7, false, 64), Grids.Pills(Strings.ShareColStatus, 13)
            });
            Grids.Edges(_sent);
            _sent.ContextMenuStrip = SentMenu();
            Wire(_sent, OnSentDelete);

            _received = Grids.Create();
            _received.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.JobColTime, 12, false, 128), Grids.Text(Strings.JobColFromPc, 14, true), Grids.TwoLines(Strings.JobColDocument, 34),
                Grids.Text(Strings.JobColPrinter, 16), Grids.Text(Strings.JobColCopies, 7, false, 64), Grids.Pills(Strings.ShareColStatus, 13)
            });
            Grids.Edges(_received);
            _received.ContextMenuStrip = ReceivedMenu();
            Wire(_received, OnReceivedDelete);

            // An empty list: an icon, what it means and where to go.
            _emptyTitle = Make.Label("", ThemeFonts.SectionTitle, Palette.Text, Ui.Pad(0, 12, 0, 0));
            _emptyTitle.Anchor = AnchorStyles.None;
            _emptyText = Make.Wrap("", ThemeFonts.Text, Palette.Muted, Ui.Pad(0, 6, 0, 0));
            _emptyText.TextAlign = ContentAlignment.TopCenter;
            _emptyButton = new ThemedButton("", ButtonKind.Secondary) { Anchor = AnchorStyles.None, Margin = Ui.Pad(0, 14, 0, 0) };
            _emptyButton.Click += (s, e) => _shell.Go(_emptyTarget);
            var emptyIcon = new IconTile(Glyph.Inbox, 52, 26, Palette.NeutralBg, Palette.NeutralText, true, 26, 1.6f) { Anchor = AnchorStyles.None };
            TableLayoutPanel emptyColumn = Make.Column(0, 0, 0, 0);
            emptyColumn.AutoSize = true;
            emptyColumn.Controls.Add(emptyIcon, 0, 0);
            emptyColumn.Controls.Add(_emptyTitle, 0, 1);
            emptyColumn.Controls.Add(_emptyText, 0, 2);
            emptyColumn.Controls.Add(_emptyButton, 0, 3);
            emptyColumn.Dock = DockStyle.Top;
            _empty = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Card, Padding = Ui.Pad(40, 56, 40, 40), Visible = false };
            _empty.Controls.Add(emptyColumn);
            _empty.Resize += (s, e) =>
            {
                int side = Math.Max(Ui.S(40), (_empty.ClientSize.Width - Ui.S(460)) / 2);
                _empty.Padding = new Padding(side, Ui.S(56), side, Ui.S(40));
            };

            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1, 1, 1, Ui.S(8)), Margin = Ui.Pad(0) };
            card.Controls.Add(_sent);
            card.Controls.Add(_received);
            card.Controls.Add(_empty);

            _keepNote = Make.Label("", ThemeFonts.Secondary, Palette.Faint, Ui.Pad(0, 0, 4, 0));
            _keepNote.Anchor = AnchorStyles.Left;
            _footer = Make.Row(_keepNote, Make.Link(Strings.JobsChangeInSettings, Palette.AccentText, () => _shell.Go(PageId.Settings), ThemeFonts.Secondary));

            TableLayoutPanel layout = Make.Column(0, 0, 100, 0);
            layout.BackColor = Palette.Background;
            layout.Controls.Add(Make.Stack(6, Make.PageTitle(Strings.TabJobs), Make.Intro(Strings.JobsIntro)), 0, 0);
            Control toolbar = Make.Heading(_switch, tools);
            toolbar.Margin = Ui.Pad(0, 22, 0, 16);
            layout.Controls.Add(toolbar, 0, 1);
            layout.Controls.Add(card, 0, 2);
            _footer.Margin = Ui.Pad(0, 14, 0, 0);
            layout.Controls.Add(_footer, 0, 3);
            Controls.Add(layout);
            Ui.Solidify(this);
            _empty.BackColor = Palette.Card;

            IList<JobRecord> received = _host.Jobs.Recent(MaxJobRows);
            for (int i = received.Count - 1; i >= 0; i--) UpsertReceived(received[i]);
            IList<ClientJobRecord> sent = _client.Jobs.Recent(MaxJobRows);
            for (int i = sent.Count - 1; i >= 0; i--) UpsertSent(sent[i]);

            _sent.ClearSelection();       // no row looks picked until someone clicks one
            _received.ClearSelection();
            int tab = _lastTab >= 0 ? _lastTab : _host.IsSharing ? ReceivedTab : SentTab;
            _switch.SelectedIndex = tab;
            ShowTab(tab);

            _host.JobChanged += OnHostJobChanged;
            _host.JobRemoved += OnHostJobRemoved;
            _host.SharingChanged += OnSharingChanged;
            _client.JobChanged += OnClientJobChanged;
            _client.JobRemoved += OnClientJobRemoved;
            _client.SettingsChanged += OnClientSettingsChanged;
        }

        private bool ShowingSent
        {
            get { return _switch.SelectedIndex == SentTab; }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            ApplyPadding();
            base.OnResize(eventargs);
        }

        /// <summary>The same edges as the other pages (PageScroller.SidePadding).</summary>
        private void ApplyPadding()
        {
            Padding padding = PageScroller.SidePadding(ClientSize.Width, Ui.S(28), Ui.S(28));
            if (Padding != padding) Padding = padding;
        }

        private void ShowTab(int tab)
        {
            _lastTab = tab;
            _cancelAllWaiting.Visible = tab == SentTab;
            _footer.Visible = tab == SentTab;
            UpdateCounts();
        }

        /// <summary>The list shown, or the empty-list message in its place; the counts and the buttons that have something to do.</summary>
        private void UpdateCounts()
        {
            bool sent = ShowingSent;
            int printed = sent ? _client.Jobs.CountToday(ClientJobStates.Printed) : _host.Jobs.CountToday(JobStates.Printed);
            int waiting = sent ? _client.Jobs.All().Count(r => r.State == ClientJobStates.Pending)
                               : _host.Jobs.All().Count(r => r.State == JobStates.Queued);
            _counts.Text = string.Format(Strings.JobsCounts, printed, waiting);

            IList<ClientJobRecord> sentJobs = _client.Jobs.All();
            _cancelAllWaiting.Enabled = sentJobs.Any(r => r.State == ClientJobStates.Pending);
            _clear.Enabled = sent ? sentJobs.Any(r => ClientJobStates.IsDone(r.State)) : _host.Jobs.All().Any(r => JobStates.IsFinal(r.State));
            _keepNote.Text = string.Format(Strings.JobsKeepNote, string.Format(CultureInfo.CurrentCulture, Strings.SettingsKeepHours, _config.KeepSentFilesHours));

            DataGridView grid = sent ? _sent : _received;
            bool empty = grid.Rows.Count == 0;
            _sent.Visible = sent && !empty;
            _received.Visible = !sent && !empty;
            _empty.Visible = empty;
            if (empty) ShowEmpty(sent);
        }

        private void ShowEmpty(bool sent)
        {
            if (sent)
            {
                IList<RemotePrinter> printers = _client.Printers;
                _emptyTitle.Text = Strings.JobsSentEmpty;
                _emptyText.Text = printers.Count == 0 ? Strings.JobsSentEmptyNoPrinter
                    : string.Format(Strings.JobsSentEmptyText, string.Join(" " + Strings.WordOr + " ", printers.Select(p => p.FriendlyName).Distinct()));
                SetEmptyButton(Strings.JobsSeeOffice, PageId.Office);
            }
            else
            {
                _emptyTitle.Text = Strings.JobsReceivedEmpty;
                _emptyText.Text = _host.IsSharing ? Strings.JobsReceivedEmptyOn : Strings.JobsReceivedEmptyOff;
                SetEmptyButton(Strings.JobsGoShare, PageId.Share);
            }
        }

        private PageId _emptyTarget;

        private void SetEmptyButton(string text, PageId target)
        {
            _emptyTarget = target;
            _emptyButton.Text = text;
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
            Point point = grid.PointToClient(Cursor.Position);
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
            ContextMenuStrip menu = MenuRenderer.Create();
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
            ContextMenuStrip menu = MenuRenderer.Create();
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
                _shell.Say(string.Format(Strings.JobsRetrying, record.Doc));
            }
        }

        private void CancelSent(string jobId)
        {
            ClientJobRecord record = jobId == null ? null : _client.Jobs.Find(jobId);
            if (record == null) return;
            if (_client.CancelWaiting(jobId))
            {
                _shell.Say(string.Format(Strings.JobsCancelled, record.Doc));
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
            _shell.Say(string.Format(Strings.JobsCancelledAll, _client.CancelAllWaiting()));
        }

        // ---- Received from other PCs ----

        private void CancelReceived(string jobId)
        {
            JobRecord record;
            if (jobId == null || !_host.Jobs.TryGet(jobId, out record)) return;
            string problem = _host.CancelJob(jobId);
            if (problem == null)
            {
                _shell.Say(string.Format(Strings.JobsHostCancelled, record.Doc, record.Client));
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

        // ---- shared ----

        private void ClearList()
        {
            if (!Confirm(Strings.JobsConfirmClear)) return;
            int removed = ShowingSent ? _client.ClearDoneJobs() : _host.ClearFinishedJobs();
            _shell.Say(string.Format(Strings.JobsCleared, removed));
        }

        private bool Confirm(string question)
        {
            return MessageBox.Show(FindForm(), question, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>"Copy details": the row as "column: value" lines, the message and the job id, to paste into a message to support.</summary>
        private void CopyDetails(DataGridView grid)
        {
            DataGridViewRow row = grid.CurrentRow;
            if (row == null) return;
            var text = new StringBuilder();
            foreach (DataGridViewColumn column in grid.Columns)
            {
                DataGridViewCell cell = row.Cells[column.Index];
                text.Append(column.HeaderText).Append(": ").AppendLine(Convert.ToString(cell.Value, CultureInfo.CurrentCulture));
                if (cell.Tag is string && ((string)cell.Tag).Length > 0)
                {
                    text.Append(Strings.JobColMessage).Append(": ").AppendLine((string)cell.Tag);
                }
            }
            text.Append(Strings.JobsCopyId).Append(": ").AppendLine(row.Tag as string);
            try
            {
                Clipboard.SetText(text.ToString());
                _shell.Say(Strings.JobsCopied);
            }
            catch (Exception ex)
            {
                Log.Warn("Job details could not be copied: " + ex.Message);
                _shell.Say(Strings.DiagCopyFailed);
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

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) UpdateCounts();
        }

        private void OnClientSettingsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) UpdateCounts();   // the "kept for 1 hour" note
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
            DataGridViewRow row = Upsert(_received, record.JobId, Texts.When(record.ReceivedAt), record.Client ?? "", record.Doc ?? "",
                record.PrinterFriendly ?? "", Math.Max(1, record.Copies).ToString(CultureInfo.CurrentCulture), Texts.JobState(record.State));
            row.Cells[2].Tag = record.State == JobStates.Printed ? "" : record.Message ?? "";
            row.Cells[5].Tag = Texts.JobPill(record.State);
            _received.InvalidateRow(row.Index);
        }

        private void UpsertSent(ClientJobRecord record)
        {
            if (record == null) return;
            DataGridViewRow row = Upsert(_sent, record.JobId, Texts.When(record.StartedAt), record.Doc ?? "", record.PrinterFriendly ?? "",
                record.HostName ?? "", Math.Max(1, record.Copies).ToString(CultureInfo.CurrentCulture), Texts.JobState(record.State));
            row.Cells[1].Tag = record.State == ClientJobStates.Printed ? "" : record.Message ?? "";
            row.Cells[5].Tag = Texts.JobPill(record.State);
            _sent.InvalidateRow(row.Index);
        }

        /// <summary>Updates the job's row (found by its id in the row's Tag) or adds it at the top.</summary>
        private static DataGridViewRow Upsert(DataGridView grid, string jobId, params string[] cells)
        {
            foreach (DataGridViewRow candidate in grid.Rows)
            {
                if (string.Equals(candidate.Tag as string, jobId, StringComparison.Ordinal))
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        candidate.Cells[i].Value = cells[i];
                    }
                    return candidate;
                }
            }
            grid.Rows.Insert(0, cells);
            DataGridViewRow row = grid.Rows[0];
            row.Tag = jobId;
            while (grid.Rows.Count > MaxJobRows)
            {
                grid.Rows.RemoveAt(grid.Rows.Count - 1);
            }
            return row;
        }

        /// <summary>The lists follow their events whether the page is open or not: nothing to start or stop.</summary>
        public void SetOpen(bool open)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _host.JobChanged -= OnHostJobChanged;
                _host.JobRemoved -= OnHostJobRemoved;
                _host.SharingChanged -= OnSharingChanged;
                _client.JobChanged -= OnClientJobChanged;
                _client.JobRemoved -= OnClientJobRemoved;
                _client.SettingsChanged -= OnClientSettingsChanged;
            }
            base.Dispose(disposing);
        }
    }
}
