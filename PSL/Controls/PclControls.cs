using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace PSL.Controls;

/// <summary>
/// PCL 缓动（<c>ModAnimation.vb:585-684</c>）。
/// <para>
/// 这两个类把 PCL 的公式原样搬过来，供 WPF 的 <c>EasingFunction</c> 使用。
/// 注意它们**不是** WPF 内置的 <c>CubicEase</c> / <c>BackEase</c>：
/// PCL 的 Back 是 <c>cos</c> 形式，波形不同。
/// </para>
/// <para>
/// 用法：把 <c>EasingMode</c> 固定为 <c>EaseIn</c>，这样 WPF 会直接返回 <c>EaseInCore</c> 的值。
/// </para>
/// </summary>
internal abstract class PclEaseBase : EasingFunctionBase
{
    /// <summary>PCL 的 AniEasePower：Weak=2 Middle=3 Strong=4 ExtraStrong=5。</summary>
    public int Power { get; set; } = 3;

    protected PclEaseBase() => EasingMode = EasingMode.EaseIn;
}

/// <summary>AniEaseInFluent / AniEaseOutFluent：<c>t^p</c> / <c>1-(1-t)^p</c>。</summary>
internal sealed class PclFluentEase : PclEaseBase
{
    public bool IsIn { get; set; }

    protected override Freezable CreateInstanceCore() => new PclFluentEase();

    protected override double EaseInCore(double t)
        => IsIn ? Math.Pow(t, Power) : 1 - Math.Pow(1 - t, Power);
}

/// <summary>
/// AniEaseInBack / AniEaseOutBack：
/// <c>t^p·cos(1.5π(1-t))</c> / <c>1-(1-t)^p·cos(1.5πt)</c>，其中 <c>p = 3 - Power*0.5</c>。
/// </summary>
internal sealed class PclBackEase : PclEaseBase
{
    public bool IsIn { get; set; }

    protected override Freezable CreateInstanceCore() => new PclBackEase();

    protected override double EaseInCore(double t)
    {
        double p = 3 - Power * 0.5;
        return IsIn
            ? Math.Pow(t, p) * Math.Cos(1.5 * Math.PI * (1 - t))
            : 1 - Math.Pow(1 - t, p) * Math.Cos(1.5 * Math.PI * t);
    }
}

/// <summary>
/// 卡片（对应 PCL 的 <c>MyCard</c>，<c>MyCard.vb</c>）。
/// <para>
/// 真值：阴影 <c>ShadowRadius=3</c>、圆角 5、闲置 <c>Opacity=0.07</c> <c>Color=ColorObject1</c>；
/// 底 <c>#F5FFFFFF</c>（A=245）；标题 <c>Bold 13</c> 位于 <c>(15,12)</c> 色 <c>ColorBrush1</c>；
/// 指向时标题转 <c>ColorBrush2</c>、阴影转 <c>ColorObject4</c> 且 <c>Opacity=0.4</c>，均为 90ms。
/// </para>
/// </summary>
public class PclCard : Grid
{
    private const double IdleShadowOpacity = 0.07;
    private const double HoverShadowOpacity = 0.40;

    // PCL MyCard 的阴影色是运行时主题值：闲置 ColorObject1 = Color1 = #333E48，指向 ColorObject4 = Color4 = #3C96EF
    private static readonly Color IdleShadowColor = Color.FromRgb(0x33, 0x3E, 0x48);
    private static readonly Color HoverShadowColor = Color.FromRgb(0x3C, 0x96, 0xEF);

    private readonly Border _shadow;
    private readonly TextBlock _title;
    private readonly SolidColorBrush _titleBrush;

    public PclCard()
    {
        ClipToBounds = false;

        // 阴影外用 MyDropShadow 自绘（九宫格 + ShadowRadius 3）；这里用 DropShadowEffect 近似同一观感
        _shadow = new Border
        {
            Margin = new Thickness(-3),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.White,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 7,
                ShadowDepth = 0,
                Direction = 270,
                Opacity = IdleShadowOpacity,
                Color = IdleShadowColor
            }
        };

        var back = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            IsHitTestVisible = false
        };

        // 自带一份画刷实例，避免动画时碰到已冻结的资源画刷
        _titleBrush = new SolidColorBrush(IdleShadowColor);
        _title = new TextBlock
        {
            Margin = new Thickness(15, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Foreground = _titleBrush
        };

        // 阴影 Effect 是软件渲染的性能大户；给整卡加 BitmapCache 让 WPF 把它缓存成位图，
        // 切页动画时不再逐帧重算模糊（修「动画卡顿」）。
        CacheMode = new BitmapCache();

        Children.Add(_shadow);
        Children.Add(back);
        Children.Add(new ContentPresenter());
        Children.Add(_title);

        MouseEnter += (_, _) => Animate(HoverShadowOpacity, HoverShadowColor, "ColorBrush2");
        MouseLeave += (_, _) => Animate(IdleShadowOpacity, IdleShadowColor, "ColorBrush1");
    }

    /// <summary>卡片标题。传一个空格可保留标题占位但不显示文字（与 PCL 一致）。</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PclCard),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is not PclCard card) return;
                string? text = e.NewValue as string;
                card._title.Text = text;
                card._title.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            }));

    /// <summary>是否启用鼠标指向动画。</summary>
    public bool HasMouseAnimation { get; set; } = true;

    private void Animate(double opacity, Color color, string titleBrushKey)
    {
        if (!HasMouseAnimation || _shadow.Effect is not DropShadowEffect effect) return;

        var duration = TimeSpan.FromMilliseconds(90);
        effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(opacity, duration));
        effect.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(color, duration));
        _titleBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(ResolveColor(titleBrushKey), duration));
    }

    private Color ResolveColor(string key)
        => TryFindResource(key) is SolidColorBrush brush ? brush.Color : IdleShadowColor;
}

