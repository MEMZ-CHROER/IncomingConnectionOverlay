using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;

namespace IncomingConnectionOverlay;

/// <summary>
/// 配置编辑窗口：托盘"修改配置"菜单打开。
/// 编辑 exe.config 的 appSettings 并保存（ConfigurationManager 写回）；
/// 保存后下一次覆盖层动画（Settings.Load 每次创建时重新读取）即生效，无需重启。
/// exe.config 不存在时保存会顺带生成一份（首次运行自动落盘配置）。
/// </summary>
public sealed class ConfigForm : Form
{
    private readonly NumericUpDown _duration = new() { Minimum = 0.5m, Maximum = 120m, DecimalPlaces = 1, Increment = 0.5m };
    private readonly NumericUpDown _fade = new() { Minimum = 0.01m, Maximum = 10m, DecimalPlaces = 2, Increment = 0.05m };
    private readonly NumericUpDown _barHeight = new() { Minimum = 20m, Maximum = 2000m, Increment = 5m };
    private readonly NumericUpDown _centerWidth = new() { Minimum = 100m, Maximum = 8000m, Increment = 10m };
    private readonly NumericUpDown _stripeHeight = new() { Minimum = 4m, Maximum = 500m, Increment = 1m };
    private readonly NumericUpDown _stripeSpeed = new() { Minimum = 0.1m, Maximum = 100m, DecimalPlaces = 1, Increment = 0.1m };
    private readonly NumericUpDown _blinkPeriod = new() { Minimum = 0.01m, Maximum = 10m, DecimalPlaces = 2, Increment = 0.01m };
    private readonly NumericUpDown _blinkOn = new() { Minimum = 0m, Maximum = 1m, DecimalPlaces = 2, Increment = 0.05m };
    private readonly NumericUpDown _tintR = new() { Minimum = 0m, Maximum = 255m, Increment = 1m };
    private readonly NumericUpDown _tintG = new() { Minimum = 0m, Maximum = 255m, Increment = 1m };
    private readonly NumericUpDown _tintB = new() { Minimum = 0m, Maximum = 255m, Increment = 1m };
    private readonly NumericUpDown _scaleBase = new() { Minimum = 100m, Maximum = 8000m, Increment = 10m };
    private readonly TextBox _titleText = new() { Width = 220 };
    private readonly TextBox _detailText = new() { Width = 220, Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _syncIp = new() { Text = "检测到扫描时同步真实源 IP 到详情文字", AutoSize = true };

    public ConfigForm()
    {
        Text = "IncomingConnectionOverlay — 配置";
        FormBorderStyle = FormBorderStyle.Sizable; // 可调大小，内容溢出可滚动
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 680);
        MinimumSize = new Size(460, 560);
        AutoScroll = true; // DPI 放大时内容可滚动，底部按钮不被挤出

        LoadCurrentValues();

        // 布局：两列（标签 + 控件）的表格
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(12, 12, 12, 0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // 标签列：加宽防中文换行
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, "总时长（秒）", _duration);
        AddRow(table, "淡入淡出（秒）", _fade);
        AddRow(table, "黑条高度（px）", _barHeight);
        AddRow(table, "排版区宽（px）", _centerWidth);
        AddRow(table, "斜纹高度（px）", _stripeHeight);
        AddRow(table, "斜纹速度（贴图宽/秒）", _stripeSpeed);
        AddRow(table, "闪烁周期（秒）", _blinkPeriod);
        AddRow(table, "闪烁占空比（0~1）", _blinkOn);
        AddRow(table, "tint R（0~255）", _tintR);
        AddRow(table, "tint G（0~255）", _tintG);
        AddRow(table, "tint B（0~255）", _tintB);
        AddRow(table, "缩放基准（屏幕高）", _scaleBase);
        AddRow(table, "标题文字", _titleText);
        AddRow(table, "详情文字（可换行）", _detailText);
        AddRow(table, "同步源 IP", _syncIp);

        Controls.Add(table);

        // 底部按钮
        var saveBtn = new Button { Text = "保存", Width = 90, DialogResult = DialogResult.OK };
        saveBtn.Click += (_, _) => Save();
        var resetBtn = new Button { Text = "恢复默认", Width = 90 };
        resetBtn.Click += (_, _) => ResetToDefaults();
        var cancelBtn = new Button { Text = "取消", Width = 90, DialogResult = DialogResult.Cancel };

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(12, 8, 12, 8),
        };
        btnPanel.Controls.Add(cancelBtn);
        btnPanel.Controls.Add(resetBtn);
        btnPanel.Controls.Add(saveBtn);
        Controls.Add(btnPanel);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    private void AddRow(TableLayoutPanel table, string label, Control editor)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        editor.Dock = DockStyle.Fill;
        table.Controls.Add(editor);
    }

    private void LoadCurrentValues()
    {
        Settings s = Settings.Load();
        _duration.Value = Clamp((decimal)s.Duration, _duration);
        _fade.Value = Clamp((decimal)s.Fade, _fade);
        _barHeight.Value = Clamp(s.BarHeightMax, _barHeight);
        _centerWidth.Value = Clamp(s.CenterWidth, _centerWidth);
        _stripeHeight.Value = Clamp(s.StripeHeightMax, _stripeHeight);
        _stripeSpeed.Value = Clamp((decimal)s.StripeSpeed, _stripeSpeed);
        _blinkPeriod.Value = Clamp((decimal)s.BlinkPeriod, _blinkPeriod);
        _blinkOn.Value = Clamp((decimal)s.BlinkOnRatio, _blinkOn);
        _tintR.Value = Clamp(s.DrawColor.R, _tintR);
        _tintG.Value = Clamp(s.DrawColor.G, _tintG);
        _tintB.Value = Clamp(s.DrawColor.B, _tintB);
        _scaleBase.Value = Clamp((decimal)s.ScaleBase, _scaleBase);
        _titleText.Text = s.TitleText;
        _detailText.Text = s.DetailText;
        _syncIp.Checked = s.SyncIpToDetail;
    }

    private static decimal Clamp(decimal v, NumericUpDown nud)
    {
        if (v < nud.Minimum) return nud.Minimum;
        if (v > nud.Maximum) return nud.Maximum;
        return v;
    }

    private void ResetToDefaults()
    {
        Settings def = new Settings(); // 代码默认值
        _duration.Value = Clamp((decimal)def.Duration, _duration);
        _fade.Value = Clamp((decimal)def.Fade, _fade);
        _barHeight.Value = Clamp(def.BarHeightMax, _barHeight);
        _centerWidth.Value = Clamp(def.CenterWidth, _centerWidth);
        _stripeHeight.Value = Clamp(def.StripeHeightMax, _stripeHeight);
        _stripeSpeed.Value = Clamp((decimal)def.StripeSpeed, _stripeSpeed);
        _blinkPeriod.Value = Clamp((decimal)def.BlinkPeriod, _blinkPeriod);
        _blinkOn.Value = Clamp((decimal)def.BlinkOnRatio, _blinkOn);
        _tintR.Value = Clamp(def.DrawColor.R, _tintR);
        _tintG.Value = Clamp(def.DrawColor.G, _tintG);
        _tintB.Value = Clamp(def.DrawColor.B, _tintB);
        _scaleBase.Value = Clamp((decimal)def.ScaleBase, _scaleBase);
        _titleText.Text = def.TitleText;
        _detailText.Text = def.DetailText;
        _syncIp.Checked = def.SyncIpToDetail;
    }

    private void Save()
    {
        try
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var app = config.AppSettings.Settings;
            Set(app, "Duration", _duration.Value.ToString());
            Set(app, "Fade", _fade.Value.ToString());
            Set(app, "BarHeightMax", ((int)_barHeight.Value).ToString());
            Set(app, "CenterWidth", ((int)_centerWidth.Value).ToString());
            Set(app, "StripeHeightMax", ((int)_stripeHeight.Value).ToString());
            Set(app, "StripeSpeed", _stripeSpeed.Value.ToString());
            Set(app, "BlinkPeriod", _blinkPeriod.Value.ToString());
            Set(app, "BlinkOnRatio", _blinkOn.Value.ToString());
            Set(app, "Tint", $"{_tintR.Value},{_tintG.Value},{_tintB.Value}");
            Set(app, "ScaleBase", ((int)_scaleBase.Value).ToString());
            Set(app, "TitleText", _titleText.Text);
            Set(app, "DetailText", _detailText.Text);
            Set(app, "SyncIpToDetail", _syncIp.Checked ? "true" : "false");

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
            MessageBox.Show("配置已保存，下一次覆盖层动画生效。", "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存配置失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Set(KeyValueConfigurationCollection app, string key, string value)
    {
        if (app[key] == null)
        {
            app.Add(key, value);
        }
        else
        {
            app[key].Value = value;
        }
    }
}
