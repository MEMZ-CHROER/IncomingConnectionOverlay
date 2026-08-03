using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// 日志查看窗口：托盘"查看日志"菜单打开（自绘窗口，不调用 notepad）。
/// 只读展示 overlay.log，每 2 秒自动刷新并保持滚动位置，也可手动刷新。
/// </summary>
public sealed class LogViewerForm : Form
{
    // 原生消息：精确读写第一个可见行，刷新后保持查看位置
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE; // WM_USER + 30
    private const int EM_LINESCROLL = 0x00B6;          // WM_USER + 6

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private int _firstVisibleLine; // 刷新前第一个可见行号

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
            // 记录当前第一个可见行号（原生消息，精确）
            if (_view.IsHandleCreated && _view.TextLength > 0)
            {
                _firstVisibleLine = (int)SendMessage(_view.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            }

            string path = OverlayForm.LogPath;
            string text = File.Exists(path) ? File.ReadAllText(path) : "（尚无日志：overlay.log 不存在）";
            _view.Text = text;

            // 还原查看位置：Text 赋值后视口回到顶部（第 0 行），
            // EM_LINESCROLL 相对滚动 N 行 = 回到刷新前的第一个可见行（追加式日志顶部行号不变）。
            if (_view.IsHandleCreated && _view.TextLength > 0 && _firstVisibleLine > 0)
            {
                SendMessage(_view.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)_firstVisibleLine);
            }
        }
        catch
        {
            // 文件被占用等异常时静默
        }
    }
}
