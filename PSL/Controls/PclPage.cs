using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PSL.Controls;

/// <summary>可播放进入 / 退出动画的页面部件。</summary>
public interface IPageAnimated
{
    /// <summary>播放进入动画。</summary>
    void TriggerShowAnimation(Action? onCompleted = null);

    /// <summary>播放退出动画。</summary>
    void TriggerHideAnimation(Action? onCompleted = null);
}

/// <summary>
/// 页面切换动画。
/// <para>
/// 数值与曲线**逐项**对齐 PCL 的 <c>MyPageLeft</c> / <c>MyPageRight</c> / <c>ModAnimation</c>：
/// </para>
/// <list type="bullet">
/// <item>同一动画组内的所有动画是**并发累加**的（<c>ModAnimation.vb:870-906</c>，
///       位移实现为 <c>TranslateTransform.X += Delta</c>，见 <c>:983-988</c>）。
///       所以「位移 +5 @200ms」与「位移 +20 @300ms」是**同时**起跑、总长取二者最大者。</item>
/// <item>缓动曲线是 PCL 自研的，不是 WPF 内置：
///       Fluent(p) = <c>1-(1-t)^p</c>；Back(p) = <c>1-(1-t)^p·cos(1.5πt)</c>，p = 3 - Power*0.5。
///       （<c>ModAnimation.vb:599-684</c>；Power: Weak=2 Middle=3 Strong=4 ExtraStrong=5，<c>:539-544</c>）</item>
/// </list>
/// <para>
/// WPF 的 Storyboard 无法让两个动画叠加同一属性，因此这里把并发的若干段**先合成一条曲线**再密集采样成关键帧。
/// </para>
/// </summary>
internal static class PclPageAnimation
{
    // ===================== PCL 缓动（ModAnimation.vb:585-684） =====================

    /// <summary>PCL 的缓动强度 AniEasePower（ModAnimation.vb:539-544）。</summary>
    private const int PowerWeak = 2;
    private const int PowerMiddle = 3;

    private static double Linear(double t) => t;
    /// <summary>AniEaseInFluent：t^p</summary>
    private static double FluentIn(double t, int power) => Math.Pow(t, power);
    /// <summary>AniEaseOutFluent：1-(1-t)^p</summary>
    private static double FluentOut(double t, int power) => 1 - Math.Pow(1 - t, power);
    /// <summary>AniEaseInBack：t^p·cos(1.5π(1-t))，p = 3 - Power*0.5</summary>
    private static double BackIn(double t, int power) => Math.Pow(t, 3 - power * 0.5) * Math.Cos(1.5 * Math.PI * (1 - t));
    /// <summary>AniEaseOutBack：1-(1-t)^p·cos(1.5πt)，p = 3 - Power*0.5</summary>
    private static double BackOut(double t, int power) => 1 - Math.Pow(1 - t, 3 - power * 0.5) * Math.Cos(1.5 * Math.PI * t);

    /// <summary>一段动画：自 <paramref name="Start"/> 起，历时 <paramref name="Duration"/>，累计位移 <paramref name="Delta"/>。</summary>
    private readonly record struct Seg(double Start, double Duration, double Delta, Func<double, double> Ease);

    /// <summary>把若干并发段累加成一条曲线在该时刻的值。</summary>
    private static double Sample(Seg[] segs, double t)
    {
        double sum = 0;
        foreach (Seg s in segs)
        {
            if (t <= s.Start) continue;
            double u = Math.Clamp((t - s.Start) / s.Duration, 0, 1);
            sum += s.Delta * s.Ease(u);
        }
        return sum;
    }

    /// <summary>采样间隔（ms）。PCL 按渲染帧推进，8ms 比屏幕帧间隔更密。</summary>
    private const double SampleStep = 8.0;

