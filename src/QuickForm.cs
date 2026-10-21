namespace AnnounceMe;

// A small window for "через 5 минут достать курицу": type, press Enter, done.
public class QuickForm : Form
{
    readonly TrayContext _ctx;
    readonly TextBox _input;
    readonly Label _preview;
    DateTime _voiceUntil;

    public QuickForm(TrayContext ctx)
    {
        _ctx = ctx;
        Text = "Быстрое напоминание";
        Icon = TrayContext.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _input = new TextBox
        {
            Font = new Font("Segoe UI", 14f),
            Width = 560,
            PlaceholderText = "через 5 минут достать курицу из духовки",
            Margin = new Padding(0, 0, 0, 6),
        };
        _input.TextChanged += (_, _) => UpdatePreview();

        var mic = new Button { Text = "🎤 Голосом", AutoSize = true, Font = new Font("Segoe UI", 11f), Margin = new Padding(6, 0, 0, 6) };
        mic.Click += async (_, _) =>
        {
            // The voice typing panel can take focus for a moment; don't hide while it listens.
            _voiceUntil = DateTime.Now.AddSeconds(30);
            _input.Focus();
            await Task.Delay(200);
            VoiceTyping.Start();
        };
        var inputRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        inputRow.Controls.AddRange(new Control[] { _input, mic });

        _preview = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 8) };

        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        presets.Controls.Add(new Label { Text = "Или просто:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        foreach (var (label, minutes) in new[] { ("+5 мин", 5), ("+10 мин", 10), ("+15 мин", 15), ("+30 мин", 30), ("+1 час", 60) })
        {
            var b = new Button { Text = label, AutoSize = true };
            b.Click += (_, _) => AddIn(minutes);
            presets.Controls.Add(b);
        }

        var hint = new Label
        {
            Text = "Примеры: «через 5 минут достать курицу», «в 18:30 позвонить маме», «завтра в 9 купить хлеб», «10м чай».\n" +
                   "Enter — поставить, Esc — закрыть. Голосом: кнопка 🎤 или Win+H.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = SystemColors.GrayText,
        };

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        layout.Controls.AddRange(new Control[] { inputRow, _preview, presets, hint });
        Controls.Add(layout);

        Deactivate += (_, _) =>
        {
            if (DateTime.Now > _voiceUntil) Hide();
        };
        UpdatePreview();
    }

    public void ShowAndFocus()
    {
        _voiceUntil = default;
        _input.Clear();
        UpdatePreview();
        Show();
        Activate();
        BringToFront();
        _input.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            Submit();
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    void UpdatePreview()
    {
        var text = _input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _preview.ForeColor = SystemColors.GrayText;
            _preview.Text = "Напишите, о чём и когда напомнить.";
            return;
        }
        var r = QuickParser.Parse(text, DateTime.Now);
        if (r == null)
        {
            _preview.ForeColor = SystemColors.GrayText;
            _preview.Text = "Не вижу, когда напомнить. Добавьте «через 10 минут» или «в 18:30», или нажмите кнопку ниже.";
            return;
        }
        _preview.ForeColor = Color.FromArgb(22, 101, 52);
        _preview.Text = $"Напомню {QuickParser.Describe(r.At, DateTime.Now)}: {r.Text}";
    }

    void Submit()
    {
        var r = QuickParser.Parse(_input.Text, DateTime.Now);
        if (r == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        _ctx.AddReminder(r.At, r.Text);
        Hide();
    }

    void AddIn(int minutes)
    {
        var text = _input.Text.Trim();
        var parsed = QuickParser.Parse(text, DateTime.Now);
        if (parsed != null) text = parsed.Text;
        if (text.Length == 0) text = "Напоминание";
        else text = char.ToUpper(text[0]) + text[1..];
        _ctx.AddReminder(DateTime.Now.AddMinutes(minutes), text);
        Hide();
    }
}
