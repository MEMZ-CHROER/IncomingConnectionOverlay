# IncomingConnectionOverlay

复刻《Hacknet》游戏 **"INCOMING CONNECTION"** 全屏警告覆盖层动画的 Windows 桌面工具。
运行后播放一次 6 秒的入侵警告动画（黑条 + 滚动斜纹 + 警示图标 + Kremlin 标题 + 警报音）并自动退出；
`--watch` 模式下驻留后台，检测到端口扫描（nmap 等）时自动触发一次。

## 来源与版权（重要）

**本仓库的源码源自 [LDTchara/IncomingConnectionOverlay](https://github.com/LDTchara/IncomingConnectionOverlay)，MIT 许可**，在其基础上做修复与扩展，并非对二进制的新开发实现。

目录中的 `icco.exe` 是上游项目的**旧版构建**。逆向分析（IL 逐方法比对、`--snapshot` 逐像素对拍、`--dumpsound` 逐字节对拍）仅用于**核对行为、定位旧版构建的缺陷**，据此修复了两处问题：

1. `--preview` 判定逻辑反转：旧版把 `Array.FindIndex` 的返回值当 bool 用（不带参数返回 -1，反而进预览窗口；`--preview` 排第 0 位反而进全屏）。已按上游修复语义改为 `Array.Exists + Equals`。
2. 嵌入字体（kremlin-1.ttf）加载成功时 `DetailFont` 未被赋值，详情小字不绘制。已修复为无条件设置。

本仓库相对上游的增量：

- `--watch` 模式（`ScanWatcher.cs`）：后台驻留，双信号检测端口扫描——TCP 连接表轮询（按源 IP 归因，`SYN_RCVD`/LISTEN 端口入站事件）+ RST 洪峰计数（`GetTcpStatistics.dwOutRsts`，可捕获连接表不留痕的纯 `-sS` 扫描）；触发一次覆盖层，30s 冷却，日志写 `overlay.log`
- 构建便利：csproj 引入 `Microsoft.NETFramework.ReferenceAssemblies`，无 .NET Framework 4.8 targeting pack 的环境也可构建

**游戏资源（贴图/字体/音效）从《Hacknet》解包，版权归原游戏，不入库、不二次分发。** 构建需要本地 `assets/` 目录（清单与获取指引见 `assets/README.txt`）；资源缺失时程序自动降级（跳过对应元素，不崩溃）。

## 特性

- **6 秒完整动画**：0.2s 淡入（黑条展开）→ 保持 → 0.5s 淡出，开场/收尾 10Hz 闪烁
- **全屏透明覆盖层**：置顶、鼠标点击穿透、不进任务栏，显示期间不干扰操作
- **分辨率自适应**：元素按屏幕高度相对 1080p 基准缩放
- **三音效纯内存播放**：beep + DoomShock/BrightFlash 双音叠加，运行时混音，不写临时文件
- **`--watch` 扫描检测**：连接表 + RST 洪峰双信号，免管理员
- **零网络、零外部依赖**；.NET Framework 4.8（Win10 1903+ / Win11 系统内置）

## 使用

```
IncomingConnectionOverlay.exe            # 播放 6 秒覆盖层动画后自动退出；ESC 强制退出
IncomingConnectionOverlay.exe --preview  # 800×450 可拖动调试窗口
IncomingConnectionOverlay.exe --snapshot out.png [t]  # 渲染第 t 秒（默认 3.0）一帧到 png
IncomingConnectionOverlay.exe --dumpsound out.wav     # 导出合成音效 wav
IncomingConnectionOverlay.exe --watch    # 驻留监视：检测到端口扫描就弹一次覆盖层
```

### --watch 检测说明

- 每 100ms 轮询 `GetExtendedTcpTable`（与 netstat 同源）：同一源 IP 在 3s 窗口内触及 ≥8 个不同端口（或非回环源 ≥24 次入站事件）→ 判定为扫描
- 同时轮询 `GetTcpStatistics.dwOutRsts`：3s 内 RST 增量 ≥40 → 判定为扫描（可捕获纯 `-sS` SYN 扫描）
- 回环源（127.x）只走"不同端口数"门槛（本机自连/健康检查不误报）
- 触发后 6s 动画 + 30s 冷却，检测与触发记录在 exe 同目录 `overlay.log`
- 局限：慢速低流量扫描（nmap -T1 等）用户态轮询不敏感；完整覆盖需管理员权限或 Npcap

### 配置（exe.config）

可配置项在 exe 同目录的 `IncomingConnectionOverlay.exe.config` 的 `<appSettings>` 中，
缺失或格式错误时回退默认值，程序始终可运行：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `Duration` | `6` | 动画总时长（秒） |
| `Fade` | `0.2` | 淡入/淡出时长（秒） |
| `BarHeightMax` | `120` | 黑条最大高度（px，按 ScaleBase 缩放） |
| `CenterWidth` | `700` | 中央排版区宽（px） |
| `StripeHeightMax` | `24` | 斜纹高度（px） |
| `StripeSpeed` | `1` | 斜纹滚动速度（贴图宽/秒） |
| `BlinkPeriod` | `0.1` | 开场/收尾闪烁周期（秒） |
| `BlinkOnRatio` | `0.5` | 闪烁占空比（0~1） |
| `Tint` | `255,0,0` | tint 主色（R,G,B，0~255） |
| `ScaleBase` | `1080` | 分辨率缩放基准（屏幕高度 px） |
| `TitleText` | `INCOMING CONNECTION` | 标题文字 |
| `DetailText` | `External...` | 详情文字（`&#10;` 为换行） |

## 构建

```powershell
dotnet build IncomingConnectionOverlay.csproj -c Release   # 或 dotnet publish
```

产物在 `bin\Release\net48\`。要求：.NET SDK（8+ 均可，net48 目标）+ 本地 `assets/` 目录。

## 验证记录

还原/修复后的源码与旧版构建 `icco.exe` 行为对拍：

- `--snapshot`：t=0.05/0.1/0.15/1.0/3.0/5.55/5.6/5.75/5.9/6.0/6.5 各时刻 PNG **逐字节一致**
- `--dumpsound`：导出 wav **逐字节一致**
- `--watch`：`nmap -sS / -sT localhost` 均触发；空闲时本机 loopback 自连不误报

## 许可

MIT。代码版权归上游作者 **LDTchara**（[上游仓库](https://github.com/LDTchara/IncomingConnectionOverlay)）；本仓库为其衍生（修复与扩展）。
游戏资源版权归《Hacknet》游戏所有，本仓库不包含、不分发。