    /// <summary>
    /// 生成一条采样动画。
    /// <paramref name="axis"/> 为 null 时作用于 Opacity，否则作用于 TranslateTransform 的该轴。
    /// </summary>
    private static void AddSampled(
        Storyboard board, FrameworkElement target, string? axis, string ownerName,
        double from, Seg[] segs, double delay, double total)
    {
        var frames = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(frames, target);
        Storyboard.SetTargetProperty(frames, new PropertyPath(axis is null
            ? "(UIElement.Opacity)"
            : $"(UIElement.RenderTransform).(TranslateTransform.{axis})"));

        for (double t = 0; t <= total + 1e-9; t += SampleStep)
        {
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(
                from + Sample(segs, t),
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + t))));
        }
        // 末尾补精确终值
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(
            from + Sample(segs, total),
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + total))));

        board.Children.Add(frames);
    }

    // ===================== 控件收集（对齐 PCL 的两套规则） =====================

    /// <summary>
    /// 左栏的动画单位（<c>MyPageLeft.vb:91-106</c>）：
    /// 列表项 / 文本按钮**整项作为一个单位**且不再向内递归；
    /// ContentControl 取 Content 继续往内；Panel 遍历子项；**其余类型也单独收集**。
    /// </summary>
    private static List<FrameworkElement> CollectLeft(FrameworkElement root)
    {
        var list = new List<FrameworkElement>();
        CollectLeft(root, list);
        return list;
    }

    private static void CollectLeft(FrameworkElement element, List<FrameworkElement> list)
    {
        if (element.Visibility == Visibility.Collapsed) return;

        // 注意顺序：PclListItem 是 Grid（Panel）的子类，必须优先命中
        if (element is PclListItem or ButtonBase && element is not PclIconButton)
        {
            list.Add(element);
        }
        else if (element is ContentControl cc)
        {
            if (cc.Content is FrameworkElement fe) CollectLeft(fe, list);
        }
        else if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                if (child is FrameworkElement fe) CollectLeft(fe, list);
        }
        else
        {
            list.Add(element);
        }
    }

    /// <summary>
    /// 右栏的动画单位（<c>MyPageRight.vb:476-488</c>）：
    /// 只有 卡片 / 提示条 / 文本 / 文本按钮 才动画且不向内递归；ContentControl 取 Content；Panel 遍历子项；
    /// **其余类型不参与**。
    /// </summary>
    private static List<FrameworkElement> CollectRight(FrameworkElement root)
    {
        var list = new List<FrameworkElement>();
        CollectRight(root, list);
        return list;
    }

    private static void CollectRight(FrameworkElement element, List<FrameworkElement> list)
    {
        if (element.Visibility == Visibility.Collapsed) return;

        if (element is PclCard or PclHint or TextBlock)
        {
            list.Add(element);
            return;
        }
        if (element is PclIconButton)
        {
            list.Add(element);
            return;
        }
        if (element is ContentControl cc)
        {
            if (cc.Content is FrameworkElement fe) CollectRight(fe, list);
            return;
        }
        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                if (child is FrameworkElement fe) CollectRight(fe, list);
        }
    }

    /// <summary>查找第一个「正在显示」滚动条的竖直 ScrollBar（<c>MyPageRight.vb:491-509</c>）。</summary>
    private static ScrollBar? FindVisibleScrollBar(FrameworkElement root)
    {
        foreach (FrameworkElement viewer in Descendants(root))
        {
            if (viewer is not ScrollViewer sv) continue;
            if (sv.ComputedVerticalScrollBarVisibility != Visibility.Visible) continue;
            sv.ApplyTemplate();
            if (sv.Template?.FindName("PART_VerticalScrollBar", sv) is ScrollBar bar) return bar;
        }
        return null;
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        if (count == 0)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is FrameworkElement fe) yield return fe;
            yield break;
        }
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                yield return fe;
                foreach (FrameworkElement sub in Descendants(fe)) yield return sub;
            }
        }
    }

    // ===================== 进入动画 =====================

    /// <summary>
    /// 左栏进入（<c>MyPageLeft.vb:29-58</c>）：
    /// 每项从 (−25,0) 起，**并发**累加 +5(200ms, Fluent Middle) 与 +20(300ms, Back Weak)，
    /// 透明度 0 → 1（文本 0.6），100ms Fluent Weak；逐项延迟 +max(15-序号, 7)*2。
    /// </summary>
    internal static void ShowLeft(FrameworkElement root, Action? onCompleted)
    {
        root.Visibility = Visibility.Visible;
        List<FrameworkElement> items = CollectLeft(root);
        if (items.Count == 0) { onCompleted?.Invoke(); return; }

        var board = new Storyboard();
        var finals = new List<(FrameworkElement Item, double Opacity)>();

        double delay = 0;
        int id = 0;
        foreach (FrameworkElement item in items)
        {
            double targetOpacity = item is TextBlock ? 0.6 : 1.0;
            PrepareTranslate(item, -25, 0);

            AddSampled(board, item, "X", "PageLeft PageChange", -25, new[]
            {
                new Seg(0, 200, +5,  t => FluentOut(t, PowerMiddle)),
                new Seg(0, 300, +20, t => BackOut(t, PowerWeak))
            }, delay, 300);

            AddSampled(board, item, null, "PageLeft PageChange", 0, new[]
            {
                new Seg(0, 100, targetOpacity, t => FluentOut(t, PowerWeak))
            }, delay, 100);

            finals.Add((item, targetOpacity));
            delay += Math.Max(15 - id, 7) * 2;
            id++;
        }

        Finish(board, finals, restore: true, root, onCompleted);
    }

    /// <summary>
    /// 右栏进入（<c>MyPageRight.vb:392-428</c>）：
    /// 每项从 (0,−16) 起，**并发**累加 +5(250ms, Fluent Middle) 与 +11(350ms, Back Middle)，
    /// 透明度 0 → 1（100ms Fluent Weak）；逐项延迟 +25；最后滚动条从 X+10 归位（350ms）。
    /// </summary>
    internal static void ShowRight(FrameworkElement root, Action? onCompleted)
    {
        root.Visibility = Visibility.Visible;
        List<FrameworkElement> items = CollectRight(root);
        if (items.Count == 0) { onCompleted?.Invoke(); return; }

        var board = new Storyboard();
        var finals = new List<(FrameworkElement Item, double Opacity)>();

        double delay = 0;
        foreach (FrameworkElement item in items)
        {
            PrepareTranslate(item, 0, -16);

            AddSampled(board, item, "Y", "PageRight PageChange", -16, new[]
            {
                new Seg(0, 250, +5,  t => FluentOut(t, PowerMiddle)),
                new Seg(0, 350, +11, t => BackOut(t, PowerMiddle))
            }, delay, 350);

            AddSampled(board, item, null, "PageRight PageChange", 0, new[]
            {
                new Seg(0, 100, 1.0, t => FluentOut(t, PowerWeak))
            }, delay, 100);

            finals.Add((item, 1.0));
            delay += 25;
        }

        // 滚动条：非 TranslateTransform 时先置 (10,0)，再归位
        ScrollBar? scroll = FindVisibleScrollBar(root);
        if (scroll is not null)
        {
            if (scroll.RenderTransform is not TranslateTransform) scroll.RenderTransform = new TranslateTransform(10, 0);
            double from = ((TranslateTransform)scroll.RenderTransform).X;
            AddSampled(board, scroll, "X", "PageRight PageChange", from, new[]
            {
                new Seg(0, 350, -from, t => FluentOut(t, PowerMiddle))
            }, 0, 350);
        }

        Finish(board, finals, restore: true, root, onCompleted);
    }

    // ===================== 退出动画 =====================

    /// <summary>
    /// 左栏退出（<c>MyPageLeft.vb:72-82</c>）：
    /// 每项延迟 70/项数*序号，X 累加 −6、透明度归零，均 50ms 且**无缓动**（Linear）。
    /// </summary>
    internal static void HideLeft(FrameworkElement root, Action? onCompleted)
    {
        List<FrameworkElement> items = CollectLeft(root);
        if (items.Count == 0) { onCompleted?.Invoke(); return; }

        double step = 70.0 / items.Count;
        var board = new Storyboard();
        var finals = new List<(FrameworkElement Item, double Opacity)>();

        int id = 0;
        foreach (FrameworkElement item in items)
        {
            double delay = step * id;
            double current = item is TextBlock ? 0.6 : 1.0;
            PrepareTranslate(item, 0, 0);

            AddSampled(board, item, "X", "PageLeft PageChange", 0, new[]
            {
                new Seg(0, 50, -6, Linear)
            }, delay, 50);

            AddSampled(board, item, null, "PageLeft PageChange", current, new[]
            {
                new Seg(0, 50, -current, Linear)
            }, delay, 50);

            finals.Add((item, 0));
            id++;
        }

        Finish(board, finals, restore: false, root, onCompleted);
    }

    /// <summary>
    /// 右栏退出（<c>MyPageRight.vb:431-462</c>）：
    /// 每项延迟 +15，Y 累加 −6、透明度归零，均 70ms 且**无缓动**（Linear）；结束后整页 Collapsed。
    /// </summary>
    internal static void HideRight(FrameworkElement root, Action? onCompleted)
    {
        List<FrameworkElement> items = CollectRight(root);
        if (items.Count == 0) { onCompleted?.Invoke(); return; }

        var board = new Storyboard();
        var finals = new List<(FrameworkElement Item, double Opacity)>();

        double delay = 0;
        foreach (FrameworkElement item in items)
        {
            PrepareTranslate(item, 0, 0);

            AddSampled(board, item, "Y", "PageRight PageChange", 0, new[]
            {
                new Seg(0, 70, -6, Linear)
            }, delay, 70);

            AddSampled(board, item, null, "PageRight PageChange", 1, new[]
            {
                new Seg(0, 70, -1, Linear)
            }, delay, 70);

            finals.Add((item, 0));
            delay += 15;
        }

        Finish(board, finals, restore: false, root, onCompleted);
    }

    // ===================== 整块缩放（未指定 AnimatedControl 时） =====================

    /// <summary>左栏整块缩放进入（<c>MyPageLeft.vb:18-27</c>）：0.96 → 1（400ms Back Weak）+ 0 → 1（100ms Linear）。</summary>
    internal static void ShowScale(FrameworkElement root, Action? onCompleted)
    {
        root.Visibility = Visibility.Visible;
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = new ScaleTransform(0.96, 0.96);
        root.Opacity = 0;

        var board = new Storyboard();
        var scale = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scale, root);
        Storyboard.SetTargetProperty(scale, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        for (double t = 0; t <= 400 + 1e-9; t += SampleStep)
        {
            double v = 0.96 + 0.04 * BackOut(Math.Clamp(t / 400, 0, 1), PowerWeak);
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        }
        board.Children.Add(scale);

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scaleY, root);
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        foreach (DoubleKeyFrame f in scale.KeyFrames) scaleY.KeyFrames.Add(new LinearDoubleKeyFrame(f.Value, f.KeyTime));
        board.Children.Add(scaleY);

        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(100));
        Storyboard.SetTarget(fade, root);
        Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
        board.Children.Add(fade);

        board.Completed += (_, _) =>
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.RenderTransform = new ScaleTransform(1, 1);
            root.Opacity = 1;
            onCompleted?.Invoke();
        };
        board.Begin();
    }

    /// <summary>左栏整块缩放退出（<c>MyPageLeft.vb:63-69</c>）：1 → 0.95（110ms InFluent Weak）+ 透明度 80ms（延迟 30）。</summary>
    internal static void HideScale(FrameworkElement root, Action? onCompleted)
    {
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        if (root.RenderTransform is not ScaleTransform) root.RenderTransform = new ScaleTransform(1, 1);

        var board = new Storyboard();
        var scale = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scale, root);
        Storyboard.SetTargetProperty(scale, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        for (double t = 0; t <= 110 + 1e-9; t += SampleStep)
        {
            double v = 1 - 0.05 * FluentIn(Math.Clamp(t / 110, 0, 1), PowerWeak);
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t))));
        }
        board.Children.Add(scale);

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scaleY, root);
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        foreach (DoubleKeyFrame f in scale.KeyFrames) scaleY.KeyFrames.Add(new LinearDoubleKeyFrame(f.Value, f.KeyTime));
        board.Children.Add(scaleY);

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(80)) { BeginTime = TimeSpan.FromMilliseconds(30) };
        Storyboard.SetTarget(fade, root);
        Storyboard.SetTargetProperty(fade, new PropertyPath("(UIElement.Opacity)"));
        board.Children.Add(fade);

        board.Completed += (_, _) =>
        {
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.RenderTransform = new ScaleTransform(1, 1);
            // PCL 退出后页面保持隐藏（MyPageRight 在退出动画末尾 Collapsed；左栏同样不还原透明度）
            root.Opacity = 0;
            root.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };
        board.Begin();
    }

    // ===================== 公共 =====================

    private static void PrepareTranslate(FrameworkElement element, double x, double y)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TranslateTransform(x, y);
    }

    /// <summary>
    /// 收尾：<paramref name="restore"/> 为 true 时还原成「可见 + 无位移 + 终值透明度」；
    /// 为 false 时保持「透明 + 已位移 + 不可见」。
    /// <para>
    /// 关键：退出动画末尾必须把页面 <c>Visibility = Collapsed</c>（PCL 行为），
    /// 否则旧页面会在被替换前整块重新显形一帧 —— 即「切换页面闪一下」。
    /// </para>
    /// </summary>
    private static void Finish(
        Storyboard board, List<(FrameworkElement Item, double Opacity)> finals,
        bool restore, FrameworkElement? root, Action? onCompleted)
    {
        board.Completed += (_, _) =>
        {
            foreach ((FrameworkElement item, double opacity) in finals)
            {
                item.BeginAnimation(UIElement.OpacityProperty, null);
                item.RenderTransform = new TranslateTransform(0, 0);
                item.Opacity = restore ? opacity : 0;
            }

            if (!restore && root is not null) root.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };
        board.Begin();
    }
}

