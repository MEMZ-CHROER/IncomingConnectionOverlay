assets 目录 — 资源文件清单
==========================

当前已放入的文件（全部 EmbeddedResource 内嵌进 exe，运行时内存加载，无需随 exe 分发）：

  CautionIcon.png      警示图标（黄色三角 + 感叹号）
  CautionIconBG.png    警示图标黑色底板
  StripePattern.png    黄黑斜纹贴图（滚动条纹）
  beep.wav             窗口显示时的提示音（beep）
  doomshock.wav        音效 1（原版 DoomShock）
  brightflash.wav      音效 2（原版 BrightFlash）
  kremlin-1.ttf        标题字体（Kremlin）

说明：
- 缺任何文件程序都会自动降级（跳过对应元素），不会崩溃，但会缺视觉/音效。
- 音效运行时从嵌入资源读出 PCM，beep 与 doomshock/brightflash 混音成单个 wav 纯内存播放
  （不写临时文件，避免杀软启发式误报）。
- 详情小字用的是系统等宽字体（原版 Font7 是位图字体，未使用）。
