using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace PSL.Core;

/// <summary>主题预设（PCL PageSetupUI.xaml:57-66 的五个基础主题）。</summary>
public sealed record ThemePreset(int Id, string Name, int Hue, int Sat)
{
    public override string ToString() => Name;
}

/// <summary>
/// 启动器主题：运行时按 色相 / 饱和度 重算 PCL 的 1–8 号色并替换资源字典。
/// <para>
/// 公式逐字照搬 PCL <c>Modules/ModSecret.vb:109-113</c>（ThemeRefreshMain）与
/// <c>Modules/Base/ModBase.vb</c> 的 <c>MyColor.FromHSL2</c>（自研分段亮度映射，不是标准 HSL）；
/// 复现脚本见 <c>_verify/pcl_palette2.py</c>。
/// </para>
/// </summary>
public static class PslTheme
{
    /// <summary>主题预设。默认「龙猫蓝」的 210/85 来自 ModSecret.vb:64 的真值；
    /// 其余四个的色相是 PSL 自定的（PCL 的预设色值绑定在付费解锁逻辑里，未公开）。</summary>
    public static readonly ThemePreset[] Presets =
    {
        new(0, "龙猫蓝", 210, 85),
        new(1, "甜柠青", 168, 72),
        new(2, "小草绿", 96, 68),
        new(3, "菠萝黄", 48, 88),
        new(4, "橡木棕", 26, 55)
    };

    /// <summary>PCL 分段亮度映射的中心偏移表（ModBase.vb 的 Cent 数组）。</summary>
    private static readonly double[] Cent =
    {
        +0.1, -0.06, -0.3, -0.19, -0.15, -0.24, -0.32, -0.09, +0.18, +0.05, -0.12, -0.02, +0.1, -0.06, 0
    };

    private static double HueComponent(double v1, double v2, double vH)
    {
        if (vH < 0) vH += 1;
        if (vH > 1) vH -= 1;
        if (vH < 1.0 / 6) return v1 + (v2 - v1) * 6 * vH;
        if (vH < 0.5) return v2;
        if (vH < 2.0 / 3) return v1 + (v2 - v1) * (4 - vH * 6);
        return v1;
    }

    /// <summary>PCL 的 FromHSL（标准 HSL → RGB，ModBase.vb）。</summary>
    private static Color FromHsl(double sH, double sS, double sL)
    {
        double h = sH / 360, s = sS / 100, l = sL / 100;
        s = l < 0.5 ? s * l + l : s * (1 - l) + l;
        l = 2 * l - s;
        return Color.FromRgb(
            (byte)Net48Math.Clamp(HueComponent(l, s, h + 1.0 / 3) * 255, 0, 255),
            (byte)Net48Math.Clamp(HueComponent(l, s, h) * 255, 0, 255),
            (byte)Net48Math.Clamp(HueComponent(l, s, h - 1.0 / 3) * 255, 0, 255));
    }

    /// <summary>PCL 的 FromHSL2（分段亮度映射，ModBase.vb）。</summary>
    public static Color FromHsl2(int hue, int sat, int light)
    {
        if (sat == 0)
        {
            byte g = (byte)Net48Math.Clamp(light * 2.55, 0, 255);
            return Color.FromRgb(g, g, g);
        }

        hue = ((hue % 360) + 360) % 360;
        double center = hue / 30.0;
        int index = (int)Math.Floor(center);
        if (index >= Cent.Length - 1) index = Cent.Length - 2;
        center = 50 - ((1 - center + index) * Cent[index] + (center - index) * Cent[index + 1]) * sat;

        double lightness = light < center ? light / center : 1 + (light - center) / (100 - center);
        lightness *= 50;

        return FromHsl(hue, sat, lightness);
    }

    /// <summary>把主题应用到资源字典（1–8 号色 + 标题栏渐变 + 主色相关画刷）。</summary>
    public static void Apply(int hue, int sat)
    {
        var dict = Application.Current.Resources;

        void Set(string key, Color color) => dict[key] = new SolidColorBrush(color);

        // ModSecret.vb:109-113 的 1–8 号色
        Set("Color1", FromHsl2(hue, (int)(sat * 0.2), 25));
        Set("Color2", FromHsl2(hue, sat, 45));
        Set("Color3", FromHsl2(hue, sat, 55));
        Set("Color4", FromHsl2(hue, sat, 65));
        Set("Color5", FromHsl2(hue, sat, 80));
        Set("Color6", FromHsl2(hue, sat, 91));
        Set("Color7", FromHsl2(hue, sat, 95));
        Set("Color8", FromHsl2(hue, sat, 97));

        // 标题栏渐变（ModSecret.vb:115-118：L = 48 / 54 / 48）
        var title = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        title.GradientStops.Add(new GradientStop(FromHsl2(hue, sat, 48), 0));
        title.GradientStops.Add(new GradientStop(FromHsl2(hue, sat, 54), 0.5));
        title.GradientStops.Add(new GradientStop(FromHsl2(hue, sat, 48), 1));
        dict["BrushTitleBarGradient"] = title;
        dict["BrushTitleBar"] = new SolidColorBrush(FromHsl2(hue, sat, 48));

        // 主色相关
        Color primary = FromHsl2(hue, sat, 45);
        Color primaryLight = FromHsl2(hue, sat, 65);
        Color primaryPale = FromHsl2(hue, sat, 91);
        dict["ColorPrimary"] = primary;
        dict["BrushPrimary"] = new SolidColorBrush(primary);
        dict["BrushPrimaryLight"] = new SolidColorBrush(primaryLight);
        dict["BrushPrimaryPale"] = new SolidColorBrush(primaryPale);
        dict["BrushPrimaryHover"] = new SolidColorBrush(FromHsl2(hue, sat, 52));
        dict["BrushPrimaryPressed"] = new SolidColorBrush(FromHsl2(hue, sat, 38));
        dict["BrushPrimaryLighter"] = new SolidColorBrush(FromHsl2(hue, sat, 80));

        // 页面底色（PCL 的 ColorBg0 = Color4*0.4 + Color5*0.4 + Gray4*0.2）
        var c4 = FromHsl2(hue, sat, 65);
        var c5 = FromHsl2(hue, sat, 80);
        var bg0 = Color.FromRgb(
            (byte)(c4.R * 0.4 + c5.R * 0.4 + 166 * 0.2),
            (byte)(c4.G * 0.4 + c5.G * 0.4 + 166 * 0.2),
            (byte)(c4.B * 0.4 + c5.B * 0.4 + 166 * 0.2));
        dict["ColorBrushBg0"] = new SolidColorBrush(bg0);
        dict["BrushPageBackground"] = new SolidColorBrush(bg0);
    }

    // ==================== 背景图片 ====================

    /// <summary>背景图文件夹。</summary>
    public static string BackgroundDir => Path.Combine(Paths.DataDir, "Background");

    /// <summary>随机加载一张背景图；没有图返回 null。</summary>
    public static ImageSource? PickBackground()
    {
        try
        {
            Directory.CreateDirectory(BackgroundDir);
            string[] files = Directory.GetFiles(BackgroundDir)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (files.Length == 0) return null;

            string pick = files[Net48Compat.Random.Next(files.Length)];
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(pick);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error("加载背景图失败", ex);
            return null;
        }
    }
}