/// <summary>
/// 页面左栏（对应 PCL 的 <c>MyPageLeft</c>，<c>MyPageLeft.vb</c>）。
/// 底色 <c>ColorBrushBackgroundTransparentSidebar</c>；右缘 4px 渐变分隔与 PCL 的 <c>RectLeftShadow</c> 一致
/// （黑色 → 透明，整体 Opacity 0.04，见 <c>FormMain.xaml:145-152</c>）。
/// </summary>
public class PclPageLeft : Border, IPageAnimated
{
    /// <summary>需要逐个播放进入动画的控件名称；不设置时整块缩放进入（与 PCL 一致）。</summary>
    public string? AnimatedControl { get; set; }

    public PclPageLeft()
    {
        BorderThickness = new Thickness(0, 0, 4, 0);
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "ColorBrushBackgroundTransparentSidebar");
        BorderBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0, 0, 0), 0),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)
            },
            new Point(0, 0), new Point(1, 0))
        {
            Opacity = 0.04
        };
    }

    public void TriggerShowAnimation(Action? onCompleted = null)
    {
        FrameworkElement? target = Resolve();
        if (target is null) { onCompleted?.Invoke(); return; }

        if (string.IsNullOrEmpty(AnimatedControl)) PclPageAnimation.ShowScale(target, onCompleted);
        else PclPageAnimation.ShowLeft(target, onCompleted);
    }

    public void TriggerHideAnimation(Action? onCompleted = null)
    {
        FrameworkElement? target = Resolve();
        if (target is null) { onCompleted?.Invoke(); return; }

        if (string.IsNullOrEmpty(AnimatedControl)) PclPageAnimation.HideScale(target, onCompleted);
        else PclPageAnimation.HideLeft(target, onCompleted);
    }

    private FrameworkElement? Resolve() => string.IsNullOrEmpty(AnimatedControl)
        ? Child as FrameworkElement
        : FindByName(this, AnimatedControl);

    /// <summary>按逻辑树查找指定名称的元素（不依赖 NameScope）。</summary>
    internal static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name) return fe;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node) continue;
            FrameworkElement? found = FindByName(node, name);
            if (found is not null) return found;
        }
        return null;
    }
}

