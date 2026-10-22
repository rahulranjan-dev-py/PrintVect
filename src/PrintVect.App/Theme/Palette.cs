using System.Drawing;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// One complete set of colours (0.5.0). Light is the owner's redesign canvas of 2026-10-22; Dark carries the
    /// Graphite colours of the first design (docs/design/design-spec.md) over to the same layout.
    /// </summary>
    internal sealed class PaletteSet
    {
        public bool IsDark;
        public string Name;

        public Color Background, Sidebar, Card, Border, Divider, RowDivider;
        public Color Text, TextSecondary, Muted, Faint, Disabled;
        public Color Accent, AccentHover, AccentPressed, AccentText, OnAccent;
        public Color SuccessBg, SuccessText, SuccessDot;
        public Color NeutralBg, NeutralText, NeutralDot;
        public Color WarningBg, WarningText, WarningDot;
        public Color InfoBg, InfoText, InfoIcon;
        public Color DangerBg, DangerText, DangerDot;
        public Color Badge, BadgeText;
        public Color ButtonBorder, InputBorder, InputBg, DisabledBg, DisabledBorder;
        public Color HeaderBg, NestedBorder, Segment, SegmentSelected;
        public Color Hover, Pressed, Selection, NavActive, NavHover, Report, DialogFooter;
        public Color Knob, KnobOff, SwitchOffTrack, SwitchOffBorder, TickOffFill, TickOffBorder;
        public Color Shadow, ToastBg, ToastText;

        public static readonly PaletteSet Light = new PaletteSet
        {
            IsDark = false,
            Name = "light",
            Background = Hex(0xF3F5F4), Sidebar = Hex(0xE9EEEC), Card = Hex(0xFFFFFF), Border = Hex(0xDAE2DE),
            Divider = Hex(0xE6ECE9), RowDivider = Hex(0xEEF2F0),
            Text = Hex(0x17211E), TextSecondary = Hex(0x2C3733), Muted = Hex(0x4F5B57), Faint = Hex(0x5A6661), Disabled = Hex(0x7C8782),
            Accent = Hex(0x0B7A5B), AccentHover = Hex(0x096A4F), AccentPressed = Hex(0x08523D), AccentText = Hex(0x0B6B4F),
            OnAccent = Hex(0xFFFFFF),
            SuccessBg = Hex(0xE2F3EB), SuccessText = Hex(0x0B6B4F), SuccessDot = Hex(0x0B7A5B),
            NeutralBg = Hex(0xECEFEE), NeutralText = Hex(0x45514C), NeutralDot = Hex(0x7C8782),
            WarningBg = Hex(0xFDF1DC), WarningText = Hex(0x6B4100), WarningDot = Hex(0xB26B00),
            InfoBg = Hex(0xE9F1FB), InfoText = Hex(0x1B4F8C), InfoIcon = Hex(0x1D5FA8),
            DangerBg = Hex(0xFBEAE8), DangerText = Hex(0xA3261C), DangerDot = Hex(0xC2362B),
            Badge = Hex(0xDDE5E1), BadgeText = Hex(0x2C3733),
            ButtonBorder = Hex(0xC3CEC9), InputBorder = Hex(0xA9B6B0), InputBg = Hex(0xFFFFFF),
            DisabledBg = Hex(0xF2F4F3), DisabledBorder = Hex(0xE1E7E4),
            HeaderBg = Hex(0xF8FAF9), NestedBorder = Hex(0xE1E8E4), Segment = Hex(0xE3E9E6), SegmentSelected = Hex(0xFFFFFF),
            Hover = Hex(0xF1F4F3), Pressed = Hex(0xE6ECE9), Selection = Hex(0xE4EFEA), NavActive = Hex(0xFFFFFF),
            NavHover = Hex(0xE1E8E5), Report = Hex(0xF4F6F5), DialogFooter = Hex(0xF6F8F7),
            Knob = Hex(0xFFFFFF), KnobOff = Hex(0x5A6661), SwitchOffTrack = Hex(0xFFFFFF), SwitchOffBorder = Hex(0x7C8782),
            TickOffFill = Hex(0xFFFFFF), TickOffBorder = Hex(0x7C8782),
            Shadow = Color.FromArgb(24, 23, 33, 30), ToastBg = Hex(0x17211E), ToastText = Hex(0xFFFFFF)
        };

        public static readonly PaletteSet Dark = new PaletteSet
        {
            IsDark = true,
            Name = "dark",
            Background = Hex(0x111418), Sidebar = Hex(0x151920), Card = Hex(0x181C22), Border = Hex(0x262D37),
            Divider = Hex(0x232A33), RowDivider = Hex(0x20262E),
            Text = Hex(0xEEF1F5), TextSecondary = Hex(0xC9D1DB), Muted = Hex(0xA3AEBB), Faint = Hex(0x939EAC), Disabled = Hex(0x6E7887),
            Accent = Hex(0x3FCF8E), AccentHover = Hex(0x5BD9A0), AccentPressed = Hex(0x33B57A), AccentText = Hex(0x6FE0AE),
            OnAccent = Hex(0x06150E),
            SuccessBg = Hex(0x17352A), SuccessText = Hex(0x6FE0AE), SuccessDot = Hex(0x3FCF8E),
            NeutralBg = Hex(0x232931), NeutralText = Hex(0xAEB8C4), NeutralDot = Hex(0x7D8896),
            WarningBg = Hex(0x3A2E14), WarningText = Hex(0xF5C46B), WarningDot = Hex(0xF0B03F),
            InfoBg = Hex(0x16263A), InfoText = Hex(0x9CC4F5), InfoIcon = Hex(0x6FA8EC),
            DangerBg = Hex(0x3A1F1E), DangerText = Hex(0xF27A72), DangerDot = Hex(0xF27A72),
            Badge = Hex(0x262D37), BadgeText = Hex(0xC9D1DB),
            ButtonBorder = Hex(0x3A4350), InputBorder = Hex(0x4A5563), InputBg = Hex(0x111418),
            DisabledBg = Hex(0x1D2229), DisabledBorder = Hex(0x262D37),
            HeaderBg = Hex(0x1C2128), NestedBorder = Hex(0x2A313B), Segment = Hex(0x1F252D), SegmentSelected = Hex(0x2E3641),
            Hover = Hex(0x20262E), Pressed = Hex(0x262D37), Selection = Hex(0x232B36), NavActive = Hex(0x232A33),
            NavHover = Hex(0x1C2128), Report = Hex(0x14181D), DialogFooter = Hex(0x1C2128),
            Knob = Hex(0xFFFFFF), KnobOff = Hex(0x9BA6B4), SwitchOffTrack = Hex(0x181C22), SwitchOffBorder = Hex(0x6E7887),
            TickOffFill = Hex(0x111418), TickOffBorder = Hex(0x7D8896),
            Shadow = Color.FromArgb(70, 0, 0, 0), ToastBg = Hex(0xEEF1F5), ToastText = Hex(0x111418)
        };

        private static Color Hex(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
    }

    /// <summary>
    /// The colours of the theme in use (Settings, Colours: Light, Dark or System default). Every control reads them
    /// when it is built or painted; a theme switch rebuilds the window, so nothing keeps an old colour.
    /// </summary>
    internal static class Palette
    {
        private static PaletteSet C
        {
            get { return ThemeManager.Colors; }
        }

        public static Color Background { get { return C.Background; } }
        public static Color Sidebar { get { return C.Sidebar; } }
        public static Color Card { get { return C.Card; } }
        public static Color Border { get { return C.Border; } }
        public static Color Divider { get { return C.Divider; } }
        public static Color RowDivider { get { return C.RowDivider; } }
        public static Color Text { get { return C.Text; } }
        public static Color TextSecondary { get { return C.TextSecondary; } }
        public static Color Muted { get { return C.Muted; } }
        public static Color Faint { get { return C.Faint; } }
        public static Color Disabled { get { return C.Disabled; } }
        public static Color Accent { get { return C.Accent; } }
        public static Color AccentHover { get { return C.AccentHover; } }
        public static Color AccentPressed { get { return C.AccentPressed; } }
        public static Color AccentText { get { return C.AccentText; } }
        public static Color OnAccent { get { return C.OnAccent; } }
        public static Color SuccessBg { get { return C.SuccessBg; } }
        public static Color SuccessText { get { return C.SuccessText; } }
        public static Color SuccessDot { get { return C.SuccessDot; } }
        public static Color NeutralBg { get { return C.NeutralBg; } }
        public static Color NeutralText { get { return C.NeutralText; } }
        public static Color NeutralDot { get { return C.NeutralDot; } }
        public static Color WarningBg { get { return C.WarningBg; } }
        public static Color WarningText { get { return C.WarningText; } }
        public static Color WarningDot { get { return C.WarningDot; } }
        public static Color InfoBg { get { return C.InfoBg; } }
        public static Color InfoText { get { return C.InfoText; } }
        public static Color InfoIcon { get { return C.InfoIcon; } }
        public static Color DangerBg { get { return C.DangerBg; } }
        public static Color DangerText { get { return C.DangerText; } }
        public static Color DangerDot { get { return C.DangerDot; } }
        public static Color Badge { get { return C.Badge; } }
        public static Color BadgeText { get { return C.BadgeText; } }
        public static Color ButtonBorder { get { return C.ButtonBorder; } }
        public static Color InputBorder { get { return C.InputBorder; } }
        public static Color InputBg { get { return C.InputBg; } }
        public static Color DisabledBg { get { return C.DisabledBg; } }
        public static Color DisabledBorder { get { return C.DisabledBorder; } }
        public static Color HeaderBg { get { return C.HeaderBg; } }
        public static Color NestedBorder { get { return C.NestedBorder; } }
        public static Color Segment { get { return C.Segment; } }
        public static Color SegmentSelected { get { return C.SegmentSelected; } }
        public static Color Hover { get { return C.Hover; } }
        public static Color Pressed { get { return C.Pressed; } }
        public static Color Selection { get { return C.Selection; } }
        public static Color NavActive { get { return C.NavActive; } }
        public static Color NavHover { get { return C.NavHover; } }
        public static Color Report { get { return C.Report; } }
        public static Color DialogFooter { get { return C.DialogFooter; } }
        public static Color Knob { get { return C.Knob; } }
        public static Color KnobOff { get { return C.KnobOff; } }
        public static Color SwitchOffTrack { get { return C.SwitchOffTrack; } }
        public static Color SwitchOffBorder { get { return C.SwitchOffBorder; } }
        public static Color TickOffFill { get { return C.TickOffFill; } }
        public static Color TickOffBorder { get { return C.TickOffBorder; } }
        public static Color Shadow { get { return C.Shadow; } }
        public static Color ToastBg { get { return C.ToastBg; } }
        public static Color ToastText { get { return C.ToastText; } }
    }
}
