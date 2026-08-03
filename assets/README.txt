assets 目录 — 素材获取指引（第三方素材不随仓库分发）
======================================================

本目录下的素材文件**不入库**（.gitignore 已排除），需要编译者自行获取后放入本目录，
构建时以 EmbeddedResource 内嵌进 exe（运行时纯内存加载）。

需要的文件（构建完整效果）：

  CautionIcon.png      警示图标（黄色三角 + 感叹号）
  CautionIconBG.png    警示图标黑色底板
  StripePattern.png    斜纹贴图
  beep.wav             窗口显示提示音
  doomshock.wav        音效 1（原版 DoomShock）
  brightflash.wav      音效 2（原版 BrightFlash）
  kremlin-1.ttf        标题字体（Kremlin）

获取方式：

  方案 A（原版观感）：从《Hacknet》游戏解包。
    ⚠️ 游戏素材版权归开发商（Fell Seal 工作室）所有，仅供个人使用，请勿再分发；
    由此产生的版权风险由使用者自行承担。

  方案 B（零版权风险，推荐）：替换为自由素材——
    字体：Google Fonts 的 Russo One / Oranienbaum（SIL OFL，可商用、可再分发、可内嵌）
    图标/斜纹：可自行绘制（程序支持缺素材降级运行）
    音效：Kenney.nl（CC0）或 freesound.org 筛选 CC0

缺失任一文件时程序自动降级（跳过对应视觉/音效元素），不会崩溃。