/// <summary>页面右栏（对应 PCL 的 <c>MyPageRight</c>，<c>MyPageRight.vb</c>）。</summary>
public class PclPageRight : Grid, IPageAnimated
{
    /// <summary>需要逐个播放进入动画的控件名称。</summary>
    public string? AnimatedControl { get; set; }

    private FrameworkElement? Target => string.IsNullOrEmpty(AnimatedControl)
        ? (Children.Count > 0 ? Children[0] as FrameworkElement : null)
        : PclPageLeft.FindByName(this, AnimatedControl);

    public void TriggerShowAnimation(Action? onCompleted = null)
    {
        FrameworkElement? target = Target;
        if (target is null) { onCompleted?.Invoke(); return; }
        PclPageAnimation.ShowRight(target, onCompleted);
    }

    public void TriggerHideAnimation(Action? onCompleted = null)
    {
        FrameworkElement? target = Target;
        if (target is null) { onCompleted?.Invoke(); return; }
        PclPageAnimation.HideRight(target, onCompleted);
    }
}

/// <summary>
/// 页面基类：自动查找内部所有 <see cref="PclPageLeft"/> / <see cref="PclPageRight"/>，
/// 同时播放它们的进入 / 退出动画（对应 PCL 里 FrmMain 分别驱动左右页面的做法）。
/// </summary>
public abstract class PclPage : UserControl, IPageAnimated
{
    /// <summary>是否禁用页面切换动画。</summary>
    public bool DisablePageAnimation { get; set; }

    public void TriggerShowAnimation(Action? onCompleted = null) => Run(show: true, onCompleted);

    public void TriggerHideAnimation(Action? onCompleted = null) => Run(show: false, onCompleted);

    private void Run(bool show, Action? onCompleted)
    {
        List<IPageAnimated> parts = FindParts(this);
        if (DisablePageAnimation || parts.Count == 0)
        {
            onCompleted?.Invoke();
            return;
        }

        int pending = parts.Count;
        foreach (IPageAnimated part in parts)
        {
            void Done()
            {
                if (Interlocked.Decrement(ref pending) == 0) onCompleted?.Invoke();
            }

            if (show) part.TriggerShowAnimation(Done);
            else part.TriggerHideAnimation(Done);
        }
    }

    private static List<IPageAnimated> FindParts(DependencyObject root)
    {
        var found = new List<IPageAnimated>();
        Walk(root);
        return found;

        void Walk(DependencyObject node)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is not DependencyObject d) continue;
                if (d is PclPageLeft or PclPageRight)
                {
                    found.Add((IPageAnimated)d);
                    continue;
                }
                Walk(d);
            }
        }
    }
}