/// <summary>
/// 提示条（对应 PCL 的 <c>MyHint</c>，<c>MyHint.xaml</c> + <c>MyHint.xaml.vb:37-55</c>）。
/// PCL 的真实外观是「浅色底 + 左侧 3px 深色竖条 + 深色文字」，圆角 2，文字 <c>LineHeight=16</c>、内边距 <c>12,9</c>。
/// </summary>
public class PclHint : Border
{
    public enum HintTheme
    {
        Blue,
        Red,
        Yellow
    }

    private readonly TextBlock _text;

    public PclHint()
    {
        BorderThickness = new Thickness(3, 0, 0, 0);
        CornerRadius = new CornerRadius(2);
        Padding = new Thickness(12, 9, 12, 9);

        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
        Child = _text;

        ApplyTheme();
    }

    /// <summary>提示文字。</summary>
    public string? Text
    {
        get => _text.Text;
        set => _text.Text = value ?? string.Empty;
    }

    public HintTheme Theme
    {
        get => (HintTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(HintTheme), typeof(PclHint),
            new PropertyMetadata(HintTheme.Red, (d, _) => ((PclHint)d).ApplyTheme()));

    private void ApplyTheme()
    {
        (string back, string border, string text) = Theme switch
        {
            HintTheme.Blue => ("ColorBrushHintBlueBack", "ColorBrushHintBlueBorder", "ColorBrushHintBlueText"),
            HintTheme.Yellow => ("ColorBrushHintYellowBack", "ColorBrushHintYellowBorder", "ColorBrushHintYellowText"),
            _ => ("ColorBrushHintRedBack", "ColorBrushHintRedBorder", "ColorBrushHintRedText")
        };

        SetResourceReference(BackgroundProperty, back);
        SetResourceReference(BorderBrushProperty, border);
        _text.SetResourceReference(TextBlock.ForegroundProperty, text);
    }
}

/// <summary>
/// 图标按钮（对应 PCL 的 <c>MyIconButton</c>，<c>MyIconButton.xaml</c> + <c>.vb:56-90</c>）。
/// <para>
/// 真值（Theme=Color）：常态图标 <c>ColorBrush4</c>、指向 <c>ColorBrush2</c>；圆形（圆角 1000）；图标 <c>Margin=5</c>。
/// 按下时整体缩到 0.8（400ms Fluent Strong），抬起 1.05（250ms Back Weak）再回 1（250ms Fluent Strong）。
/// </para>
/// </summary>
public class PclIconButton : Button
{
    private readonly ShapePath _icon;
    // PCL MyIconButton Theme=Color：有动画时目标为 ColorBrush4，指向 ColorBrush2（运行时取值）
    private readonly SolidColorBrush _brush = new(Color.FromRgb(0x3C, 0x96, 0xEF));
    private Border? _root;
    private bool _pressed;

    public PclIconButton()
    {
        Width = 25;
        Height = 25;
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);

        _icon = new ShapePath
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = _brush,
            IsHitTestVisible = false
        };

        var root = new FrameworkElementFactory(typeof(Border), "PART_Root");
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(1000));
        root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        root.SetValue(RenderTransformOriginProperty, new Point(0.5, 0.5));
        root.SetValue(RenderTransformProperty, new ScaleTransform(1, 1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(presenter);
        Template = new ControlTemplate(typeof(PclIconButton)) { VisualTree = root };
        Content = _icon;

        RefreshColor();
        MouseEnter += (_, _) => RefreshColor();
        MouseLeave += (_, _) => { _pressed = false; ScaleTo(1, 250, new PclFluentEase { Power = 3 }); RefreshColor(); };
        PreviewMouseLeftButtonDown += (_, _) => { _pressed = true; ScaleTo(0.8, 400, new PclFluentEase { Power = 4 }); };
        PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_pressed) return;
            _pressed = false;
            var board = new Storyboard();
            AddScale(board, 1.05, 250, new PclBackEase { Power = 2 });
            AddScale(board, 1.0, 250, new PclFluentEase { Power = 4 }, delay: 250);
            board.Begin();
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = Template?.FindName("PART_Root", this) as Border;
    }

    private void AddScale(Storyboard board, double to, int ms, IEasingFunction ease, int delay = 0)
    {
        if (_root is null) return;
        var sx = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = ease
        };
        Storyboard.SetTarget(sx, _root);
        Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        board.Children.Add(sx);

        var sy = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = ease
        };
        Storyboard.SetTarget(sy, _root);
        Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        board.Children.Add(sy);
    }

    private void ScaleTo(double to, int ms, IEasingFunction ease)
    {
        var board = new Storyboard();
        AddScale(board, to, ms, ease);
        board.Begin();
    }

    /// <summary>矢量图标。</summary>
    public Geometry? Logo
    {
        get => _icon.Data;
        set => _icon.Data = value;
    }

    /// <summary>图标缩放（对应 PCL 的 LogoScale）。</summary>
    public double LogoScale
    {
        get => _icon.RenderTransform is ScaleTransform s ? s.ScaleX : 1;
        set
        {
            _icon.RenderTransformOrigin = new Point(0.5, 0.5);
            _icon.RenderTransform = new ScaleTransform(value, value);
        }
    }

    private void RefreshColor()
        => _brush.Color = IsMouseOver
            ? Color.FromRgb(0x0F, 0x64, 0xB8)   // ColorBrush2
            : Color.FromRgb(0x3C, 0x96, 0xEF);  // ColorBrush4
}

