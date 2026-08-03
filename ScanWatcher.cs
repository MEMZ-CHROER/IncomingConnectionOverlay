using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// --watch 模式：驻留后台（无窗口），检测端口扫描（nmap 等）并触发一次覆盖层。
///
/// 双信号检测（均无需管理员）：
///   1) 连接表信号（可按源 IP 归因）：每 100ms 轮询 GetExtendedTcpTable（与 netstat 同源），
///      统计确凿的入站事件：SYN_RCVD（半开连接 = SYN 扫描特征）或目标端口处于 LISTEN 的连接。
///      出站连接（本地临时端口、不在 LISTEN 集合）不计 → 浏览器等正常出站流量不误报。
///      同一源 IP 在 3s 窗口内触及 ≥8 个不同端口触发；非回环源另有 ≥24 次事件的补充阈值。
///      回环源（127.x）只走"不同端口数"门槛——本机服务的高频自连（单端口）不会误报。
///   2) RST 洪峰信号（全局，无归因）：轮询 GetTcpStatistics 的 dwOutRsts 计数。
///      对关闭端口的 SYN 扫描（nmap -sS 默认扫 1000 端口，其中绝大多数是关闭的）会让主机
///      瞬间回发大量 RST（实测 -sS localhost：+1000），3s 内增量 ≥40 判定为扫描。
///      该信号能抓到连接表完全不留痕的纯 SYN 扫描。
///
/// 触发：覆盖层 6s 动画播放期间抑制重复触发，结束后 30s 冷却，随后重新武装。
/// 已知局限：用户态轮询对"慢速、低流量"扫描（nmap -T1 等）不敏感；完整覆盖需管理员/Npcap。
/// </summary>
internal static class ScanWatcher
{
    // ---- iphlpapi P/Invoke ----
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const uint NO_ERROR = 0;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(byte[] pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetTcpStatistics(byte[] pStats);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // TCP 状态（MIB_TCP_STATE_*）
    private const uint MIB_TCP_STATE_LISTEN = 2;
    private const uint MIB_TCP_STATE_SYN_RCVD = 4;

    // 检测参数
    private const int PollMs = 100;             // 轮询间隔
    private const int WindowMs = 3000;          // 事件统计窗口
    private const int ScanPortsThreshold = 8;   // 同一源 IP 触及的不同本地端口数
    private const int ScanEventsThreshold = 24; // 或窗口内入站事件总数（仅非回环源）
    private const int RstBurstThreshold = 40;   // 3s 内 RST 发送数增量（关闭端口 SYN 扫描特征）
    private const int CooldownSeconds = 30;     // 触发后冷却，防止同一次扫描连环触发

    private static void Log(string msg) => OverlayForm.Log(msg);

    /// <summary>驻留窗体：隐藏、消息泵 + 轮询定时器（Application.Run 的宿主）。</summary>
    internal sealed class WatchForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollMs };
        private readonly Dictionary<uint, List<(int port, DateTime tick)>> _inbound = new();
        private readonly Queue<(DateTime tick, long outRsts)> _rstSamples = new();
        private readonly HashSet<(uint ip, int localPort, int remotePort)> _seenConns = new(); // 连接级去重：长连接只计首次出现
        private bool _overlayActive;
        private DateTime _lastTrigger = DateTime.MinValue;
        private NotifyIcon _tray;
        private ConfigForm _configWindow;   // 单实例：配置编辑窗口
        private LogViewerForm _logWindow;   // 单实例：日志查看窗口

