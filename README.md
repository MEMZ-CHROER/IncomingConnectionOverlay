# IncomingConnectionOverlay（本目录：icco.exe 的逆向还原源码）

本目录是从 `icco.exe`（v0.4.0.0）逆向还原出来的完整可构建源码，与同目录的
`icco.exe` 一一对应。上游项目：<https://github.com/LDTchara/IncomingConnectionOverlay>
（本目录是**旧版**行为还原，上游 HEAD 已修复若干 bug，差异见下文）。

> 程序本身是《Hacknet》游戏 "INCOMING CONNECTION" 警告动画的桌面复刻：
> 全屏置顶透明覆盖层，播放一次 6 秒入侵警告动画（黑条 + 滚动斜纹 + 警示图标 +
> Kremlin 字体标题 + 警报音），自动退出。无网络行为。

## 文件

- `Program.cs` / `Assets.cs` / `OverlayForm.cs` / `ScanWatcher.cs` — 还原源码 + `--watch` 扫描检测（行为按 exe 的 IL 逐方法核对）
- `assets/` — 从 exe 嵌入资源中抽取的原始资源（png/ttf/wav/README.txt）
- `IncomingConnectionOverlay.csproj` / `app.manifest` / `LICENSE.txt` — 构建配置（沿用上游）

## 构建

```powershell
dotnet build IncomingConnectionOverlay.slnx -c Release
# 或
dotnet publish IncomingConnectionOverlay.slnx -c Release   # bin\Release\net48\publish\
```

## 使用

```
IncomingConnectionOverlay.exe            # 播放 6 秒覆盖层动画后自动退出；ESC 强制退出
IncomingConnectionOverlay.exe --preview  # 窗口模式调试（见下方"注意"）
IncomingConnectionOverlay.exe --snapshot out.png [t]  # 渲染第 t 秒一帧到 png
IncomingConnectionOverlay.exe --dumpsound out.wav     # 导出合成音效
IncomingConnectionOverlay.exe --watch    # 驻留监视：检测到端口扫描（nmap 等）就弹一次覆盖层
```

### --watch（端口扫描触发模式）

后台驻留（无窗口、免管理员），双信号检测：

1. **连接表信号**（可按源 IP 归因）：每 100ms 轮询 `GetExtendedTcpTable`（与 netstat 同源），
   统计确凿的入站事件：`SYN_RCVD`（半开连接 = SYN 扫描特征）或目标端口处于 LISTEN 的连接（connect 扫描）——
   浏览器等出站流量天然排除。同一源 IP 在 3s 窗口内触及 ≥8 个不同端口（或非回环源 ≥24 次事件）→ 判定为扫描。
2. **RST 洪峰信号**（全局，无归因）：轮询 `GetTcpStatistics.dwOutRsts` 计数。
   SYN 扫描对关闭端口会让主机瞬间回发大量 RST（实测 `nmap -sS localhost`：+1000），
   3s 内增量 ≥40 即判定为扫描——连接表完全不留痕的纯 SYN 扫描也能抓到。

- **回环源（127.x）也可以触发**（`nmap localhost` 有效），但只走"≥8 个不同端口"门槛——
  本机服务的高频自连（如单端口健康检查）不会误报
- 触发一次：6s 动画播放期间 + 30s 冷却内不重复触发，之后重新武装
- 检测与触发都记入 `overlay.log`（位于 exe 同目录）

实测（本机）：`nmap -sS -Pn localhost` / `nmap -sT -Pn localhost` 均触发；空闲时本机 loopback 自连流量不误报。
已知局限：慢速、低流量扫描（nmap -T1 等）用户态轮询不敏感；完整覆盖需管理员权限或 Npcap 抓包。

## 还原说明：与上游源码 HEAD 的差异（exe 固有行为）

1. **`--preview` 判定逻辑在 exe 里是反的**（`Program.cs` 已修复）：
   旧版用 `Array.FindIndex` 的**返回值**当 bool 用（`new OverlayForm(previewIdx, 1)`，
   ctor 里 `previewIdx != 0` 才算预览），导致：
   - 不带参数：索引 `-1` → **预览窗口**（即你现在遇到的"怎么是 preview"）
   - `--preview` 恰好是第 0 个参数：索引 `0` → **全屏覆盖层**
   匹配还用 `EndsWith`（`x--preview` 也会命中）。
   **本目录源码已按上游修复**：`Array.Exists` + `Equals` + `bool` 参数，
   默认运行即全屏覆盖层，显式 `--preview` 才进预览窗口。原版 `icco.exe`
   仍是旧逻辑（不带参数出预览窗口，`--preview` 放第一位才出全屏）。
2. **`textRect` 无 clamp**：旧版标题排版矩形不做 `Math.Max` 保护；上游新版有。

其余逻辑（分层渲染、闪烁时间轴、音效内存混音、资源嵌入优先/目录回退、6 秒自动退出等）与上游一致。

## 验证

还原版与原版 exe 的行为对拍：`--snapshot` 在 t=0.05/0.1/0.15/1.0/3.0/5.55/5.6/5.75/5.9/6.0/6.5
各时刻渲染的 PNG 与原版**逐字节一致**；`--dumpsound` 导出的 wav 也逐字节一致
（对拍时 `--snapshot`/`--dumpsound` 路径不受上述 `--preview` 修复影响）。