/// <summary>
/// 列表项（对应 PCL 的 <c>MyListItem</c>，<c>MyListItem.xaml</c> + <c>.vb</c>）。
/// <para>
/// 所有尺寸都随 <see cref="FrameworkElement.Height"/> 自适应，因为 PCL 各处都依据
/// <c>Height &lt; 40</c> 分支（其默认高 42；设置 / 帮助 / 下载页用 36，版本选择页 40，添加类 34）：
/// </para>
/// <list type="bullet">
/// <item>勾选列宽：无/Clickable → <c>Height&lt;40 ? 4 : 2</c>；RadioBox/CheckBox → 6</item>
/// <item>图标列宽：<c>34 + (Height&lt;40 ? 0 : 4)</c></item>
/// <item>指向底色圆角：<c>启用缩放动画 或 Height&gt;40 ? 6 : 0</c></item>
/// <item>选中前景：<c>Height&lt;40 ? ColorBrush3 : ColorBrush2</c></item>
/// <item>标题边距：<c>(4,0,0, Height&lt;40 ? 0 : 2)</c></item>
/// </list>
/// </summary>
public class PclListItem : Grid
{
    /// <summary>勾选类型（对应 PCL 的 <c>MyListItem.CheckType</c>）。</summary>
    public enum CheckType
    {
        None,
        Clickable,
        RadioBox,
        CheckBox
    }

    private readonly Border _back;      // PCL 的 RectBack
    private readonly Border _check;     // PCL 的 RectCheck
    private readonly ShapePath _logo;
    private readonly TextBlock _title;
    private readonly TextBlock _info;
    private readonly StackPanel _buttons;

    private bool _checked;
    private int _minPaddingRight = 4;
    private CheckType _type = CheckType.None;
    private Geometry? _logoData;
    private double _logoScale = 1;

