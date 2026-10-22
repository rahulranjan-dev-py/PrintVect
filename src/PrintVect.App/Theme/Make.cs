using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>The labels and layout panels every page builds its cards from, so they all look alike.</summary>
    internal static class Make
    {
        /// <summary>The page's title, 26 px.</summary>
        public static Label PageTitle(string text)
        {
            return Label(text, ThemeFonts.PageTitle, Palette.Text, Ui.Pad(0, 0, 0, 6));
        }

        /// <summary>A section title above a card, 16 px semibold.</summary>
        public static Label SectionTitle(string text)
        {
            return Label(text, ThemeFonts.SectionTitle, Palette.Text, Ui.Pad(0));
        }

        /// <summary>A title inside a card, 15 px semibold.</summary>
        public static Label CardTitle(string text)
        {
            return Label(text, ThemeFonts.CardTitle, Palette.Text, Ui.Pad(0, 0, 0, 4));
        }

        /// <summary>A small label above a value: 12 px semibold capitals, faint.</summary>
        public static Label Caption(string text)
        {
            return Label((text ?? "").ToUpperInvariant(), ThemeFonts.TinySemi, Palette.Faint, Ui.Pad(0, 0, 0, 4));
        }

        /// <summary>Body text that wraps to the width of its column.</summary>
        public static Label Body(string text)
        {
            return Wrap(text, ThemeFonts.Text, Palette.Text, Ui.Pad(0));
        }

        /// <summary>Secondary text, 13 px muted, wrapping.</summary>
        public static Label Muted(string text)
        {
            return Wrap(text, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
        }

        /// <summary>The line under a page title, 14 px muted, wrapping.</summary>
        public static Label Intro(string text)
        {
            return Wrap(text, ThemeFonts.Text, Palette.Muted, Ui.Pad(0));
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
                Margin = margin,
                UseMnemonic = false
            };
        }

        /// <summary>A link in the text ("Set a PIN", "Change in Settings"): underlined, Enter or a click runs it.</summary>
        public static LinkLabel Link(string text, Color colour, Action onClick, Font font = null)
        {
            var link = new LinkLabel
            {
                Text = text ?? "",
                Font = font ?? ThemeFonts.SecondarySemi,
                AutoSize = true,
                LinkColor = colour,
                ActiveLinkColor = colour,
                VisitedLinkColor = colour,
                ForeColor = colour,
                BackColor = Color.Transparent,
                LinkBehavior = LinkBehavior.AlwaysUnderline,
                Margin = Ui.Pad(0),
                UseMnemonic = false,
                TabStop = true
            };
            link.LinkClicked += (s, e) => onClick();
            return link;
        }

        /// <summary>A 1 px line across a card.</summary>
        public static Control Divider(int marginTopPx = 0, int marginBottomPx = 0, bool row = false)
        {
            return new Panel
            {
                Height = 1,
                Dock = DockStyle.Fill,
                BackColor = row ? Palette.RowDivider : Palette.Divider,
                Margin = new Padding(0, Ui.S(marginTopPx), 0, Ui.S(marginBottomPx))
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

        /// <summary>Controls one under the other, each as tall as its content, <paramref name="gapPx"/> apart.</summary>
        public static TableLayoutPanel Stack(int gapPx, params Control[] controls)
        {
            TableLayoutPanel stack = Column(new float[controls.Length]);
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            for (int i = 0; i < controls.Length; i++)
            {
                Control c = controls[i];
                // Blocks take the column's width (text wraps in it); buttons, pills and short labels keep their own.
                bool fill = c is WrapLabel || c is TableLayoutPanel || c is FlowLayoutPanel || c is Card || c is SplitRow
                            || c is TwoColumns || c is Notice || c is DataGridView || c.GetType() == typeof(Panel)
                            || (c is Label && !c.AutoSize);
                c.Dock = fill ? DockStyle.Fill : DockStyle.None;
                if (!fill) c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                Padding m = c.Margin;
                c.Margin = new Padding(m.Left, i == 0 ? m.Top : Math.Max(m.Top, Ui.S(gapPx)), m.Right, m.Bottom);
                stack.Controls.Add(c, 0, i);
            }
            return stack;
        }

        /// <summary>A row of buttons and short texts, left to right, wrapping when the window is narrow.</summary>
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Ui.Pad(0),
                Padding = Ui.Pad(0),
                BackColor = Color.Transparent
            };
            foreach (Control c in controls)
            {
                if (c != null) row.Controls.Add(c);
            }
            return row;
        }

        /// <summary>A title with something on its right (a count, a button), on one line.</summary>
        public static TableLayoutPanel Heading(Control left, Control right)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Ui.Pad(0),
                Padding = Ui.Pad(0),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Anchor = AnchorStyles.Left;
            right.Anchor = AnchorStyles.Right;
            table.Controls.Add(left, 0, 0);
            table.Controls.Add(right, 1, 0);
            return table;
        }

        /// <summary>
        /// A card around one column of content. With no padding (list rows that carry their own), 1 px at the sides and
        /// 6 px at the top and bottom stay free, so the rows never cover the card's border or its round corners.
        /// </summary>
        public static Card Card(Control content, int padX = 20, int padY = 18)
        {
            var padding = new Padding(padX == 0 ? 1 : Ui.S(padX), padY == 0 ? Ui.S(6) : Ui.S(padY), padX == 0 ? 1 : Ui.S(padX), padY == 0 ? Ui.S(6) : Ui.S(padY));
            var card = new Card { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = padding };
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            return card;
        }
    }
}
