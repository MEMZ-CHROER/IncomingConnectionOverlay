using System;
using System.Configuration;
using System.Drawing;
using System.Globalization;

namespace IncomingConnectionOverlay;

/// <summary>
/// 从 exe.config（App.config 编译产物）的 appSettings 读取可配置项。
/// 每项都带默认值：配置缺失/格式错误时回退默认，程序始终可运行。
/// </summary>
public sealed class Settings
{
    public float Duration = 6.0f;            // 动画总时长（秒）
    public float Fade = 0.2f;                // 淡入/淡出时长（秒）
    public int BarHeightMax = 120;           // 黑条最大高度（px，1080p 基准）
    public int CenterWidth = 700;            // 中央排版区宽（px）
    public int StripeHeightMax = 24;         // 斜纹高度（px）
    public float StripeSpeed = 1.0f;         // 斜纹滚动速度（贴图宽/秒）
    public float BlinkPeriod = 0.1f;         // 开场/收尾闪烁周期（秒）
    public float BlinkOnRatio = 0.5f;        // 闪烁占空比（0~1）
    public Color DrawColor = Color.FromArgb(255, 255, 0, 0); // tint 主色（R,G,B）
    public float ScaleBase = 1080f;          // 分辨率缩放基准（屏幕高度 px）
    public string TitleText = "INCOMING CONNECTION";
    public string DetailText = "External unsyndicated UDP traffic on port 22\nLogging all activity to ~/log";

    public static Settings Load()
    {
        var s = new Settings();
        s.Duration = GetFloat("Duration", s.Duration);
        s.Fade = GetFloat("Fade", s.Fade);
        s.BarHeightMax = GetInt("BarHeightMax", s.BarHeightMax);
        s.CenterWidth = GetInt("CenterWidth", s.CenterWidth);
        s.StripeHeightMax = GetInt("StripeHeightMax", s.StripeHeightMax);
        s.StripeSpeed = GetFloat("StripeSpeed", s.StripeSpeed);
        s.BlinkPeriod = GetFloat("BlinkPeriod", s.BlinkPeriod);
        s.BlinkOnRatio = GetFloat("BlinkOnRatio", s.BlinkOnRatio);
        s.ScaleBase = GetFloat("ScaleBase", s.ScaleBase);
        s.TitleText = GetString("TitleText", s.TitleText);
        s.DetailText = GetString("DetailText", s.DetailText);

        string tint = GetString("Tint", null);
        if (tint != null)
        {
            string[] parts = tint.Split(',');
            if (parts.Length >= 3 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) &&
                int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) &&
                int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
            {
                s.DrawColor = Color.FromArgb(255,
                    Math.Max(0, Math.Min(255, r)),
                    Math.Max(0, Math.Min(255, g)),
                    Math.Max(0, Math.Min(255, b)));
            }
        }
        return s;
    }

    private static string GetString(string key, string def)
    {
        string v = ConfigurationManager.AppSettings[key];
        return string.IsNullOrEmpty(v) ? def : v;
    }

    private static float GetFloat(string key, float def)
    {
        string v = ConfigurationManager.AppSettings[key];
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : def;
    }

    private static int GetInt(string key, int def)
    {
        string v = ConfigurationManager.AppSettings[key];
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
    }
}
