using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// Lists in the look of the theme. A DataGridView can be coloured completely (a ListView keeps a system header), so
    /// the job lists are these: a header row in the header colour, a thin line between rows, a status column drawn as
    /// pills and a document column with the job's message in small grey text under the name.
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
                AllowUserToResizeColumns = false,
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
            grid.CellPainting += PaintCell;
            // A list that appears shows no row as picked until someone clicks one (the grid picks the first by itself).
            grid.VisibleChanged += (s, e) =>
            {
                if (grid.Visible && grid.IsHandleCreated && !grid.Focused) grid.BeginInvoke((Action)grid.ClearSelection);
            };
            return grid;
        }

        private static void Style(DataGridView grid)
        {
            grid.BackgroundColor = Palette.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Palette.RowDivider;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Ui.S(42);
            grid.RowTemplate.Height = Ui.S(56);

            // The header row in the card's colour: a tinted strip would cover the card's round top corners.
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Palette.Card,
                ForeColor = Palette.Muted,
                SelectionBackColor = Palette.Card,
                SelectionForeColor = Palette.Muted,
                Font = ThemeFonts.Small,
                Padding = new Padding(Ui.S(7), 0, Ui.S(7), 0),
                WrapMode = DataGridViewTriState.False
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Palette.Card,
                ForeColor = Palette.Text,
                SelectionBackColor = Palette.Selection,
                SelectionForeColor = Palette.Text,
                Font = ThemeFonts.Text,
                Padding = new Padding(Ui.S(7), 0, Ui.S(7), 0),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                WrapMode = DataGridViewTriState.False
            };
            NativeTheme.UseThemedScrollBars(grid);
        }

        /// <summary>A text column; <paramref name="weight"/> shares out the width.</summary>
        public static DataGridViewTextBoxColumn Text(string header, float weight, bool mono = false, int minWidthPx = 60)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = Ui.S(minWidthPx),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };
            if (mono)
            {
                column.DefaultCellStyle = new DataGridViewCellStyle { Font = ThemeFonts.Mono };
            }
            return column;
        }

        /// <summary>A column whose cells are drawn as status pills: the cell's value is the text, its Tag the PillKind.</summary>
        public static DataGridViewTextBoxColumn Pills(string header, float weight, int minWidthPx = 110)
        {
            DataGridViewTextBoxColumn column = Text(header, weight, false, minWidthPx);
            column.Tag = ColumnLook.Pill;
            return column;
        }

        /// <summary>A column with a second, grey line: the cell's value is the first line, its Tag the second.</summary>
        public static DataGridViewTextBoxColumn TwoLines(string header, float weight, int minWidthPx = 160)
        {
            DataGridViewTextBoxColumn column = Text(header, weight, false, minWidthPx);
            column.Tag = ColumnLook.TwoLines;
            return column;
        }

        /// <summary>Gives the first and last columns the card's 20 px inner edge.</summary>
        public static void Edges(DataGridView grid)
        {
            if (grid.Columns.Count == 0) return;
            foreach (DataGridViewColumn column in new[] { grid.Columns[0], grid.Columns[grid.Columns.Count - 1] })
            {
                DataGridViewCellStyle style = column.DefaultCellStyle ?? new DataGridViewCellStyle();
                bool first = column.Index == 0;
                style.Padding = new Padding(first ? Ui.S(20) : Ui.S(7), 0, first ? Ui.S(7) : Ui.S(20), 0);
                column.DefaultCellStyle = style;
                column.HeaderCell.Style.Padding = style.Padding;
            }
        }

        private enum ColumnLook
        {
            Pill,
            TwoLines
        }

        private static void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var grid = (DataGridView)sender;
            if (e.ColumnIndex < 0) return;
            if (e.RowIndex < 0)
            {
                // The header: its text at the column's own inner edge, with a line under it.
                e.PaintBackground(e.CellBounds, false);
                DataGridViewCellStyle own = grid.Columns[e.ColumnIndex].DefaultCellStyle;
                Padding edge = own != null && own.Padding != Padding.Empty ? own.Padding : e.CellStyle.Padding;
                var headerText = new Rectangle(e.CellBounds.X + edge.Left, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - edge.Horizontal), e.CellBounds.Height);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), ThemeFonts.Small, headerText, Palette.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (var pen = new Pen(Palette.Divider))
                {
                    e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                }
                e.Handled = true;
                return;
            }
            object look = grid.Columns[e.ColumnIndex].Tag;
            if (!(look is ColumnLook)) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border);
            DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            Padding pad = e.CellStyle.Padding;
            string text = Convert.ToString(e.Value);
            if ((ColumnLook)look == ColumnLook.Pill)
            {
                if (text.Length > 0)
                {
                    PillKind kind = cell.Tag is PillKind ? (PillKind)cell.Tag : PillKind.Neutral;
                    Size size = StatusPill.Measure(text, kind);
                    int width = Math.Min(size.Width, e.CellBounds.Width - pad.Horizontal);
                    var pill = new Rectangle(e.CellBounds.X + pad.Left, e.CellBounds.Y + (e.CellBounds.Height - size.Height) / 2, width, size.Height);
                    StatusPill.Draw(e.Graphics, pill, text, kind);
                }
            }
            else
            {
                string second = cell.Tag as string;
                var area = new Rectangle(e.CellBounds.X + pad.Left, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - pad.Horizontal), e.CellBounds.Height);
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (string.IsNullOrEmpty(second))
                {
                    TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, area, e.CellStyle.ForeColor, flags | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    int line1 = TextRenderer.MeasureText("Ag", e.CellStyle.Font).Height;
                    int line2 = TextRenderer.MeasureText("Ag", ThemeFonts.Secondary).Height;
                    int top = area.Y + (area.Height - line1 - line2) / 2;
                    TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font, new Rectangle(area.X, top, area.Width, line1), e.CellStyle.ForeColor, flags);
                    TextRenderer.DrawText(e.Graphics, second, ThemeFonts.Secondary, new Rectangle(area.X, top + line1, area.Width, line2), Palette.Muted, flags);
                }
            }
            e.Handled = true;
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
    }
}