    public PclListItem()
    {
        Height = 36;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Background = Brushes.Transparent;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });   // Check
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });   // PaddingLeft
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });   // Logo
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });   // PaddingRight

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 指向背景（PCL 的 RectBack）：底 ColorBrush7 / 描边 ColorBrush6，初始不可见且缩到 0.8
        _back = new Border
        {
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.8, 0.8)
        };
        _back.SetResourceReference(Border.BackgroundProperty, "ColorBrush7");
        _back.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
        SetColumnSpan(_back, 99);
        SetRowSpan(_back, 99);
        Children.Add(_back);

        // 选中竖条（PCL 的 RectCheck）
        _check = new Border
        {
            Width = 5,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-1, 0, 0, 0),
            Height = 0,
            Opacity = 0
        };
        _check.SetResourceReference(Border.BackgroundProperty, "ColorBrush3");
        SetRowSpan(_check, 4);
        Children.Add(_check);

        // 图标：不设固定尺寸，靠 Stretch=Uniform 在单元格内适配（与 PCL 一致）
        _logo = new ShapePath
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SetColumn(_logo, 2);
        SetRowSpan(_logo, 4);
        Children.Add(_logo);

        _title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            IsHitTestVisible = false
        };
        SetColumn(_title, 3);
        SetRow(_title, 1);
        Children.Add(_title);

        // 副文本（PCL 的 LabInfo）：FontSize 12 / 缩进 4 / **不设前景色**（继承 ColorBrush1）/ Opacity 0.6
        _info = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            Opacity = 0.6,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SetColumn(_info, 3);
        SetRow(_info, 2);
        Children.Add(_info);

        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Opacity = 0
        };
        SetColumnSpan(_buttons, 99);
        SetRowSpan(_buttons, 99);
        Children.Add(_buttons);

        ApplyMetrics();
        MouseEnter += (_, _) => Refresh(over: true);
        MouseLeave += (_, _) => Refresh(over: false);
        MouseLeftButtonDown += (_, _) => Refresh(over: true, down: true);
        MouseLeftButtonUp += (_, e) =>
        {
            Refresh(over: IsMouseOver);
            // 点在右侧图标按钮上时只触发按钮，不触发整行
            if (IsInsideIconButton(e.OriginalSource as DependencyObject)) return;
            Click?.Invoke(this, EventArgs.Empty);
        };
    }

    private static bool IsInsideIconButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is PclIconButton) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>标题文字。</summary>
    public string? Title
    {
        get => _title.Text;
        set => _title.Text = (value ?? string.Empty).ReplaceLineEndings("");
    }

    /// <summary>矢量图标；不设置时不占图标列宽。</summary>
    public Geometry? Logo
    {
        get => _logoData;
        set
        {
            _logoData = value;
            _logo.Data = value;
            _logo.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            ApplyMetrics();
        }
    }

    /// <summary>图标缩放（对应 PCL 的 LogoScale；只作用于 RenderTransform）。</summary>
    public double LogoScale
    {
        get => _logoScale;
        set
        {
            _logoScale = value;
            _logo.RenderTransform = new ScaleTransform(value, value);
        }
    }

    /// <summary>第二行说明文字（对应 PCL <c>MyListItem.Info</c>，如版本选择页的文件夹路径）。为空时不占位。</summary>
    public string? Info
    {
        get => _info.Text;
        set
        {
            string text = (value ?? string.Empty).ReplaceLineEndings("");
            _info.Text = text;
            _info.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>右侧最小内边距（对应 PCL 的 <c>MinPaddingRight</c>，左栏列表通常设 30~35）。</summary>
    public int MinPaddingRight
    {
        get => _minPaddingRight;
        set
        {
            _minPaddingRight = value;
            ApplyPaddingRight(IsMouseOver);
        }
    }

    /// <summary>勾选类型（对应 PCL 的 <c>MyListItem.Type</c>）。</summary>
    public CheckType Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            ApplyMetrics();
            ApplyChecked(animate: false);
        }
    }

    /// <summary>
    /// 是否启用缩放动画（对应 PCL 的 <c>IsScaleAnimationEnabled</c>，其默认 True，但各页均显式设 False）。
    /// 影响指向背景的圆角。
    /// </summary>
    public bool IsScaleAnimationEnabled { get; set; }

    /// <summary>是否选中（显示左侧竖条 + 文字转主题色）。</summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            ApplyChecked(animate: true);
        }
    }

    /// <summary>右侧图标按钮组（指向时淡入，与 PCL 的 MyListItem.Buttons 一致）。</summary>
    public IList<PclIconButton> Buttons { get; } = new List<PclIconButton>();

    /// <summary>点击事件。</summary>
    public event EventHandler? Click;

    /// <summary>把一组图标按钮挂到右侧。</summary>
    public void AddButton(PclIconButton button)
    {
        Buttons.Add(button);
        _buttons.Children.Add(button);
        ApplyPaddingRight(IsMouseOver);
    }

    /// <summary>高度变化时重算所有依赖 <c>Height &lt; 40</c> 的尺寸（对应 PCL 的 <c>OnSizeChanged</c>）。</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ApplyMetrics();
    }

    /// <summary>按当前高度重算列宽 / 边距 / 圆角 / 前景色（对应 PCL <c>MyListItem.vb:329-340</c> 与 <c>:303/:330</c>）。</summary>
    private void ApplyMetrics()
    {
        bool small = Height < 40;

        _back.CornerRadius = new CornerRadius(IsScaleAnimationEnabled || Height > 40 ? 6 : 0);

        ColumnDefinitions[0].Width = new GridLength(
            _type is CheckType.None or CheckType.Clickable ? (small ? 4 : 2) : 6);
        ColumnDefinitions[2].Width = new GridLength(_logoData is null ? 0 : 34 + (small ? 0 : 4));

        _logo.Margin = new Thickness(small ? 6 : 8, 8, small ? 4 : 6, 8);
        _title.Margin = new Thickness(4, 0, 0, small ? 0 : 2);
        _info.Margin = new Thickness(4, 0, 0, 0);

        SetTitleForeground();
    }

    /// <summary>选中 / 未选中的前景色（PCL：<c>Height&lt;40 ? ColorBrush3 : ColorBrush2</c>）。</summary>
    private void SetTitleForeground()
    {
        string key = _checked
            ? (Height < 40 ? "ColorBrush3" : "ColorBrush2")
            : "ColorBrush1";
        _title.SetResourceReference(TextBlock.ForegroundProperty, key);
        _logo.SetResourceReference(Shape.FillProperty, key);
    }

    private void ApplyChecked(bool animate)
    {
        SetTitleForeground();

        // PCL 的 RectCheck：选中时高度铺满（上下 6 边距），未选中高度 0
        if (!animate)
        {
            _check.Opacity = _checked ? 1 : 0;
            _check.Height = _checked ? double.NaN : 0;
            _check.VerticalAlignment = _checked ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            _check.Margin = _checked ? new Thickness(-1, 6, 0, 6) : new Thickness(-1, 0, 0, 0);
            return;
        }

        var board = new Storyboard();
        if (_checked)
        {
            _check.VerticalAlignment = VerticalAlignment.Center;
            _check.Margin = new Thickness(-1, 0, 0, 0);
            AddHeight(board, _check, (ActualHeight - _check.ActualHeight - 12) * 0.4, 200, new PclFluentEase { Power = 2 });
            AddHeight(board, _check, (ActualHeight - _check.ActualHeight - 12) * 0.6, 300, new PclBackEase { Power = 2 }, 200);
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(30));
            Storyboard.SetTarget(fade, _check);
            Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
            board.Children.Add(fade);
        }
        else
        {
            AddHeight(board, _check, -_check.ActualHeight, 120, new PclFluentEase { IsIn = true, Power = 2 });
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(70))
            {
                BeginTime = TimeSpan.FromMilliseconds(40)
            };
            Storyboard.SetTarget(fade, _check);
            Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
            board.Children.Add(fade);
        }
        board.Completed += (_, _) =>
        {
            _check.BeginAnimation(FrameworkElement.HeightProperty, null);
            _check.BeginAnimation(UIElement.OpacityProperty, null);
            _check.VerticalAlignment = _checked ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            _check.Height = _checked ? double.NaN : 0;
            _check.Margin = _checked ? new Thickness(-1, 6, 0, 6) : new Thickness(-1, 0, 0, 0);
            _check.Opacity = _checked ? 1 : 0;
        };
        board.Begin();
    }

    private static void AddHeight(Storyboard board, FrameworkElement target, double delta, int ms, IEasingFunction ease, int delay = 0)
    {
        double from = double.IsNaN(target.Height) ? target.ActualHeight : target.Height;
        var anim = new DoubleAnimation(from, from + delta, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(FrameworkElement.Height)"));
        board.Children.Add(anim);
    }

    /// <summary>
    /// 指向 / 按下 / 离开的视觉反馈（对应 PCL <c>MyListItem.vb:546-633</c>）：
    /// 指向与按下 120ms，离开 180ms；指向背景同时做 0.8 → 1 的缩放。
    /// <para>
    /// 说明：PCL 还会对整行 <c>Me</c> 做 1 → 0.98 / 1 的缩放，但那要求 <c>RenderTransform</c> 是 ScaleTransform；
    /// 页面切换动画会把整行的 RenderTransform 换成 TranslateTransform（PCL 亦如此），两者相互覆盖。
    /// 这里只保留指向背景的缩放，避免和切页动画打架。
    /// </para>
    /// </summary>
    private void Refresh(bool over, bool down = false)
    {
        int ms = over ? 120 : 180;
        var board = new Storyboard();

        // 资源字典里的画刷可能是冻结的，动画前先换成自持实例
        EnsureMutableBack();

        var fade = new DoubleAnimation(over ? 1 : 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new PclFluentEase { Power = 3 }
        };
        Storyboard.SetTarget(fade, _back);
        Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
        board.Children.Add(fade);

        string backKey = over ? (down ? "ColorBrush6" : "ColorBrushBg1") : "ColorBrush7";
        var color = new ColorAnimation(ResolveBrushColor(backKey), TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new PclFluentEase { Power = 3 }
        };
        Storyboard.SetTarget(color, _back);
        Storyboard.SetTargetProperty(color, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));
        board.Children.Add(color);

        var scale = new DoubleAnimation(1, TimeSpan.FromMilliseconds(over ? 192 : 180))
        {
            EasingFunction = new PclFluentEase { Power = 3 }
        };
        Storyboard.SetTarget(scale, _back);
        Storyboard.SetTargetProperty(scale, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        board.Children.Add(scale);

        var scaleY = new DoubleAnimation(1, TimeSpan.FromMilliseconds(over ? 192 : 180))
        {
            EasingFunction = new PclFluentEase { Power = 3 }
        };
        Storyboard.SetTarget(scaleY, _back);
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        board.Children.Add(scaleY);

        board.Completed += (_, _) =>
        {
            _back.BeginAnimation(UIElement.OpacityProperty, null);
            _back.BeginAnimation(UIElement.RenderTransformProperty, null);
            _back.Opacity = over ? 1 : 0;
            _back.Background = new SolidColorBrush(ResolveBrushColor(backKey));
            _back.RenderTransform = new ScaleTransform(over ? 1 : 0.8, over ? 1 : 0.8);
        };
        board.Begin();

        _buttons.Opacity = over ? 1 : 0;
        ApplyPaddingRight(over);
    }

    private void EnsureMutableBack()
    {
        if (_back.Background is SolidColorBrush { IsFrozen: false }) return;
        Color current = _back.Background is SolidColorBrush brush ? brush.Color : Colors.White;
        _back.Background = new SolidColorBrush(current);
    }

    private Color ResolveBrushColor(string key)
        => TryFindResource(key) is SolidColorBrush brush ? brush.Color : Colors.Transparent;

    /// <summary>右侧内边距：常态 <see cref="MinPaddingRight"/>，指向时按图标按钮数量撑开（PCL 的 RefreshColor 逻辑）。</summary>
    private void ApplyPaddingRight(bool over)
        => ColumnDefinitions[4].Width = new GridLength(
            over ? Math.Max(_minPaddingRight, 5 + Buttons.Count * 25) : _minPaddingRight);
}

