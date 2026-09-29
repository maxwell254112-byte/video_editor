namespace VideoEditor;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(12, 16, 22);
    public static readonly Color Panel = Color.FromArgb(21, 27, 36);
    public static readonly Color Panel2 = Color.FromArgb(28, 36, 48);
    public static readonly Color Line = Color.FromArgb(44, 54, 70);
    public static readonly Color Text = Color.FromArgb(232, 238, 247);
    public static readonly Color Muted = Color.FromArgb(147, 160, 180);
    public static readonly Color Accent = Color.FromArgb(59, 130, 246);
    public static readonly Color Danger = Color.FromArgb(248, 113, 113);
    public static readonly Color Ok = Color.FromArgb(52, 211, 153);
    public static readonly Color Warn = Color.FromArgb(251, 191, 36);
    public static readonly Color Video = Color.FromArgb(37, 99, 235);
    public static readonly Color Image = Color.FromArgb(5, 150, 105);
    public static readonly Color TextClip = Color.FromArgb(217, 119, 6);
    public static readonly Color Audio = Color.FromArgb(124, 58, 237);

    public static Font UiFont { get; } = new("Segoe UI", 9f);
    public static Font TitleFont { get; } = new("Segoe UI Semibold", 11f, FontStyle.Bold);

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = primary ? Accent : Panel2,
            Font = UiFont,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(4, 6, 4, 6),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(38, 48, 64);
        return button;
    }

    public static TextBox TextBox()
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Panel2,
            ForeColor = Text,
            Font = UiFont
        };
    }

    public static ComboBox Combo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel2,
            ForeColor = Text,
            Font = UiFont
        };
    }

    public static NumericUpDown Number(decimal min, decimal max, decimal increment, int decimals)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            DecimalPlaces = decimals,
            BackColor = Panel2,
            ForeColor = Text,
            Font = UiFont,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 120
        };
    }

    public static Label Caption(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Muted,
            Font = UiFont,
            Margin = new Padding(0, 8, 0, 2)
        };
    }

    public static Label Heading(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Text,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 6)
        };
    }
}
