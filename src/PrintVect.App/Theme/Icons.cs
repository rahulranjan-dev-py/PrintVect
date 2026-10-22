using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace PrintVect.App.Theme
{
    internal enum Glyph
    {
        None, Printer, Share, Jobs, Settings, Page, Retry, Trash, Search, Check, Info, Lock, Copy, Folder, Chevron, ChevronDown,
        Inbox, Warning, InfoMark, Alert, Dash, Pause, Play, Plus, Minus, Network
    }

    /// <summary>
    /// The line icons of the redesign (24-unit grid, 1.8 stroke, round ends), drawn from the same path data as the
    /// canvas so they match it exactly. Only the path commands those icons use are understood: M L H V A Z, upper
    /// and lower case, plus circles and rounded rectangles.
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<Glyph, string[]> Shapes = new Dictionary<Glyph, string[]>
        {
            { Glyph.Printer, new[] { "M7 9V3h10v6", "R3 9 18 8 2", "M7 14h10v7H7z" } },
            { Glyph.Share, new[] { "C12 12 2", "M16.2 7.8a6 6 0 0 1 0 8.4", "M7.8 16.2a6 6 0 0 1 0-8.4", "M19.1 4.9a10 10 0 0 1 0 14.2", "M4.9 19.1a10 10 0 0 1 0-14.2" } },
            { Glyph.Jobs, new[] { "M9 6h11M9 12h11M9 18h11", "M4 6h.01M4 12h.01M4 18h.01" } },
            { Glyph.Settings, new[] { "M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1", "C15 6 2", "C9 12 2", "C17 18 2" } },
            { Glyph.Page, new[] { "M6 3h8l4 4v14H6z", "M14 3v4h4" } },
            { Glyph.Retry, new[] { "M20 12a8 8 0 1 1-2.3-5.7", "M20 4v5h-5" } },
            { Glyph.Trash, new[] { "M4 7h16", "M9 7V4h6v3", "M6 7l1 13h10l1-13" } },
            { Glyph.Search, new[] { "C11 11 7", "M20 20l-4-4" } },
            { Glyph.Check, new[] { "M5 12.5l4.5 4.5L19 7.5" } },
            { Glyph.Info, new[] { "C12 12 9", "M12 11v5", "M12 8h.01" } },
            { Glyph.Lock, new[] { "R5 11 14 9 2", "M8 11V8a4 4 0 0 1 8 0v3" } },
            { Glyph.Copy, new[] { "R9 9 11 11 2", "M5 15V5a2 2 0 0 1 2-2h8" } },
            { Glyph.Folder, new[] { "M3 6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" } },
            { Glyph.Chevron, new[] { "M9 6l6 6-6 6" } },
            { Glyph.ChevronDown, new[] { "M6 9l6 6 6-6" } },
            { Glyph.Inbox, new[] { "M3 13l3-8h12l3 8v6H3z", "M3 13h5l1 3h6l1-3h5" } },
            { Glyph.Warning, new[] { "M12 7v6", "M12 17h.01" } },
            { Glyph.InfoMark, new[] { "M12 11v6", "M12 7h.01" } },
            { Glyph.Alert, new[] { "C12 12 9", "M12 8v5", "M12 16h.01" } },
            { Glyph.Dash, new[] { "M7 12h10" } },
            { Glyph.Pause, new[] { "M9 6v12", "M15 6v12" } },
            { Glyph.Play, new[] { "M8 5l11 7-11 7z" } },
            { Glyph.Plus, new[] { "M12 5v14", "M5 12h14" } },
            { Glyph.Minus, new[] { "M5 12h14" } },
            { Glyph.Network, new[] { "R3 4 18 12 2", "M8 20h8", "M12 16v4" } }
        };

        private static readonly Dictionary<Glyph, GraphicsPath[]> Cache = new Dictionary<Glyph, GraphicsPath[]>();

        /// <summary>Draws <paramref name="icon"/> into <paramref name="bounds"/> (square), in <paramref name="colour"/>.</summary>
        public static void Draw(Graphics g, Glyph icon, RectangleF bounds, Color colour, float stroke = 1.8f)
        {
            if (icon == Glyph.None) return;
            GraphicsPath[] paths = Paths(icon);
            float scale = Math.Min(bounds.Width, bounds.Height) / 24f;
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X + (bounds.Width - 24 * scale) / 2f, bounds.Y + (bounds.Height - 24 * scale) / 2f);
            g.ScaleTransform(scale, scale);
            using (var pen = new Pen(colour, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var dot = new SolidBrush(colour))
            {
                foreach (GraphicsPath path in paths)
                {
                    RectangleF box = path.GetBounds();
                    if (box.Width < 0.1f && box.Height < 0.1f)
                    {
                        // "h.01" with round ends is a dot in the browser; GDI+ draws nothing that short, so fill one.
                        g.FillEllipse(dot, box.X - stroke / 2f, box.Y - stroke / 2f, stroke, stroke);
                    }
                    else
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
            g.Restore(state);
        }

        private static GraphicsPath[] Paths(Glyph icon)
        {
            lock (Cache)
            {
                GraphicsPath[] paths;
                if (Cache.TryGetValue(icon, out paths)) return paths;
                var list = new List<GraphicsPath>();
                foreach (string shape in Shapes[icon])
                {
                    if (shape.StartsWith("C", StringComparison.Ordinal))
                    {
                        float[] n = Numbers(shape.Substring(1));
                        var circle = new GraphicsPath();
                        circle.AddEllipse(n[0] - n[2], n[1] - n[2], n[2] * 2, n[2] * 2);
                        list.Add(circle);
                    }
                    else if (shape.StartsWith("R", StringComparison.Ordinal))
                    {
                        float[] n = Numbers(shape.Substring(1));
                        list.Add(Ui.RoundRect(new RectangleF(n[0], n[1], n[2], n[3]), n[4]));
                    }
                    else
                    {
                        // Each "M" starts its own path, so a dot after a line ("M9 6h11M4 6h.01") is found as a dot.
                        list.AddRange(ParsePath(shape));
                    }
                }
                paths = list.ToArray();
                Cache[icon] = paths;
                return paths;
            }
        }

        private static IEnumerable<GraphicsPath> ParsePath(string d)
        {
            var result = new List<GraphicsPath>();
            GraphicsPath path = null;
            var tokens = new Tokens(d);
            PointF current = PointF.Empty, start = PointF.Empty;
            char command = 'M';
            while (!tokens.AtEnd)
            {
                if (tokens.NextIsCommand)
                {
                    command = tokens.Command();
                }
                bool relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                    {
                        PointF p = tokens.Point(relative ? current : PointF.Empty);
                        path = new GraphicsPath();
                        result.Add(path);
                        current = start = p;
                        command = relative ? 'l' : 'L';   // more pairs after a moveto are linetos
                        break;
                    }
                    case 'L':
                    {
                        PointF p = tokens.Point(relative ? current : PointF.Empty);
                        path.AddLine(current, p);
                        current = p;
                        break;
                    }
                    case 'H':
                    {
                        float x = tokens.Number() + (relative ? current.X : 0);
                        var p = new PointF(x, current.Y);
                        path.AddLine(current, p);
                        current = p;
                        break;
                    }
                    case 'V':
                    {
                        float y = tokens.Number() + (relative ? current.Y : 0);
                        var p = new PointF(current.X, y);
                        path.AddLine(current, p);
                        current = p;
                        break;
                    }
                    case 'A':
                    {
                        float rx = tokens.Number(), ry = tokens.Number();
                        tokens.Number();   // x-axis rotation: always 0 in these icons
                        bool large = tokens.Number() != 0, sweep = tokens.Number() != 0;
                        PointF p = tokens.Point(relative ? current : PointF.Empty);
                        AddArc(path, current, p, rx, ry, large, sweep);
                        current = p;
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        current = start;
                        break;
                    default:
                        throw new FormatException("Icon path command '" + command + "' is not supported.");
                }
            }
            return result;
        }

        /// <summary>SVG's endpoint arc (circles only here) as a GDI+ arc: centre, start angle and sweep (SVG 1.1, F.6.5).</summary>
        private static void AddArc(GraphicsPath path, PointF from, PointF to, float rx, float ry, bool large, bool sweep)
        {
            double x1p = (from.X - to.X) / 2.0, y1p = (from.Y - to.Y) / 2.0;
            double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1)
            {
                rx = (float)(rx * Math.Sqrt(lambda));
                ry = (float)(ry * Math.Sqrt(lambda));
            }
            double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double coef = (large != sweep ? 1 : -1) * Math.Sqrt(Math.Max(0, num / den));
            double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
            double cx = cxp + (from.X + to.X) / 2.0, cy = cyp + (from.Y + to.Y) / 2.0;
            double start = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
            double end = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
            double delta = end - start;
            if (sweep && delta < 0) delta += 2 * Math.PI;
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            path.AddArc((float)(cx - rx), (float)(cy - ry), rx * 2, ry * 2, (float)(start * 180 / Math.PI), (float)(delta * 180 / Math.PI));
        }

        private static float[] Numbers(string text)
        {
            var tokens = new Tokens(text);
            var numbers = new List<float>();
            while (!tokens.AtEnd) numbers.Add(tokens.Number());
            return numbers.ToArray();
        }

        /// <summary>Reads path data: command letters and numbers like "8.4-8.4" or ".01".</summary>
        private sealed class Tokens
        {
            private readonly string _s;
            private int _i;

            public Tokens(string s)
            {
                _s = s;
                Skip();
            }

            public bool AtEnd
            {
                get { return _i >= _s.Length; }
            }

            public bool NextIsCommand
            {
                get { return !AtEnd && char.IsLetter(_s[_i]); }
            }

            public char Command()
            {
                char c = _s[_i++];
                Skip();
                return c;
            }

            public float Number()
            {
                int begin = _i;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                bool dot = false;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || (_s[_i] == '.' && !dot)))
                {
                    if (_s[_i] == '.') dot = true;
                    _i++;
                }
                if (begin == _i) throw new FormatException("Number expected in icon path \"" + _s + "\" at " + begin + ".");
                float value = float.Parse(_s.Substring(begin, _i - begin), CultureInfo.InvariantCulture);
                Skip();
                return value;
            }

            public PointF Point(PointF origin)
            {
                float x = Number();
                float y = Number();
                return new PointF(origin.X + x, origin.Y + y);
            }

            private void Skip()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == ',')) _i++;
            }
        }
    }
}