/// <summary>
/// 搜索框（对应 PCL 的 <c>MySearchBox</c>，<c>MySearchBox.xaml</c>）。
/// <para>
/// 真值：整块是 <c>MyCard</c>（带卡片阴影）高 40；输入框 <c>Padding="32,0,40,0"</c> <c>FontSize=13.5</c>；
/// 放大镜 <c>14×14</c> <c>Margin="14,0,0,0"</c> <c>Fill=ColorBrush1</c>；
/// 清除按钮 <c>24×24</c> <c>Margin="0,0,10,0"</c> <c>LogoScale=0.75</c>（Black 主题，Fill = <c>#A0000000</c>）。
/// </para>
/// </summary>
public class PclSearchBox : Grid
{
    private readonly TextBox _input;
    private readonly TextBlock _watermark;
    private readonly ShapePath _clear;

    public PclSearchBox()
    {
        Height = 40;

        var back = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255))
        };
        back.SetResourceReference(UIElement.EffectProperty, "ShadowCard");

        _input = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(32, 0, 40, 0),
            FontSize = 13.5,
            Height = 40,
            MaxLength = 50,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var icon = new ShapePath
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(14, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Data = (Geometry)FindResource("IconSearch")
        };
        icon.SetResourceReference(Shape.FillProperty, "ColorBrush1");

        // 水印文字沿用 PCL MyTextBox 的 labHint 配色（ColorBrushGray4）
        _watermark = new TextBlock
        {
            Margin = new Thickness(33, 0, 40, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13.5,
            IsHitTestVisible = false
        };
        _watermark.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4");

        _clear = new ShapePath
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Data = (Geometry)FindResource("IconClose")
        };
        // PCL 的清除按钮是 Theme=Black：Fill = MyColor(160,0,0,0)
        _clear.SetResourceReference(Shape.FillProperty, "ColorBrushTitleButtonClear");
        _clear.MouseLeftButtonUp += (_, _) =>
        {
            _input.Text = string.Empty;
            _input.Focus();
        };

        _input.TextChanged += (_, _) =>
        {
            bool empty = string.IsNullOrEmpty(_input.Text);
            _watermark.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            TextChanged?.Invoke(this, EventArgs.Empty);
        };

        Children.Add(back);
        Children.Add(_input);
        Children.Add(_watermark);
        Children.Add(icon);
        Children.Add(_clear);

        _watermark.Visibility = Visibility.Visible;
    }

    /// <summary>提示文字（未输入时以灰色浮显）。</summary>
    public string? HintText
    {
        get => _watermark.Text;
        set => _watermark.Text = value ?? string.Empty;
    }

    /// <summary>当前输入内容。</summary>
    public string Text
    {
        get => _input.Text;
        set => _input.Text = value ?? string.Empty;
    }

    /// <summary>内容变化事件（等价于 TextChanged）。</summary>
    public event EventHandler? TextChanged;
}

