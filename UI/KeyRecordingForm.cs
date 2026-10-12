namespace Shigure;

internal sealed class KeyRecordingForm : Form
{
    private readonly Label _recordArea;
    private readonly Button _saveButton;
    public string Hotkey { get; private set; }

    public KeyRecordingForm(string current)
    {
        Hotkey = KeySender.IsValidHotkey(current) ? current : string.Empty;
        Text = "录制按键";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.Text;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        UiTheme.ConfigureResizableDialog(this, 520, 280, 420, 260);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3,
            BackColor = UiTheme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(new Label
        {
            Text = "按下一个键或组合键，或在下方点击鼠标键。\n录制后点击保存；Esc 取消。",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _recordArea = new Label
        {
            Text = Hotkey.Length == 0 ? "等待按键…" : Hotkey,
            Dock = DockStyle.Fill, BackColor = UiTheme.Field, ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 16F), TabStop = true
        };
        _recordArea.MouseDown += (_, e) =>
        {
            var key = e.Button switch
            {
                MouseButtons.Left => "LBUTTON", MouseButtons.Right => "RBUTTON",
                MouseButtons.Middle => "MBUTTON", MouseButtons.XButton1 => "XBUTTON1",
                MouseButtons.XButton2 => "XBUTTON2", _ => null
            };
            if (key is not null) Record(WithModifiers(key, ModifierKeys));
        };
        root.Controls.Add(_recordArea, 0, 1);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 10, 0, 0), BackColor = UiTheme.Surface
        };
        var cancel = UiTheme.CreateButton("取消", UiTheme.ButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;
        _saveButton = UiTheme.CreateButton("保存", UiTheme.ButtonKind.Primary);
        _saveButton.Enabled = Hotkey.Length > 0;
        _saveButton.DialogResult = DialogResult.OK;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_saveButton);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = _saveButton;
        CancelButton = cancel;
        Shown += (_, _) => _recordArea.Focus();
        UiTheme.ApplyDarkTitleBar(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        var name = KeyName(key);
        if (name is not null)
        {
            Record(WithModifiers(name, keyData));
            return true;
        }
        return key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            || base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyDarkTitleBar(this);
    }

    private void Record(string hotkey)
    {
        Hotkey = hotkey;
        _recordArea.Text = hotkey;
        _saveButton.Enabled = true;
    }

    internal static string WithModifiers(string name, Keys data)
        => (data.HasFlag(Keys.Control) ? "CTRL-" : "")
            + (data.HasFlag(Keys.Alt) ? "ALT-" : "")
            + (data.HasFlag(Keys.Shift) ? "SHIFT-" : "") + name;

    internal static string? KeyName(Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z) return key.ToString();
        if (key is >= Keys.D0 and <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return $"NUMPAD{key - Keys.NumPad0}";
        var name = key switch
        {
            Keys.Oemcomma => ",", Keys.OemPeriod => ".", Keys.OemQuestion => "/",
            Keys.OemSemicolon => ";", Keys.OemQuotes => "'", Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]", Keys.Oemplus => "=", Keys.OemMinus => "-",
            Keys.Oemtilde => "`", Keys.OemBackslash or Keys.OemPipe => "\\",
            Keys.Decimal => "NUMPADDECIMAL", Keys.Add => "NUMPADPLUS", Keys.Subtract => "NUMPADMINUS",
            Keys.Multiply => "NUMPADMULTIPLY", Keys.Divide => "NUMPADDIVIDE",
            _ => key.ToString().ToUpperInvariant()
        };
        return KeySender.IsValidHotkey(name) ? name : null;
    }
}
