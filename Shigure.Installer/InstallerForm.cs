using System.Diagnostics;

namespace Shigure.Installer;

/// <summary>
/// 安装器主界面：收集程序集名称 / 输出路径 / 是否保留配置，触发隔离构建流程。
/// 实际构建逻辑全部在 <see cref="ProjectBuilder"/>，本类只负责交互与状态。
/// </summary>
internal sealed class InstallerForm : Form
{
    private readonly TextBox _nameTextBox;
    private readonly TextBox _outputTextBox;
    private readonly Button _browseButton;
    private readonly Button _buildButton;
    private readonly TextBox _logTextBox;

    private bool _building;

    private static readonly Color Background = Color.FromArgb(30, 30, 30);
    private static readonly Color Field = Color.FromArgb(45, 45, 45);
    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    public InstallerForm()
    {
        Text = "Shigure 安装器";
        Size = new Size(620, 560);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9);

        var titleLabel = new Label
        {
            Text = "生成定制版 Shigure",
            Location = new Point(20, 18),
            Size = new Size(560, 30),
            Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
        };

        var nameLabel = MakeLabel("程序名称（字母 / 数字 / 下划线，不能以数字开头）：", new Point(20, 60), 560);

        _nameTextBox = new TextBox
        {
            Location = new Point(20, 88),
            Size = new Size(560, 28),
            Font = new Font("Consolas", 11),
            BackColor = Field,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "MyWowHelper",
        };

        var outputLabel = MakeLabel("输出路径（留空 = 桌面）：", new Point(20, 128), 560);

        _outputTextBox = new TextBox
        {
            Location = new Point(20, 156),
            Size = new Size(460, 28),
            BackColor = Field,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "未选择（将使用桌面）",
        };

        _browseButton = MakeButton("浏览…", new Point(490, 155), new Size(90, 30), Field);
        _browseButton.Click += (_, _) => BrowseForOutput();

        _buildButton = MakeButton("开始构建", new Point(20, 196), new Size(560, 42), Accent);
        _buildButton.Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
        _buildButton.Click += async (_, _) => await BuildAsync();

        var logLabel = MakeLabel("构建日志：", new Point(20, 252), 560);

        _logTextBox = new TextBox
        {
            Location = new Point(20, 278),
            Size = new Size(560, 224),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(120, 220, 120),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        // 让输入控件随窗口横向拉伸。
        _nameTextBox.Anchor = _outputTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _buildButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        Controls.AddRange(new Control[]
        {
            titleLabel, nameLabel, _nameTextBox,
            outputLabel, _outputTextBox, _browseButton,
            _buildButton, logLabel, _logTextBox,
        });

        AcceptButton = _buildButton;
    }

    private void BrowseForOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择输出文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputTextBox.Text = dialog.SelectedPath;
    }

    private async Task BuildAsync()
    {
        var programName = _nameTextBox.Text.Trim();

        if (!ProjectBuilder.IsValidProgramName(programName))
        {
            MessageBox.Show(this,
                "程序名称不合法：只能包含英文字母、数字、下划线，且不能以数字开头。",
                "请检查名称", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var baseDir = _outputTextBox.Text.Trim();
        if (baseDir.Length == 0)
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (!Directory.Exists(baseDir))
        {
            MessageBox.Show(this, $"输出路径不存在：{baseDir}", "请检查路径",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outputDir = Path.Combine(baseDir, programName);

        SetBuilding(true);
        _logTextBox.Clear();

        try
        {
            var builder = new ProjectBuilder(LogMessage);
            await Task.Run(() => builder.BuildAndPackage(programName, outputDir));

            LogMessage(string.Empty);
            LogMessage($"✓ 构建完成：{outputDir}");

            var open = MessageBox.Show(this,
                $"构建成功！\n\n输出目录：{outputDir}\n\n是否打开输出文件夹？",
                "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (open == DialogResult.Yes && Directory.Exists(outputDir))
                Process.Start("explorer.exe", $"\"{outputDir}\"");
        }
        catch (Exception ex)
        {
            LogMessage(string.Empty);
            LogMessage($"✗ 构建失败：{ex.Message}");
            MessageBox.Show(this, $"构建失败：\n\n{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBuilding(false);
        }
    }

    private void SetBuilding(bool building)
    {
        _building = building;
        _buildButton.Text = building ? "构建中…" : "开始构建";
        _buildButton.Enabled = !building;
        _nameTextBox.Enabled = !building;
        _outputTextBox.Enabled = !building;
        _browseButton.Enabled = !building;
        UseWaitCursor = building;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_building)
        {
            MessageBox.Show(this, "正在构建中，请等待构建完成后再关闭。", "请稍候",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void LogMessage(string message)
    {
        if (_logTextBox.InvokeRequired)
        {
            _logTextBox.BeginInvoke(() => LogMessage(message));
            return;
        }

        _logTextBox.AppendText(message + Environment.NewLine);
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private static Label MakeLabel(string text, Point location, int width) => new()
    {
        Text = text,
        Location = location,
        Size = new Size(width, 22),
        ForeColor = Color.LightGray,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };

    private static Button MakeButton(string text, Point location, Size size, Color back)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return button;
    }
}