/// <summary>
/// 本地资源项（对应 PCL <c>Pages/PageInstance/MyLocalModItem.xaml</c>，逐项照抄其网格参数）。
/// <para>
/// <c>Height=44</c>；列 <c>6 / 34 / 7 / Auto / 1* / 4</c>；行 <c>1* / 17 / 18 / 1*</c>；
/// Logo 是 34×34、圆角 6、右对齐的 <c>Border.Clip</c>；标题 14px；
/// 副标题 12px <c>ColorBrushGray1</c> + <c>Opacity 0.4</c>；Info 12px <c>ColorBrushGray4</c>。
/// </para>
/// </summary>
public class PclLocalItem : Grid
{
    private readonly Border _logoBack;
    private readonly ShapePath _logo;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Border _tag;
    private readonly TextBlock _tagText;
    private readonly TextBlock _info;
    private readonly StackPanel _buttons;
    private readonly Border _back;

    private bool _checked;
    private Geometry? _logoData;

    public PclLocalItem()
    {
        // 对应 MyLocalModItem.xaml:6
        Height = 44;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Background = (SolidColorBrush)FindResource("ColorBrushSemiTransparent");
        SnapsToDevicePixels = true;
        Cursor = Cursors.Hand;

        // 列：6 / 34 / 7 / Auto / 1* / 4（MyLocalModItem.xaml:7-13）
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });   // ColumnPaddingRight

        // 行：1* / 17 / 18 / 1*（MyLocalModItem.xaml:14-20）
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 选中底色（PCL 用整项背景区分；这里加一层可切换的高亮底）
        _back = new Border
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = 0
        };
        SetColumnSpan(_back, 99);
        SetRowSpan(_back, 99);
        Children.Add(_back);

        // Logo：34×34、圆角 6、右对齐（MyLocalModItem.xaml:22-28）
        _logoBack = new Border
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, 34, 34), 6, 6)
        };
        SetColumn(_logoBack, 1);
        SetRow(_logoBack, 1);
        SetRowSpan(_logoBack, 2);

        _logo = new ShapePath
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _logoBack.Child = _logo;
        Children.Add(_logoBack);

        // 标题行：Auto(标题) / Auto(状态) / Auto(按钮) / 1*（MyLocalModItem.xaml:30-46）
        var titleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 1, 0, 0),
            IsHitTestVisible = false
        };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(titleGrid, 3);
        SetColumnSpan(titleGrid, 2);
        SetRow(titleGrid, 1);

        _title = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        titleGrid.Children.Add(_title);

        // 状态标签（对应 MyLocalModItem.xaml 的 PanTags）
        _tag = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _tagText = new TextBlock { FontSize = 11, IsHitTestVisible = false };
        _tag.Child = _tagText;
        SetColumn(_tag, 1);
        titleGrid.Children.Add(_tag);

        // 右侧图标按钮
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        SetColumn(_buttons, 2);
        titleGrid.Children.Add(_buttons);

        Children.Add(titleGrid);

        // 副标题（MyLocalModItem.xaml:39-41）
        _subtitle = new TextBlock
        {
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("ColorBrushGray1"),
            Opacity = 0.4,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SetColumn(_subtitle, 3);
        SetRow(_subtitle, 1);
        Children.Add(_subtitle);

        // Info（MyLocalModItem.xaml:53-55）
        _info = new TextBlock
        {
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("ColorBrushGray4"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        SetColumn(_info, 4);
        SetRow(_info, 2);
        Children.Add(_info);

        MouseLeftButtonUp += (_, e) =>
        {
            if (IsInsideIconButton(e.OriginalSource as DependencyObject)) return;
            Click?.Invoke(this, EventArgs.Empty);
        };
    }

    private static bool IsInsideIconButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is PclIconButton) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>标题文字。</summary>
    public string? Title
    {
        get => _title.Text;
        set => _title.Text = value ?? string.Empty;
    }

    /// <summary>副标题（PCL 的 LabSubtitle，灰色淡显）。</summary>
    public string? Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value ?? string.Empty;
            _subtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>第二行说明文字（PCL 的 LabInfo）。</summary>
    public string? Info
    {
        get => _info.Text;
        set => _info.Text = value ?? string.Empty;
    }

    /// <summary>矢量图标；不设置时不占图标列。</summary>
    public Geometry? Logo
    {
        get => _logoData;
        set
        {
            _logoData = value;
            _logo.Data = value;
            _logoBack.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>状态标签文字（如「已启用 / 已禁用」）；为空时不显示。</summary>
    public string? Status
    {
        get => _tagText.Text;
        set
        {
            _tagText.Text = value ?? string.Empty;
            _tag.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>状态标签的前景色 / 底色。</summary>
    public Brush StatusForeground { get => _tagText.Foreground; set => _tagText.Foreground = value; }

    /// <summary>状态标签的背景色。</summary>
    public Brush StatusBackground { get => _tag.Background; set => _tag.Background = value; }

    /// <summary>是否选中（整行高亮）。</summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            _back.Background = value ? (SolidColorBrush)FindResource("ColorBrush7") : Brushes.Transparent;
            _back.Opacity = value ? 1 : 0;
            _title.Foreground = value
                ? (SolidColorBrush)FindResource("ColorBrush2")
                : (SolidColorBrush)FindResource("ColorBrush1");
            if (_logoData is not null)
                _logo.Fill = (SolidColorBrush)FindResource(value ? "ColorBrush2" : "ColorBrush1");
        }
    }

    /// <summary>右侧图标按钮组。</summary>
    public IList<PclIconButton> Buttons { get; } = new List<PclIconButton>();

    /// <summary>把一组图标按钮挂到标题行右侧。</summary>
    public void AddButton(PclIconButton button)
    {
        Buttons.Add(button);
        _buttons.Children.Add(button);
    }

    /// <summary>点击事件。</summary>
    public event EventHandler? Click;
}

/// <summary>
/// 下载页资源项（对应 PCL <c>Pages/PageDownload/Resource/MyResourceItem.xaml</c>，逐项照抄其网格参数）。
/// <para>
/// <c>Height=64</c>；列 <c>7 / 50 / 8 / Auto / 1*</c>；行 <c>1* / 17 / 18 / 16 / 1.1*</c>；
/// Logo 是 50×50、圆角 6、右对齐的 <c>Border.Clip</c>；标题 14px <c>ColorBrush1</c>；
/// 原始名 12px <c>ColorBrushGray1</c> + <c>Opacity 0.4</c>；Info 12px <c>ColorBrushGray3</c>；
/// 下边栏四组「图标 11px + 文字 12px」分别是 版本(Gray3) / 下载量(Gray4) / 时间(Gray4) / 来源(Gray4)。
/// </para>
/// </summary>
public class PclResourceItem : Grid
{
    private readonly Border _logoBack;
    private readonly ShapePath _logo;
    private readonly TextBlock _title;
    private readonly TextBlock _raw;
    private readonly TextBlock _info;
    private readonly StackPanel _buttons;
    private readonly TextBlock _version;
    private readonly TextBlock _download;
    private readonly TextBlock _time;
    private readonly TextBlock _source;

    public PclResourceItem()
    {
        // 对应 MyResourceItem.xaml:5-6
        Height = 64;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Background = (SolidColorBrush)FindResource("ColorBrushSemiTransparent");
        SnapsToDevicePixels = true;
        Cursor = Cursors.Hand;

        // 列：7 / 50 / 8 / Auto / 1*（MyResourceItem.xaml:7-13）
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 行：1* / 17 / 18 / 16 / 1.1*（MyResourceItem.xaml:14-20）
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.1, GridUnitType.Star) });

        // Logo：50×50、圆角 6、右对齐（MyResourceItem.xaml:22-28）
        _logoBack = new Border
        {
            Width = 50,
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, 50, 50), 6, 6)
        };
        SetColumn(_logoBack, 1);
        SetRow(_logoBack, 1);
        SetRowSpan(_logoBack, 3);

        _logo = new ShapePath
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _logoBack.Child = _logo;
        Children.Add(_logoBack);

        // 标题行（MyResourceItem.xaml:30-34）：Auto(标题) / Auto(原始名) / 1*(按钮)
        var titleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 1, 0, 0),
            IsHitTestVisible = false
        };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(titleGrid, 3);
        SetColumnSpan(titleGrid, 2);
        SetRow(titleGrid, 1);

        _title = new TextBlock
        {
            FontSize = 14,
            Foreground = (SolidColorBrush)FindResource("ColorBrush1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.WordEllipsis,
            MaxWidth = 420,
            IsHitTestVisible = false
        };
        titleGrid.Children.Add(_title);

        _raw = new TextBlock
        {
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("ColorBrushGray1"),
            Opacity = 0.4,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.WordEllipsis,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SetColumn(_raw, 1);
        titleGrid.Children.Add(_raw);

        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        SetColumn(_buttons, 2);
        titleGrid.Children.Add(_buttons);

        Children.Add(titleGrid);

        // Info（MyResourceItem.xaml:41-48）
        _info = new TextBlock
        {
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("ColorBrushGray3"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        SetColumn(_info, 4);
        SetRow(_info, 2);
        Children.Add(_info);

        // 下边栏（MyResourceItem.xaml:50-82）：版本 / 下载量 / 时间 / 来源
        var bottom = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                   // 0 版本图标
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ItemVersion" });  // 1 版本
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });           // 2
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                   // 3 下载图标
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });                                // 4 下载量
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });             // 5
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                   // 6 时间图标
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(57), SharedSizeGroup = "ItemTime" }); // 7 时间
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });             // 8
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                                   // 9 来源图标
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ItemSource" });   // 10 来源
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });           // 11
        SetColumn(bottom, 3);
        SetColumnSpan(bottom, 2);
        SetRow(bottom, 3);

        bottom.Children.Add(MetricIcon("IconResVersion", 11, 0));
        _version = MetricText(1);
        bottom.Children.Add(_version);

        bottom.Children.Add(MetricIcon("IconDownload", 10.5, 3));
        _download = MetricText(4);
        bottom.Children.Add(_download);

        bottom.Children.Add(MetricIcon("IconResTime", 11.5, 6));
        _time = MetricText(7);
        bottom.Children.Add(_time);

        bottom.Children.Add(MetricIcon("IconResSource", 11.5, 9));
        _source = MetricText(10);
        bottom.Children.Add(_source);

        Children.Add(bottom);

        MouseLeftButtonUp += (_, e) =>
        {
            if (IsInsideIconButton(e.OriginalSource as DependencyObject)) return;
            Click?.Invoke(this, EventArgs.Empty);
        };
    }

    private static bool IsInsideIconButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is PclIconButton) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private ShapePath MetricIcon(string key, double height, int column)
    {
        var icon = new ShapePath
        {
            Stretch = Stretch.Uniform,
            Height = height,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (SolidColorBrush)FindResource("ColorBrushGray4"),
            Data = FindResource(key) as Geometry,
            IsHitTestVisible = false
        };
        SetColumn(icon, column);
        return icon;
    }

    private TextBlock MetricText(int column)
    {
        var text = new TextBlock
        {
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("ColorBrushGray4"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 2, 0),
            TextTrimming = TextTrimming.WordEllipsis,
            IsHitTestVisible = false
        };
        SetColumn(text, column);
        return text;
    }

    private readonly System.Windows.Controls.Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
        Visibility = Visibility.Collapsed
    };

    /// <summary>矢量图标；不设置时不占图标列。</summary>
    public Geometry? Logo
    {
        get => _logo.Data;
        set
        {
            _logo.Data = value;
            _logo.Visibility = value is null && _image.Visibility != Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            _logoBack.Visibility = value is null && _image.Visibility != Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>位图图标（网络图片，PCL 的 MyImage）；设置后替代矢量图标。</summary>
    public ImageSource? Image
    {
        get => _image.Source;
        set
        {
            if (_image.Parent is null)
            {
                _logoBack.Child = _image;
                Panel.SetZIndex(_image, 1);
            }
            _image.Source = value;
            _image.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            _logo.Visibility = value is null && _logo.Data is not null ? Visibility.Visible : Visibility.Collapsed;
            _logoBack.Visibility = value is null && _logo.Data is null
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>标题。</summary>
    public string? Title
    {
        get => _title.Text;
        set => _title.Text = value ?? string.Empty;
    }

    /// <summary>原始名（PCL 的 LabTitleRaw，重复条目时显示文件名）。</summary>
    public string? RawTitle
    {
        get => _raw.Text;
        set
        {
            _raw.Text = value ?? string.Empty;
            _raw.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>第二行说明（PCL 的 LabInfo）。</summary>
    public string? Info
    {
        get => _info.Text;
        set => _info.Text = value ?? string.Empty;
    }

    /// <summary>下边栏「版本」。</summary>
    public string? Version
    {
        get => _version.Text;
        set => _version.Text = value ?? string.Empty;
    }

    /// <summary>下边栏「下载量」。</summary>
    public string? DownloadCount
    {
        get => _download.Text;
        set => _download.Text = value ?? string.Empty;
    }

    /// <summary>下边栏「时间」。</summary>
    public string? Time
    {
        get => _time.Text;
        set => _time.Text = value ?? string.Empty;
    }

    /// <summary>下边栏「来源」。</summary>
    public string? Source
    {
        get => _source.Text;
        set => _source.Text = value ?? string.Empty;
    }

    /// <summary>右侧图标按钮组。</summary>
    public IList<PclIconButton> Buttons { get; } = new List<PclIconButton>();

    /// <summary>把一组图标按钮挂到标题行右侧。</summary>
    public void AddButton(PclIconButton button)
    {
        Buttons.Add(button);
        _buttons.Children.Add(button);
    }

    /// <summary>点击事件。</summary>
    public event EventHandler? Click;
}
