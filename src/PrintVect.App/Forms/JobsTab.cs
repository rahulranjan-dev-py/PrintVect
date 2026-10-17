using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Client;
using PrintVect.Core.Host;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Jobs" (the Queue screen of the design): the jobs other PCs sent to this PC's printers, and the jobs this PC
    /// sent to printers of other PCs, newest first, each list with today's printed count. These were the lower
    /// halves of the Share and Use tabs before M6b.
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
            _client.JobChanged += OnClientJobChanged;
        }

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
            _receivedCount = Make.Label("", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            layout.Controls.Add(JobCard(Strings.ShareRecentJobs, _receivedCount, _received, true), 0, 0);

            _sent = Grids.Create();
            _sent.Columns.AddRange(new DataGridViewColumn[]
            {
                Grids.Text(Strings.JobColTime, 9), Grids.Text(Strings.JobColDocument, 22), Grids.Text(Strings.JobColPrinter, 17),
                Grids.Text(Strings.JobColHost, 14, mono: true), Grids.Text(Strings.JobColState, 10), Grids.Text(Strings.JobColMessage, 31)
            });
            _sentCount = Make.Label("", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            foreach (DataGridView grid in new[] { _received, _sent })
            {
                // Nothing is done with a selected job, so a highlighted row would only look like it means something.
                grid.DefaultCellStyle.SelectionBackColor = Palette.Card;
                grid.DefaultCellStyle.SelectionForeColor = Palette.Text;
            }
            layout.Controls.Add(JobCard(Strings.UseRecentJobs, _sentCount, _sent, false), 0, 1);

            Controls.Add(layout);
            ResumeLayout(true);
        }

        private static Card JobCard(string title, Label count, DataGridView grid, bool first)
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = first ? Ui.Pad(0, 0, 0, 16) : Ui.Pad(0) };
            TableLayoutPanel column = Make.Column(0, 100);
            column.Controls.Add(Make.Heading(Make.Title(title), count), 0, 0);
            column.Controls.Add(grid, 0, 1);
            card.Controls.Add(column);
            return card;
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
            Upsert(_sent, record.JobId, Grids.StateColour(record.State, ClientJobStates.Printed, ClientJobStates.Pending, ClientJobStates.Error),
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

        private void UpdateCounts()
        {
            _receivedCount.Text = string.Format(Strings.ShareJobsToday, _host.Jobs.CountToday(JobStates.Printed));
            _sentCount.Text = string.Format(Strings.UseJobsToday, _client.Jobs.CountToday(ClientJobStates.Printed));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _host.JobChanged -= OnHostJobChanged;
                _client.JobChanged -= OnClientJobChanged;
            }
            base.Dispose(disposing);
        }
    }
}
