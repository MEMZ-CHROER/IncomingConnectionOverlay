using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// 日志查看窗口：托盘"查看日志"菜单打开（自绘窗口，不调用 notepad）。
/// 只读展示 overlay.log，每 2 秒自动刷新并保持滚动位置，也可手动刷新。
/// </summary>
public sealed class LogViewerForm : Form
{
    private readonly TextBox _view = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.FromArgb(220, 220, 220),
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private int _topCharIdx; // 刷新前视口顶部字符索引，用于刷新后还原查看位置

    public LogViewerForm()
    {
        Text = "IncomingConnectionOverlay — 日志";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 520);
        MinimumSize = new Size(420, 240);

        Controls.Add(_view);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(8, 6, 8, 6),
        };
        var refreshBtn = new Button { Text = "刷新", Width = 80 };
        refreshBtn.Click += (_, _) => RefreshLog();
        var autoLabel = new Label { Text = "每 2 秒自动刷新", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) };
        btnPanel.Controls.Add(refreshBtn);
        btnPanel.Controls.Add(autoLabel);
        Controls.Add(btnPanel);

        _timer.Tick += (_, _) => RefreshLog();
        _timer.Start();
        FormClosed += (_, _) => _timer.Stop();
        RefreshLog();
    }

    private void RefreshLog()
    {
        try
        {
            // 记录当前视口顶部字符索引（取视口左上角第一个可见字符）
            if (_view.TextLength > 0)
            {
                _topCharIdx = _view.GetCharIndexFromPosition(new Point(2, 2));
            }

            string path = OverlayForm.LogPath;
            string text = File.Exists(path) ? File.ReadAllText(path) : "（尚无日志：overlay.log 不存在）";
            _view.Text = text;

            // 还原查看位置：滚动到刷新前视口顶部对应的字符处（追加式日志中该索引指向同一行）
            if (_view.TextLength > 0 && _topCharIdx <= _view.TextLength)
            {
                _view.SelectionStart = _topCharIdx;
                _view.SelectionLength = 0;
                _view.ScrollToCaret();
            }
        }
        catch
        {
            // 文件被占用等异常时静默
        }
    }
}
