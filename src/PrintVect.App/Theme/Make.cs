using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>The labels and layout panels every page builds its cards from, so they all look alike.</summary>
    internal static class Make
    {
        /// <summary>A card title, 16 px bold.</summary>
        public static Label Title(string text)
        {
            return Label(text, ThemeFonts.CardTitle, Palette.Text, Ui.Pad(0, 0, 0, 4));
        }

        /// <summary>A small label above a value: 11 px bold capitals, muted.</summary>
        public static Label Caption(string text)
        {
            return Label((text ?? "").ToUpperInvariant(), ThemeFonts.Label, Palette.Muted, Ui.Pad(0, 0, 0, 6));
        }

        /// <summary>Body text that wraps to the width of its column.</summary>
        public static Label Body(string text)
        {
            return Wrap(text, ThemeFonts.Text, Palette.Text, Ui.Pad(0, 0, 0, 8));
        }

        /// <summary>Secondary text, 13 px muted, wrapping.</summary>
        public static Label Muted(string text)
        {
            return Wrap(text, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0, 0, 0, 8));
        }

        /// <summary>Text that wraps to its column and grows downwards (messages, explanations).</summary>
        public static Label Wrap(string text, Font font, Color colour, Padding margin)
        {
            return new WrapLabel { Text = text ?? "", Font = font, ForeColor = colour, Margin = margin };
        }

        public static Label Label(string text, Font font, Color colour, Padding margin)
        {
            return new Label
            {
                Text = text ?? "",
                Font = font,
                ForeColor = colour,
                BackColor = Color.Transparent,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = margin,
                UseMnemonic = false
            };
        }

        /// <summary>A one-column table whose rows size to their content, except the ones given as percentages.</summary>
        public static TableLayoutPanel Column(params float[] rowPercents)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = rowPercents.Length,
                Margin = Ui.Pad(0),
                Padding = Ui.Pad(0),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float percent in rowPercents)
            {
                table.RowStyles.Add(percent > 0 ? new RowStyle(SizeType.Percent, percent) : new RowStyle(SizeType.AutoSize));
            }
            return table;
        }

        /// <summary>A row of buttons and short texts, left to right, wrapping when the window is narrow.</summary>
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Ui.Pad(0, 12, 0, 0),
                Padding = Ui.Pad(0),
                BackColor = Color.Transparent
            };
            row.Controls.AddRange(controls);
            return row;
        }

        /// <summary>A card title with something on its right (a count, a button), on one line.</summary>
        public static TableLayoutPanel Heading(Control left, Control right)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Ui.Pad(0, 0, 0, 10),
                Padding = Ui.Pad(0),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Dock = DockStyle.Fill;
            left.Margin = Ui.Pad(0);
            right.Anchor = AnchorStyles.Right;
            table.Controls.Add(left, 0, 0);
            table.Controls.Add(right, 1, 0);
            return table;
        }
    }
}
