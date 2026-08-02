using System;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

internal static class Program
{
    /// <summary>
    /// 入口。
    /// 默认：全屏透明覆盖层（置顶、点击穿透），运行后播放一次 Incoming Connection 动画并自动退出。
    /// --preview：以普通窗口模式运行，便于调试动画（可拖动、可缩放、ESC 退出）。
    /// --snapshot &lt;path&gt;：渲染一帧（t=3.0s）到指定 png 后退出，用于无头自验证绘制内容。
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        bool preview = Array.Exists(args, a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        int snapIdx = Array.FindIndex(args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        string snapshotPath = snapIdx >= 0 && snapIdx + 1 < args.Length ? args[snapIdx + 1] : null;

        // DPI awareness 由 app.manifest 声明（PerMonitorV2），net48 无 Application.SetHighDpiMode
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (snapshotPath != null)
        {
            // 可选第三参：快照时间点（默认 3.0s）
            float snapT = 3.0f;
            if (snapIdx + 2 < args.Length && float.TryParse(args[snapIdx + 2], out float pt))
            {
                snapT = pt;
            }
            using var form = new OverlayForm(preview: false, autostart: false);
            form.SaveSnapshot(snapshotPath, snapT);
            Console.WriteLine($"snapshot saved: {snapshotPath} (t={snapT})");
            return;
        }

        // --dumpsound <path>：把合成的激活音效 wav 写盘（调试用）
        int dumpIdx = Array.FindIndex(args, a => a.Equals("--dumpsound", StringComparison.OrdinalIgnoreCase));
        if (dumpIdx >= 0 && dumpIdx + 1 < args.Length)
        {
            using var assets = Assets.Load();
            assets.DumpActivateSound(args[dumpIdx + 1]);
            Console.WriteLine($"sound dumped: {args[dumpIdx + 1]}");
            return;
        }

        Application.Run(new OverlayForm(preview));
    }
}
