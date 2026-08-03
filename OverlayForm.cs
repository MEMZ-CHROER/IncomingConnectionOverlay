using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// 覆盖层窗口：复刻 Hacknet IncomingConnectionOverlay 的 6 秒全屏动画。
/// 覆盖模式：全屏分层透明（UpdateLayeredWindow + CreateDIBSection 保 alpha）、置顶、点击穿透。
/// 预览模式（--preview）：普通可拖动窗口，便于调试。
///
/// 逆向还原说明：exe 旧版签名是 (int previewIdx, int autostart)，且判定为 previewIdx != 0，
/// 导致 FindIndex 返回 -1（未传 --preview）时反而进预览窗口——反转 bug。
/// 本源码已按上游修复为 (bool preview, bool autostart = true)。
/// </summary>
public class OverlayForm : Form
{
    // ---- 窗口扩展样式 ----
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;  // 鼠标点击穿透
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;   // 不进任务栏 / Alt-Tab
    private const int GWL_EXSTYLE = -20;

    // ---- UpdateLayeredWindow / DIB ----
    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref Size psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, UIntPtr count);

    // net48 无 Random.Shared，用实例随机源
    private static readonly Random _rng = new();

    private readonly Settings _settings;      // 可配置项（exe.config appSettings，带默认值）

    private readonly bool _preview;
    private readonly Rectangle _dest;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _clock = new();
    private readonly Assets _assets;

    // 分层渲染缓存（覆盖模式专用）
    private Bitmap _frame;
    private IntPtr _dib = IntPtr.Zero;
    private IntPtr _dibBits;
    private int _rowBytes;

    // 排错日志
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "overlay.log");
    private int _ulwFailures;

    /// <summary>
    /// 已按上游修复为 (bool preview, bool autostart = true)：
    /// 默认全屏覆盖层；--preview 才进可拖动调试窗口。
    /// </summary>
    public OverlayForm(bool preview, bool autostart = true)
    {
        _preview = preview;
        KeyPreview = true;

        _settings = Settings.Load(); // 从 exe.config 读取可配置项，缺失回退默认

        _assets = Assets.Load(); // 内嵌资源加载（缺失时回退 exe 旁 assets/ 目录）

        // 背景统一黑色：覆盖模式防分层窗口初始化前闪白，预览模式与游戏黑底一致
        BackColor = Color.Black;

        // 预览模式走 OnPaint 直画窗口 DC，必须双缓冲否则每帧擦黑重绘会频闪
        DoubleBuffered = true;

        if (_preview)
        {
            Text = "Incoming Connection Overlay — preview";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(800, 450);
            _dest = ClientRectangle;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            ShowInTaskbar = false;
            _dest = Bounds;
        }

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) => Tick();

        if (autostart)
        {
            // 计时从窗口显示那一刻（Shown）才启动，避免把程序启动/窗口创建时间算进动画，
            // 否则会跳过前 0.2s 的入场淡入（黑条展开）。
            Shown += (_, _) => StartRendering();
        }
    }

    private void StartRendering()
    {
        _clock.Restart(); // t=0 从窗口真正显示开始
        _timer.Start();
        if (!_preview)
        {
            Present(0f); // 覆盖模式立即呈现第一帧，避免分层窗口空窗白屏
        }
        else
        {
            Invalidate();
        }
    }

    /// <summary>每帧驱动：绘制 + 播完自动关闭。</summary>
    private void Tick()
    {
        float t = (float)_clock.Elapsed.TotalSeconds;
        if (_preview)
        {
            Invalidate();
        }
        else
        {
            Present(t);
        }

        if (t > _settings.Duration)
        {
            _timer.Stop();
            Close();
        }
    }

    // ================= 分层窗口渲染 =================

    private void EnsureSurface()
    {
        if (_frame != null)
        {
            return;
        }

        _frame = new Bitmap(_dest.Width, _dest.Height, PixelFormat.Format32bppArgb);
        _rowBytes = _dest.Width * 4;

        BITMAPINFO bmi = new();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = _dest.Width;
        bmi.bmiHeader.biHeight = -_dest.Height; // 负 = 自顶向下，与 GDI+ LockBits 行序一致
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        IntPtr hdc = GetDC(IntPtr.Zero);
        _dib = CreateDIBSection(hdc, ref bmi, DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, hdc);

        if (_dib == IntPtr.Zero)
        {
            Log("CreateDIBSection FAILED");
        }
    }

    private void Present(float t)
    {
        if (!IsHandleCreated || _dest.Width <= 0 || _dest.Height <= 0)
        {
            return;
        }

        EnsureSurface();
        if (_frame == null)
        {
            return;
        }

        using (Graphics g = Graphics.FromImage(_frame))
        {
            g.Clear(Color.Transparent);
            DrawFrame(g, new Rectangle(0, 0, _frame.Width, _frame.Height), t);
        }

        Rectangle rect = new(0, 0, _frame.Width, _frame.Height);
        BitmapData data = _frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < _frame.Height; y++)
        {
            RtlMoveMemory(IntPtr.Add(_dibBits, y * _rowBytes), IntPtr.Add(data.Scan0, y * data.Stride), (UIntPtr)_rowBytes);
        }
        _frame.UnlockBits(data);

        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr old = SelectObject(memDc, _dib);

        POINT ptDst = new() { X = Left, Y = Top };
        POINT ptSrc = new() { X = 0, Y = 0 };
        BLENDFUNCTION blend = new()
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA,
        };
        Size size = new(_frame.Width, _frame.Height);
        bool ok = UpdateLayeredWindow(Handle, hdc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        if (!ok)
        {
            _ulwFailures++;
            if (_ulwFailures <= 3)
            {
                int err = Marshal.GetLastWin32Error();
                GetWindowRect(Handle, out RECT wr);
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                Log($"ULW FAIL #{_ulwFailures} err={err} win=({wr.Left},{wr.Top},{wr.Right - wr.Left}x{wr.Bottom - wr.Top}) " +
                    $"bounds=({_dest.Width}x{_dest.Height}) exstyle=0x{ex:X8} dib={_dib.ToInt64():X} dibBits={_dibBits.ToInt64():X}");
            }
        }

        SelectObject(memDc, old);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, hdc);
    }

    // ================= 预览模式绘制 =================

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_preview)
        {
            e.Graphics.Clear(Color.Black);
            DrawFrame(e.Graphics, ClientRectangle, (float)_clock.Elapsed.TotalSeconds);
        }
        base.OnPaint(e);
    }

    // ================= 快照（无头自验证） =================

    public void SaveSnapshot(string path, float t)
    {
        using Bitmap bmp = new(_dest.Width, _dest.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            DrawFrame(g, new Rectangle(0, 0, _dest.Width, _dest.Height), t);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // ================= 动画帧（复刻原版 Draw 逻辑） =================

    private void DrawFrame(Graphics g, Rectangle dest, float t)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        // 尺寸基准：原版常量按 1080p 设计。高分屏（如 2560x1600）固定像素会显小，
        // 按屏幕高度比例放大，保证各分辨率下占屏比例一致。
        float s = dest.Height / _settings.ScaleBase;
        if (s < 0.5f) s = 0.5f;
        if (s > 3.0f) s = 3.0f;

        // 闪烁：开场 0.5s 与收尾 0.5s，100ms 周期 50% 占空比（与原版 num%0.1f<0.05f 一致）
        float blinkT = t > _settings.Duration - 0.5f ? t - (_settings.Duration - 0.5f) : t;
        if (blinkT <= 0.5f && blinkT % _settings.BlinkPeriod < _settings.BlinkOnRatio * _settings.BlinkPeriod)
        {
            return;
        }

        // 淡入淡出：alpha（0~1）与黑条高度（0~120）联动
        float alpha = 1f;
        int barH = (int)(_settings.BarHeightMax * s);
        if (t < _settings.Fade)
        {
            alpha = t / _settings.Fade;
            barH = (int)(_settings.BarHeightMax * s * alpha);
        }
        else if (t > _settings.Duration - _settings.Fade)
        {
            float f = 1f - (t - (_settings.Duration - _settings.Fade));
            alpha = f;
            barH = (int)(_settings.BarHeightMax * s * f);
        }

        if (barH <= 0)
        {
            return;
        }

        // 黑条：全屏宽、垂直居中
        Rectangle bar = new(dest.X, dest.Y + dest.Height / 2 - barH / 2, dest.Width, barH);
        using (SolidBrush black = new(Color.FromArgb((int)(0.9f * 255 * alpha), 0, 0, 0)))
        {
            g.FillRectangle(black, bar);
        }

        // 上下边缘滚动斜纹
        if (_assets.StripePattern != null)
        {
            int stripeH = (int)(_settings.StripeHeightMax * s * alpha);
            DrawStripe(g, new Rectangle(bar.X, bar.Y, bar.Width, stripeH), t, s);
            DrawStripe(g, new Rectangle(bar.X, bar.Bottom - stripeH, bar.Width, stripeH), t, s);
        }

        // 中央排版区
        int cw = Math.Min((int)(_settings.CenterWidth * s), bar.Width);
        Rectangle area = new(bar.X + bar.Width / 2 - cw / 2, bar.Y, cw, barH);

        // 警示图标：宽 = 图标纵横比 × 黑条高；底板外扩 30s → 图标内缩 4s（复刻原版）
        int iconW = _assets.CautionIcon != null
            ? (int)((float)_assets.CautionIcon.Width / _assets.CautionIcon.Height * barH)
            : (int)(barH * 0.8f);
        Rectangle iconRect = Inflate(new Rectangle(area.X, area.Y, iconW, barH), (int)(30 * s));
        Rectangle iconInner = Inflate(iconRect, (int)(-4 * s));

        if (_assets.CautionIconBG != null)
        {
            // 原版：sb.Draw(CautionSignBG, rect, Color.Black * num4) —— 底板贴图 tint 黑色（贴图自带形状，非纯色矩形）
            Color bgTint = Color.FromArgb((int)(255 * alpha), 0, 0, 0);
            DrawTinted(g, _assets.CautionIconBG, iconRect, bgTint);
        }

        if (_assets.CautionIcon != null)
        {
            // 原版：Lerp(Red, DrawColor, 0.95 + 0.05*rand) —— 两点均为纯红，等效恒红
            float lerpT = 0.95f + 0.05f * (float)_rng.NextDouble();
            Color tint = Lerp(Color.Red, _settings.DrawColor, lerpT);
            DrawTinted(g, _assets.CautionIcon, iconInner, tint);
        }

        // 标题/详情共用排版矩形（原版 dest3）：
        //   X = area.X + iconInner.Width + 2*4s - 18s，W = area.Width - (iconRect.Width + 2*4s) + 20s
        // 注：旧版无 Math.Max clamp（上游新版有）
        Rectangle textRect = new(
            area.X + iconInner.Width + (int)(2 * 4 * s) - (int)(18 * s),
            area.Y + (int)(4 * s),
            area.Width - (iconRect.Width + (int)(2 * 4 * s)) + (int)(20 * s),
            (int)(barH * 0.8));

        // 标题："INCOMING CONNECTION"（Kremlin，恒红不随淡入淡出）
        if (_assets.TitleFont != null)
        {
            DrawLabel(g, _settings.TitleText, _assets.TitleFont, textRect, _settings.DrawColor, centerHoriz: false);
        }

        // 详情两行小字（随淡入淡出）：原版 dest3.Y += dest3.Height - 27; dest3.Height = barH * 0.2
        if (_assets.DetailFont != null)
        {
            Rectangle detailRect = new(textRect.X, textRect.Y + textRect.Height - (int)(27 * s), textRect.Width, (int)(barH * 0.2));
            Color detailColor = Color.FromArgb((int)(255 * alpha), _settings.DrawColor.R, _settings.DrawColor.G, _settings.DrawColor.B);
            DrawLabel(g, _settings.DetailText, _assets.DetailFont, detailRect, detailColor, centerHoriz: false);
        }
    }

    private static Rectangle Inflate(Rectangle r, int d)
    {
        return Rectangle.Inflate(r, d, d);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        return Color.FromArgb(
            a.A + (int)((b.A - a.A) * t),
            a.R + (int)((b.R - a.R) * t),
            a.G + (int)((b.G - a.G) * t),
            a.B + (int)((b.B - a.B) * t));
    }

    /// <summary>滚动斜纹：StripePattern 先经红黑 tint 预渲染成缓存贴图，再用 TextureBrush 无缝平铺。
    /// 用 WrapMode.Tile 而不是 DrawImage 逐块画——DrawImage 在 40px 边界处有双线性插值接缝。</summary>
    private void DrawStripe(Graphics g, Rectangle rect, float t, float s)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        Bitmap tile = _assets.StripePattern;
        int tileW = tile.Width;
        EnsureRedStripeTile(tile);
        // 滚动速度随缩放比例放大，与视觉尺寸协调（模仍为 tileW，保证平铺无缝）
        int scroll = (int)((t * _settings.StripeSpeed * tileW * s) % tileW);

        using TextureBrush brush = new(_redStripeTile, WrapMode.Tile);

        // 注意：平移后 FillRectangle 覆盖 [rect.X-scroll, rect.X-scroll+W]，右侧少 scroll 像素会露出黑条，
        // 回绕时又突然补上（即"右侧冒出新斜纹"）。宽度补一个 tileW 余量，保证滚动时右端始终有纹理。
        GraphicsState state = g.Save();
        g.TranslateTransform(rect.X - scroll, rect.Y);
        g.FillRectangle(brush, new Rectangle(0, 0, rect.Width + tileW, rect.Height));
        g.Restore(state);
    }

    private Bitmap _redStripeTile;

    private void EnsureRedStripeTile(Bitmap tile)
    {
        if (_redStripeTile != null)
        {
            return;
        }

        _redStripeTile = new Bitmap(tile.Width, tile.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(_redStripeTile))
        using (ImageAttributes attrs = new())
        {
            // 条纹 tint 跟随配置主色（原版 patternColor=DrawColor）
            attrs.SetColorMatrix(TintMatrix(_settings.DrawColor));
            g.DrawImage(tile, new Rectangle(0, 0, tile.Width, tile.Height), 0, 0, tile.Width, tile.Height, GraphicsUnit.Pixel, attrs);
        }
    }

    /// <summary>按 tint 色绘制贴图（保留 alpha）：输出 = 源色 × tint/255，对应 XNA 乘法 tint。</summary>
    private void DrawTinted(Graphics g, Bitmap bmp, Rectangle dest, Color tint)
    {
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        using ImageAttributes attrs = new();
        attrs.SetColorMatrix(TintMatrix(tint));
        g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
    }

    /// <summary>乘法 tint 的 ColorMatrix：输出 = 源色 × tint/255（alpha 保留）。</summary>
    private static ColorMatrix TintMatrix(Color tint)
    {
        float r = tint.R / 255f, gr = tint.G / 255f, b = tint.B / 255f;
        return new ColorMatrix(new[]
        {
            new float[] { r, 0, 0, 0, 0 },
            new float[] { 0, gr, 0, 0, 0 },
            new float[] { 0, 0, b, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 },
        });
    }

    /// <summary>
    /// 文字等比缩放填入矩形（居中），类似原版 doFontLabelToSize。
    /// 用 Graphics.Transform 缩放，避免每帧重建字体导致抖动/闪烁。
    /// </summary>
    private void DrawLabel(Graphics g, string text, Font font, Rectangle rect, Color color, bool centerHoriz)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        SizeF sz = g.MeasureString(text, font);
        if (sz.Width <= 0 || sz.Height <= 0)
        {
            return;
        }

        float scale = Math.Min(rect.Width / sz.Width, rect.Height / sz.Height);
        if (scale > 2.5f)
        {
            scale = 2.5f; // 防过度放大
        }

        float w = sz.Width * scale;
        float h = sz.Height * scale;
        float x = rect.X + (centerHoriz ? (rect.Width - w) / 2f : 0f);
        float y = rect.Y + (rect.Height - h) / 2f;

        using SolidBrush brush = new(color);
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(scale, scale);
        g.DrawString(text, font, brush, 0, 0);
        g.Restore(state);
    }

    // ================= 生命周期 =================

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            if (!_preview)
            {
                // 分层 + 点击穿透 + 置顶 + 工具窗口（不进任务栏）
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
            }
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_preview)
        {
            Activate(); // 覆盖层抢焦点，保证 ESC 可用
        }
        _assets.PlayActivateSounds(); // 对应原版 Activate()
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        if (_dib != IntPtr.Zero)
        {
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        _frame?.Dispose();
        _redStripeTile?.Dispose();
        _assets.Dispose();
        base.OnFormClosed(e);
    }

    internal static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响运行
        }
    }
}