        public WatchForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Text = "IncomingConnectionOverlay — watch";
            // 异常防护：任何轮询异常都不能让常驻进程崩溃
            _timer.Tick += (_, _) =>
            {
                try { Poll(); }
                catch (Exception ex) { Log("POLL ERROR: " + ex.Message); }
            };
            _timer.Start();
            SetupTray();
            FormClosed += (_, _) => { _timer.Stop(); _tray?.Dispose(); };
        }

        /// <summary>QQ 式托盘：常驻通知区，双击/菜单手动触发一次，菜单退出结束驻留。</summary>
        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Text = "IncomingConnectionOverlay — 扫描检测中（双击可手动触发）",
            };
            try
            {
                using var assets = Assets.Load();
                if (assets.CautionIcon != null)
                {
                    IntPtr h = assets.CautionIcon.GetHicon();
                    try
                    {
                        using var hicon = Icon.FromHandle(h);
                        _tray.Icon = (Icon)hicon.Clone(); // 克隆脱离位图生命周期
                    }
                    finally
                    {
                        DestroyIcon(h); // 释放 GetHicon 产生的 HICON
                    }
                }
            }
            catch
            {
            }
            if (_tray.Icon == null)
            {
                _tray.Icon = SystemIcons.Shield;
            }
            _tray.Visible = true; // 先设 Icon 再 Visible，避免刷新问题

            var menu = new ContextMenuStrip();
            var triggerItem = new ToolStripMenuItem("立即触发一次覆盖层");
            triggerItem.Click += (_, _) => TriggerManual();
            var configItem = new ToolStripMenuItem("修改配置");
            configItem.Click += (_, _) => OpenConfigWindow();
            var logItem = new ToolStripMenuItem("查看日志");
            logItem.Click += (_, _) => OpenLogWindow();
            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (_, _) =>
            {
                _timer.Stop();
                Close();
                Application.Exit();
            };
            menu.Items.Add(triggerItem);
            menu.Items.Add(configItem);
            menu.Items.Add(logItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => TriggerManual();

            // 启动气泡，QQ 式可发现性提示
            try
            {
                _tray.ShowBalloonTip(4000, "IncomingConnectionOverlay", "已驻留后台：检测到端口扫描自动触发覆盖层", ToolTipIcon.Info);
            }
            catch
            {
            }
        }

        /// <summary>打开配置编辑窗口（单实例，重复点击只激活已有窗口）。</summary>
        private void OpenConfigWindow()
        {
            if (_configWindow != null && !_configWindow.IsDisposed)
            {
                _configWindow.Activate();
                return;
            }
            _configWindow = new ConfigForm();
            _configWindow.FormClosed += (_, _) => _configWindow = null;
            _configWindow.Show();
        }

        /// <summary>打开日志查看窗口（单实例，重复点击只激活已有窗口）。</summary>
        private void OpenLogWindow()
        {
            if (_logWindow != null && !_logWindow.IsDisposed)
            {
                _logWindow.Activate();
                return;
            }
            _logWindow = new LogViewerForm();
            _logWindow.FormClosed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }

        /// <summary>托盘手动触发：跳过冷却，但覆盖层播放中不叠加。</summary>
        private void TriggerManual()
        {
            if (_overlayActive)
            {
                return;
            }
            _lastTrigger = DateTime.UtcNow;
            _inbound.Clear();
            _rstSamples.Clear();
            Log("MANUAL trigger from tray — showing overlay");
            ShowOverlay();
        }

        private void NotifyTray()
        {
            try
            {
                _tray?.ShowBalloonTip(3000, "IncomingConnectionOverlay", "检测到端口扫描，已触发警告覆盖层", ToolTipIcon.Warning);
            }
            catch
            {
            }
        }

        private void Poll()
        {
            DateTime now = DateTime.UtcNow;

            // 数据采集始终进行（RST 基线、表事件都要保持新鲜），触发判定在门控之后
            var conns = ReadTcpTable();
            // 连接级去重：只统计【新出现】的连接（元组 = 源IP+本地端口+远端端口）。
            // ESTABLISHED 长连接（RDP/SSH/游戏等）只在建立瞬间计 1 次，之后每轮同元组不再计，
            // 避免持续入站连接在 3s 窗口内刷满事件阈值导致周期性误触发。
            foreach (var c in conns.connections)
            {
                if (_seenConns.Contains(c))
                {
                    continue;
                }
                if (!_inbound.TryGetValue(c.ip, out var list))
                {
                    list = new List<(int, DateTime)>();
                    _inbound[c.ip] = list;
                }
                list.Add((c.localPort, now));
            }
            // 快照更新：只保留当前仍存在的连接；连接断开后若重新出现（同元组）会重新计数，
            // 对 distinctPorts 阈值无影响（同端口），扫描重试场景可接受。
            _seenConns.Clear();
            _seenConns.UnionWith(conns.connections);
            foreach (uint ip in _inbound.Keys.ToList())
            {
                _inbound[ip].RemoveAll(e => (now - e.tick).TotalMilliseconds > WindowMs);
                if (_inbound[ip].Count == 0)
                {
                    _inbound.Remove(ip);
                }
            }

            long outRsts = ReadOutRsts();
            if (outRsts >= 0)
            {
                _rstSamples.Enqueue((now, outRsts));
                while (_rstSamples.Count > 0 && (now - _rstSamples.Peek().tick).TotalMilliseconds > WindowMs)
                {
                    _rstSamples.Dequeue();
                }
            }

            // ---- 门控：播放中 / 冷却期不触发（数据照常积累） ----
            if (_overlayActive)
            {
                return;
            }
            if ((now - _lastTrigger).TotalSeconds < CooldownSeconds)
            {
                return;
            }

            // ---- 信号 2：RST 洪峰（全局，抓纯 SYN 扫描） ----
            if (_rstSamples.Count >= 2)
            {
                long delta = outRsts - _rstSamples.Peek().outRsts;
                if (delta >= RstBurstThreshold)
                {
                    _rstSamples.Clear();
                    Trigger(ip: null, events: null, $"TCP RST burst: +{delta} RSTs in {WindowMs / 1000}s window (closed-port SYN scan signature)");
                    return;
                }
            }

            // ---- 信号 1：连接表（可按源 IP 归因） ----
            foreach (var kv in _inbound)
            {
                uint ip = kv.Key;
                var list = kv.Value;
                // MIB 地址字段按网络字节序字节流存储：小端读 uint 后 (ip & 0xFF) 即 IP 最高字节
                bool loopback = (ip & 0xFF) == 127;
                int distinctPorts = list.Select(e => e.port).Distinct().Count();
                if (distinctPorts >= ScanPortsThreshold || (!loopback && list.Count >= ScanEventsThreshold))
                {
                    Trigger(ip, list, null);
                    break;
                }
            }
        }

        private void Trigger(uint? ip, List<(int port, DateTime tick)> events, string rstDesc)
        {
            _lastTrigger = DateTime.UtcNow;
            _overlayActive = true;
            _inbound.Clear();

            if (rstDesc != null)
            {
                Log($"SCAN DETECTED: {rstDesc} — triggering overlay");
            }
            else
            {
                int distinct = events.Select(e => e.port).Distinct().Count();
                var sample = events.Select(e => e.port).Distinct().OrderBy(p => p).Take(12).ToList();
                Log($"SCAN DETECTED: source={IpToString(ip.Value)} events={events.Count} distinctPorts={distinct} " +
                    $"ports=[{string.Join(",", sample)}{(sample.Count >= 12 ? ",..." : "")}] — triggering overlay");
            }

            NotifyTray(); // 检测到扫描 → 气泡提示
            // 连接表信号有源 IP：按配置 SyncIpToDetail 决定是否把真实攻击源注入详情文字
            // （RST 信号无归因，始终用默认文案）；每次触发读配置，修改即时生效
            bool syncIp = rstDesc == null && Settings.Load().SyncIpToDetail;
            ShowOverlay(syncIp ? $"External connection from {IpToString(ip.Value)}\nLogging all activity to ~/log" : null);
        }

        private void ShowOverlay(string detailOverride = null)
        {
            var overlay = new OverlayForm(preview: false, autostart: true, detailOverride);
            overlay.FormClosed += (_, _) =>
            {
                _overlayActive = false;
                _inbound.Clear();
            };
            overlay.Show(); // 6 秒动画播完自动 Close，ESC 可提前退出
        }

        /// <summary>读当前 TCP 连接表：LISTEN 端口集合 + 当前入站连接集合（SYN_RCVD 或目标在 LISTEN 的连接，含回环源）。</summary>
        private static (HashSet<int> listeners, HashSet<(uint ip, int localPort, int remotePort)> connections) ReadTcpTable()
        {
            var listeners = new HashSet<int>();
            var conns = new HashSet<(uint, int, int)>();

            int size = 0;
            uint rc = GetExtendedTcpTable(null, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (rc != ERROR_INSUFFICIENT_BUFFER || size <= 0)
            {
                return (listeners, conns);
            }

            byte[] buf = new byte[size];
            rc = GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (rc != NO_ERROR)
            {
                return (listeners, conns);
            }

            // MIB_TCPROW_OWNER_PID：state/localAddr/localPort/remoteAddr/remotePort/owningPid，各 4 字节
            int count = BitConverter.ToInt32(buf, 0);
            for (int i = 0; i < count; i++)
            {
                int off = 4 + i * 24;
                uint state = BitConverter.ToUInt32(buf, off);
                uint localPortRaw = BitConverter.ToUInt32(buf, off + 8);
                uint remoteAddr = BitConverter.ToUInt32(buf, off + 12);
                uint remotePortRaw = BitConverter.ToUInt32(buf, off + 16);
                int localPort = (int)(((localPortRaw & 0xFF) << 8) | ((localPortRaw >> 8) & 0xFF)); // 网络字节序 → 主机序
                int remotePort = (int)(((remotePortRaw & 0xFF) << 8) | ((remotePortRaw >> 8) & 0xFF)); // 网络字节序 → 主机序

                if (state == MIB_TCP_STATE_LISTEN)
                {
                    listeners.Add(localPort);
                    continue;
                }
                if (remoteAddr == 0)
                {
                    continue; // 无对端（如本地监听残留状态）
                }
                if (state == MIB_TCP_STATE_SYN_RCVD || listeners.Contains(localPort))
                {
                    conns.Add((remoteAddr, localPort, remotePort));
                }
            }
            return (listeners, conns);
        }

        /// <summary>读 MIB_TCPSTATS.dwOutRsts（累计 RST 发送数）；失败返回 -1。</summary>
        private static long ReadOutRsts()
        {
            byte[] buf = new byte[64]; // MIB_TCPSTATS 15 个 DWORD
            if (GetTcpStatistics(buf) != NO_ERROR)
            {
                return -1;
            }
            return BitConverter.ToUInt32(buf, 52); // dwOutRsts = 第 14 个字段
        }

        private static string IpToString(uint addr)
        {
            // MIB 地址字段按网络字节序字节流存储：GetBytes 的内存字节序即点分十进制顺序（首字节 = IP 最高字节）
            byte[] b = BitConverter.GetBytes(addr);
            return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
        }
    }
}
