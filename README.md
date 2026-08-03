# IncomingConnectionOverlay

复刻《Hacknet》游戏 **"INCOMING CONNECTION"** 全屏警告覆盖层动画的 Windows 桌面工具。
运行后在屏幕上播放一次 6 秒的入侵警告动画，自动退出。

## 特性

- **6 秒完整动画**：0.2s 淡入（黑条展开）→ 保持 → 0.5s 淡出，开场/收尾 10Hz 闪烁（复刻原版时间轴）
- **全屏透明覆盖层**：置顶、鼠标点击穿透、不进任务栏，显示期间不干扰任何操作
- **复刻原版视觉**：黑色警告条、滚动红黑斜纹、警示图标（随机色调扰动）、Kremlin 标题、终端风详情文字
- **三音效纯内存播放**：beep 提示音 + DoomShock/BrightFlash 双音叠加（运行时混音，不写临时文件）
- **分辨率自适应**：元素尺寸按屏幕高度相对 1080p 基准缩放，任何分辨率下占屏比例一致
- **资源全部内嵌**：贴图/字体/音效编进 exe，单文件分发
- **零网络、零外部依赖、零安装**

## 系统要求

- Windows 10 (1903+) / Windows 11
- .NET Framework 4.8（系统内置组件，**无需安装任何 runtime**）

## 使用

```
IncomingConnectionOverlay.exe
```

运行即播放覆盖层动画，6 秒后自动退出；按 `ESC` 可强制退出。

### 调试参数

| 参数 | 说明 |
|---|---|
| `--preview` | 普通可拖动窗口模式，便于调试动画 |
| `--snapshot <path> [t]` | 渲染第 `t` 秒（默认 3.0）的单帧到 png，无头自验证用 |
| `--dumpsound <path>` | 导出运行时合成的激活音效 wav，检查混音/拼接 |
| `--watch` | 驻留后台（托盘），检测到端口扫描自动触发覆盖层（见下节） |

### 驻留模式（--watch）

```
IncomingConnectionOverlay.exe --watch
```

常驻系统托盘（隐藏窗口 + 100ms 轮询，无管理员权限），检测到疑似端口扫描（nmap 等）时自动触发一次覆盖层动画。双信号检测：

- **连接表信号**（可按源 IP 归因）：轮询 `GetExtendedTcpTable`，只统计**新出现**的入站连接（连接级去重，RDP/SSH 等长连接不会误报）；同一源 IP 在 3 秒内触及 ≥8 个不同端口，或非回环源 ≥24 次事件时触发
- **RST 洪峰信号**（抓纯 SYN 扫描）：轮询 `GetTcpStatistics` 的 `dwOutRsts` 增量，3 秒内 ≥40 判定为对关闭端口的扫描

托盘右键菜单：**立即触发一次覆盖层** / **修改配置**（打开配置窗口，保存即生效） / **查看日志**（自绘日志窗口，2 秒自动刷新） / **退出**。触发后 30 秒冷却防连环触发。

已知局限：用户态轮询对慢速低流量扫描（nmap -T1）不敏感；完整覆盖需管理员权限或 Npcap。

### 配置（exe.config）

可配置项在 exe 同目录的 `IncomingConnectionOverlay.exe.config` 的 `<appSettings>` 中，
缺失或格式错误时回退默认值，程序始终可运行。**--watch 模式下可从托盘"修改配置"窗口编辑并保存**
（exe.config 不存在时保存会顺带生成一份）；保存后下一次覆盖层动画即生效，无需重启：

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

## 构建与发布

```powershell
# 构建
dotnet build IncomingConnectionOverlay.slnx

# 发布（产物在 bin\Release\net48\publish\，约 890KB 单 exe）
dotnet publish IncomingConnectionOverlay.slnx -c Release
```

把 `publish` 目录里的 `IncomingConnectionOverlay.exe`（可选带 `.exe.config`）拷给任何人即可运行。

## 资源

`assets/` 为资源源文件，构建时以 `EmbeddedResource` 内嵌进 exe（运行时从内存加载，不随 exe 分发）：

- `CautionIcon.png` / `CautionIconBG.png` / `StripePattern.png` —— 从《Hacknet》游戏解包
- `beep.wav` / `doomshock.wav` / `brightflash.wav` —— 提示音与双音效
- `kremlin-1.ttf` —— 标题字体（Kremlin）

## 技术要点

- **分层窗口**：`UpdateLayeredWindow` + `CreateDIBSection`（32bpp ARGB 保留 alpha，避免 `GetHbitmap` 丢透明通道）
- **动画逻辑**：逐行复刻原版 `IncomingConnectionOverlay.cs`（官方源码），含闪烁/淡入淡出/布局公式
- **斜纹平铺**：`TextureBrush` + `WrapMode.Tile` 无缝滚动（`DrawImage` 逐块有 1px 插值接缝）
- **音效**：PlaySound 是单通道，多个 `Play()` 会互相打断，故运行时把 beep 与双音效 PCM 混音成一条再内存播放
- **误报说明**：360 等国产杀软的启发式规则对"全屏置顶透明窗口 + P/Invoke + 无签名"敏感，属误报（本程序无任何网络与恶意行为）；已通过去除临时文件解压、补充版本信息等降低误报面

## 致谢

- 资源解包：XnbConverter、[xnb_parse](https://github.com/fesh0r/xnb_parse)
- 参考：Hacknet 官方源码（IncomingConnectionOverlay / PatternDrawer / TextItem）、BASpark（覆盖层方案参考）
