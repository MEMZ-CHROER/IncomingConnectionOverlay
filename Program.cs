using System;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// 入口（按 icco.exe 逆向还原的旧版逻辑，与上游 GitHub 源码 HEAD 有差异，见下）。
/// 默认：全屏透明覆盖层（置顶、点击穿透），播放一次 Incoming Connection 动画后自动退出。
/// --preview：窗口模式运行，便于调试动画。
/// --snapshot &lt;path&gt; [t]：渲染第 t 秒（默认 3.0）的一帧到 png 后退出，无头自验证用。
/// --dumpsound &lt;path&gt;：把运行时合成的激活音效 wav 写盘（调试用）。
/// --watch：驻留后台监视入站连接，检测到疑似端口扫描（nmap）时触发一次覆盖层动画。
///
/// 逆向还原说明：exe 旧版把 FindIndex 返回值当 bool 用（不带参数返回 -1，反而进预览窗口），
/// 属明显的反转 bug；本源码已按上游修复为 Array.Exists + Equals + bool（默认全屏覆盖层，
/// 显式传 --preview 才进预览窗口）。如需还原 exe 旧版的错误行为，见 README「与上游差异」一节。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool preview = Array.Exists(args, a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        int snapIdx = Array.FindIndex(args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        string snapshotPath = snapIdx >= 0 && snapIdx + 1 < args.Length ? args[snapIdx + 1] : null;
        bool watch = Array.Exists(args, a => a.Equals("--watch", StringComparison.OrdinalIgnoreCase));

    // DPI awareness 由 app.manifest 声明（PerMonitorV2），net48 无 Application.SetHighDpiMode
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    if (watch)
    {
        // 驻留监视：被扫描就弹一次覆盖层（见 ScanWatcher.cs）
        Application.Run(new ScanWatcher.WatchForm());
        return;
    }

    if (snapshotPath != null)
    {
        // 可选第三参：快照时间点（默认 3.0s）
        float snapT = 3.0f;
        if (snapIdx + 2 < args.Length && float.TryParse(args[snapIdx + 2], out float pt))
        {
            snapT = pt;
        }
        using (var form = new OverlayForm(preview: false, autostart: false))
        {
            form.SaveSnapshot(snapshotPath, snapT);
        }
        Console.WriteLine($"snapshot saved: {snapshotPath} (t={snapT})");
        return;
    }

    int dumpIdx = Array.FindIndex(args, a => a.Equals("--dumpsound", StringComparison.OrdinalIgnoreCase));
    if (dumpIdx >= 0 && dumpIdx + 1 < args.Length)
    {
        using (var assets = Assets.Load())
        {
            assets.DumpActivateSound(args[dumpIdx + 1]);
        }
        Console.WriteLine($"sound dumped: {args[dumpIdx + 1]}");
        return;
    }

    Application.Run(new OverlayForm(preview));
}
}
