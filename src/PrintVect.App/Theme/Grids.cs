using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// Lists in the dark look. A DataGridView can be coloured completely (a ListView keeps a light header), so every
    /// list of printers and jobs is one of these, read-only unless a column says otherwise.
    /// </summary>
    internal static class Grids
    {
        public static DataGridView Create()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                StandardTab = true,
                ScrollBars = ScrollBars.Vertical,
                Margin = Ui.Pad(0)
            };
            Style(grid);
            return grid;
        }

        public static void Style(DataGridView grid)
        {
            grid.BackgroundColor = Palette.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Palette.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Ui.S(34);
            grid.RowTemplate.Height = Ui.S(36);

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Palette.Card,
                ForeColor = Palette.Muted,
                SelectionBackColor = Palette.Card,
                SelectionForeColor = Palette.Muted,
                Font = ThemeFonts.Small,
                Padding = new Padding(Ui.S(6), 0, Ui.S(6), 0),
                WrapMode = DataGridViewTriState.False
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Palette.Card,
                ForeColor = Palette.Text,
                SelectionBackColor = Palette.Selection,
                SelectionForeColor = Palette.Text,
                Font = ThemeFonts.Text,
                Padding = new Padding(Ui.S(6), 0, Ui.S(6), 0),
                WrapMode = DataGridViewTriState.False
            };
            NativeTheme.UseDarkScrollBars(grid);
        }

        /// <summary>A text column; <paramref name="weight"/> shares out the width like the old list columns did.</summary>
        public static DataGridViewTextBoxColumn Text(string header, float weight, bool mono = false)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };
            if (mono)
            {
                column.DefaultCellStyle = new DataGridViewCellStyle { Font = ThemeFonts.Mono };
            }
            return column;
        }

        /// <summary>
        /// A tick column with the design's own box: 20 px, empty with a grey outline when off, green with a dark tick
        /// when on. The grid's own flat box is tiny, looks the same either way and only reacts to a click right on it,
        /// so the column is read-only for the grid: a click anywhere in the cell, or Space, flips the value here, which
        /// raises CellValueChanged as before.
        /// </summary>
        public static DataGridViewCheckBoxColumn Tick(DataGridView grid, string header, float weight)
        {
            var column = new DataGridViewCheckBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                MinimumWidth = Ui.S(56),
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            };
            grid.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != column.Index) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                PaintTick(e.Graphics, e.CellBounds, e.Value is bool && (bool)e.Value);
                e.Handled = true;
            };
            grid.CellMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == column.Index)
                {
                    Flip(grid.Rows[e.RowIndex].Cells[column.Index]);
                }
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == column.Index)
                {
                    e.Handled = true;
                    Flip(grid.CurrentCell);
                }
            };
            return column;
        }

        private static void Flip(DataGridViewCell cell)
        {
            cell.Value = !(cell.Value is bool && (bool)cell.Value);
        }

        private static void PaintTick(Graphics g, Rectangle cell, bool on)
        {
            int size = Ui.S(20);
            var box = new RectangleF(cell.X + (cell.Width - size) / 2f, cell.Y + (cell.Height - size) / 2f, size, size);
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(box, Ui.S(5)))
            {
                if (on)
                {
                    using (var fill = new SolidBrush(Palette.Accent)) g.FillPath(fill, path);
                    using (var pen = new Pen(Palette.OnAccent, Ui.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(pen, new[]
                        {
                            new PointF(box.X + box.Width * 0.26f, box.Y + box.Height * 0.52f),
                            new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
                            new PointF(box.X + box.Width * 0.75f, box.Y + box.Height * 0.32f)
                        });
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(Palette.Background)) g.FillPath(fill, path);
                    using (var pen = new Pen(Palette.Muted, Ui.S(1) * 1.5f)) g.DrawPath(pen, path);
                }
            }
            g.SmoothingMode = before;
        }

        /// <summary>
        /// Makes <paramref name="row"/> the one selected row (or the first row when it is null), or selects nothing in an
        /// empty list. Moving the current cell is what clears the old selection in a single-select grid.
        /// </summary>
        public static void SelectRow(DataGridView grid, DataGridViewRow row)
        {
            if (row == null && grid.Rows.Count > 0)
            {
                row = grid.Rows[0];
            }
            if (row == null)
            {
                grid.ClearSelection();
                return;
            }
            grid.CurrentCell = row.Cells[0];
            row.Selected = true;
        }

        /// <summary>The colour of a job state: green printed, yellow waiting, red failed, white in progress.</summary>
        public static Color StateColour(string state, string printed, string waiting, string error)
        {
            if (state == printed) return Palette.ConnectedText;
            if (state == error) return Palette.Error;
            if (waiting != null && state == waiting) return Palette.Warning;
            return Palette.Text;
        }
    }
}
