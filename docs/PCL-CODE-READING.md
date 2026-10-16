# PCL 源码通读记录（逐文件核对表）

> 本文件记录**实际翻开的每一份 PCL 源码**及其中的取值。
> 每条都给出 `文件:行号`，可供复核。凡未标注出处的数值一律视为「推断」。
>
> 参考源码根：`_ref/PCL-main/Plain Craft Launcher 2/`
> 通读日期：2026-10-16
> 通读范围：`Application.xaml(.vb)`、`FormMain.xaml(.vb)`、`Controls/`（18 个 XAML + 关键 .vb）、
> `Pages/`（PageLaunch / PageSelect / PageSetup / PageOther / PageDownload / PageInstance 的 Left/Right）、
> `Modules/Base/ModAnimation.vb`、`Modules/Base/ModBase.vb`、`Modules/ModSecret.vb`

---

## 0. 结论速览：三处此前的重大误解

| # | 此前的认识 | 源码实际 | 出处 |
|---|---|---|---|
| 1 | 色板取 `Application.xaml` 的硬编码值 | `Application.xaml` 只是**设计期兜底**；运行时由 `ThemeRefresh()` 用 `MyColor.FromHSL2` 重新生成整条色阶，**两者每一项都不相同** | `ModSecret.vb:67-108`、`ModBase.vb:365-389` |
| 2 | 页面动画「先滑到中间点、再滑到位」= 两段**接力** | 同一动画组内所有 `AniData` **并发推进**，位移是**累加**（`TranslateTransform.X += Delta`）；同 Delay 的两段位移同时作用，总时长取二者最大 | `ModAnimation.vb:870-906`、`983-988` |
| 3 | 回弹缓动 ≈ WPF `BackEase` | PCL 自定义 `AniEaseOutBack` 是 **`1-(1-t)^p·cos(1.5πt)`** 形式，与 `BackEase` 曲线不同 | `ModAnimation.vb:674-684` |

---

## 1. 色板（权威来源是运行时 HSL，不是 XAML）

**算法**：`ModBase.vb:340-389`

```
FromHSL(sH,sS,sL)   标准 HSL→RGB，但 hue 分段阈值用 0.16667 / 0.66667
FromHSL2(sH,sS,sL)  先按色相把 L 做分段映射（cent 表），再调 FromHSL
  cent = { +0.1, -0.06, -0.3,      // 0°,30°,60°
           -0.19, -0.15, -0.24,    // 90°,120°,150°
           -0.32, -0.09, +0.18,    // 180°,210°,240°
           +0.05, -0.12, -0.02,    // 270°,300°,330°
           +0.1, -0.06 }           // 360°,390°(=0°,30°)
  center  = 50 - ((1 - frac + i)·cent[i] + frac·cent[i+1]) · sS
  sL'     = (sL<center ? sL/center : 1 + (sL-center)/(100-center)) · 50
```

**默认主题参数**：`ModSecret.vb:64` → `ColorHue=210, ColorSat=85, ColorLightAdjust=0`

**生成规则**：`ModSecret.vb:72-93`

| 资源键 | 公式 | **实际值** | Application.xaml 兜底值（❌ 不能用） |
|---|---|---|---|
| ColorBrush1 | `HSL2(210, 85*0.2, 25)` | `#333E48` | `#343d4a` |
| ColorBrush2 | `HSL2(210, 85, 45)` | `#0F64B8` | `#0b5bcb` |
| ColorBrush3 | `HSL2(210, 85, 55)` | `#127AE1` | `#1370f3` |
| ColorBrush4 | `HSL2(210, 85, 65)` | `#3C96EF` | `#4890f5` |
| ColorBrush5 | `HSL2(210, 85, 80)` | `#90C3F6` | `#96c0f9` |
| ColorBrush6 | `HSL2(210, 85, 91)` | `#CDE4FB` | `#d5e6fd` |
| ColorBrush7 | `HSL2(210, 85, 95)` | `#E3F0FD` | `#e0eafd` |
| ColorBrush8 | `HSL2(210, 85, 97)` | `#EEF6FE` | `#eaf2fe` |
| ColorBrushBg0 | `Color4*0.4 + Color5*0.4 + ColorGray4*0.2` | `#73ABE3` | `#96c0f9` |
| ColorBrushBg1 | `MyColor(190, Color7)` | `#BEE3F0FD` | `#bee0eafd` |
| ColorSemiTransparent | `MyColor(1, Color8)` | `#01EEF6FE` | — |

复现脚本：`_verify/pcl_palette2.py`（严格按上述公式实现，输出与上表一致）。

**交叉验证**：`MyHint` 蓝字写死 `#0F64B8`（`MyHint.xaml.vb:42`），与 `Color2` 的算值完全相同 → 说明该算法正确。

**灰阶**（`ModSecret.vb:53-60`，不随主题变）：
Gray1 `#404040` / Gray2 `#737373` / Gray3 `#8C8C8C` / Gray4 `#A6A6A6` /
Gray5 `#CCCCCC` / Gray6 `#EBEBEB` / Gray7 `#F0F0F0` / Gray8 `#F5F5F5`

**窗口底色**：`ThemeRefreshMain`（`ModSecret.vb:109-130`）
- 标题栏：`HSL2(210,85,48) → (…,54) → (…,48)`，**offset 0 / 0.5 / 1**，水平渐变
  → `#106AC4` / `#1277DD` / `#106AC4`
- 主背景（`UiBackgroundColorful` 默认开）：`HSL2(195,68,91) → (210,68,91) → (225,68,91)`，
  offset `-0.1 / 0.4 / 1.1`，起点 `(0.9,0)` 终点 `(0.1,1)`
- 关掉彩色背景时：纯 `MyColor(245,245,245)` = `#F5F5F5`

---

## 2. 主窗体骨架（`FormMain.xaml`，逐行）

| 项 | 值 | 行 |
|---|---|---|
| 窗口 | `AllowsTransparency=True` `WindowStyle=None` `ResizeMode=CanMinimize` `Background=null` `Topmost=True` | 7-9 |
| 默认尺寸 | `Width=850 Height=500`；`MinWidth=810 MinHeight=470` | 7 |
| 外层 | `Grid x:Name="PanBack" Margin="10"`，初始 `RotateTransform -4°` + `TranslateTransform Y=60` | 14-20 |
| 缩放热区 | 8 个 Rectangle：上下左右 8px、四角 13px；渐变填充 | 21-88 |
| 窗体本体 | `Border x:Name="BorderForm" Margin="8"` + `Clip` 矩形圆角 **6** | 89-92 |
| 内容网格 | `Grid x:Name="PanForm"`：行 0 标题栏(Auto)、行 1 内容 | 93-97 |
| 标题栏 | `Grid x:Name="PanTitle" Height="48"` | 100 |
| 关闭按钮 | 28×28，`Margin="0,0,12,0"`，`Theme=White` `LogoScale=0.72` | 107-108 |
| 最小化按钮 | 28×28，`Margin="0,0,44,0"` | 109-110 |
| Logo（图形） | `ShapeTitleLogo` Path，`Width=39` `Stroke=White` `StrokeThickness=2.2` `Margin="19,15.5,0,15.5"` | 117 |
| 导航条 | `PanTitleSelect` 水平 StackPanel，`Height=27` `Margin="13,0"` | 120 |
| 导航项 | `MyRadioButton`：`Margin="5,0"` `Padding="2,0"`，各自带 `Logo` | 121-130 |
| 内页标题 | `PanTitleInner`（返回箭头 28×28 + 15px 标题），`Margin="-16,0,0,0"` | 133-137 |
| 左栏容器 | `PanLeft`：`RectLeftBackground`(Fill=#F1FFFFFF) + `RectLeftShadow`(宽 4，黑→透明渐变，`Opacity=0.04`) | 139-153 |
| 主区容器 | `PanMain`：`PanMainRight`(列 1) + `PanMainLeft`(列 0) | 154-167 |
| 提示层 | `PanHint` 底部左，`Margin="0,0,0,20"`，`Grid.RowSpan=2` | 168 |
| 附加按钮 | 右下 `StackPanel Margin="15"`：返回顶部 / 任务管理 / 投降 / 关闭MC / 音乐 | 169-179 |

**窗体行为**（`FormMain.xaml.vb`）
- 拖动：`PanTitle.MouseLeftButtonDown → DragMove()`（623-626）
- 尺寸同步（633-644）：`RectForm.Rect = (0,0,BorderForm.ActualWidth, BorderForm.ActualHeight)`；
  `PanForm.Width = BorderForm.ActualWidth + 0.001`；`PanMain.Height = max(0, PanForm.Height - PanTitle.ActualHeight)`
- **开场动画**（378-389）：
  ```
  AniStart({
    AaCode(..., 50),
    AaOpacity(Me, UiLauncherTransparent/1000 + 0.4, 250, 100),
    AaDouble(TransformPos.Y += i, -TransformPos.Y, 600, 100, AniEaseOutBack(Weak)),   // 60 → 0
    AaDouble(TransformRotate.Angle += i, -TransformRotate.Angle, 500, 100, AniEaseOutBack(Weak)), // -4 → 0
    AaCode(PanBack.RenderTransform = Nothing, ...)
  }, "Form Show")
  ```

---

## 3. 页面切换时序（`FormMain.xaml.vb:1489-1592`）—— 精确到毫秒

```vb
PageChangeAnim(TargetLeft, TargetRight):
  AniStop("FrmMain LeftChange"); AniStop("PageLeft PageChange")
  清空新旧页面的 ContentPresenter 关联
  PanMainLeft.Child.TriggerHideAnimation()      ' 旧左栏退场
  PanMainRight.Child.PageOnExit()               ' 旧右栏退场
  AniStart({
      AaCode( PanMainLeft.Child = 新左栏; 新左栏.Opacity = 0; PanMainLeft_Resize(ActualWidth), 110),
      AaCode( 新左栏.Opacity = 1; 新左栏.TriggerShowAnimation(), 30, After:=True)
  }, "FrmMain PageChangeLeft")
  AniStart({
      AaCode( 旧右栏.PageOnForceExit(); PanMainRight.Child = 新右栏; 新右栏.Opacity = 0, 110),
      AaCode( 新右栏.Opacity = 1; 新右栏.PageOnEnter(), 30, After:=True)
  }, "FrmMain PageChangeRight")
```

**左栏白底宽度动画**（`PanMainLeft_Resize`，1568-1592）：
```
AaWidth(RectLeftBackground, NewWidth - 当前宽, 180, 0, AniEaseOutFluent(ExtraStrong))
AaOpacity(RectLeftShadow, 1 - 当前不透明度, 180)
AaCode(PanMainLeft.IsHitTestVisible = True, 150)
```
→ 差异 > 0.1 才做动画；否则直接赋值。

**标题栏层级切换**（1427-1433 / 1550-1555）：
- 进子页：`PanTitleMain` 淡出 + `X +12`，150ms；`PanTitleInner` 淡入 150ms（延迟 200）+ `X` 350ms `AniEaseOutBack`
- 退子页：`PanTitleInner` 淡出 + `X -18`，150ms；`PanTitleMain` 淡入 150ms（延迟 200）+ `X` 350ms `AniEaseOutBack(Weak)`

---

## 4. 动画引擎（`Modules/Base/ModAnimation.vb`）

**执行模型**（关键）
- `AniStart(列表, 名称)` 把整份列表放进**一个动画组**；组内每个 `AniData` **各自独立计时**，互不等待 → **并发**。
- 每帧：`Anim.TimeFinished += DeltaTick`；`TimeFinished` 初值为 `-Delay`（负值即延迟）。
- 推进量：`Delta = Lerp(0, Value, Ease.GetDelta(t_now, t_prev))`，`GetDelta = ease(t1) - ease(t0)` → 增量式累加。
- 位移实现（983-988）：
  ```
  TranslateX: 若 RenderTransform 非 TranslateTransform 则新建 (0,0)；然后 .X += Delta
  ```
- `IsAfter=True` 的项要等前面所有非 After 项做完才开始（873-903）。
- 全局速度倍率 `AniSpeed`（13-18），默认设置下为 1.0。

**缓动强度**（539-544）：`Weak=2  Middle=3  Strong=4  ExtraStrong=5`

**缓动公式**（585-712）

| 类 | 公式 | 备注 |
|---|---|---|
| `AniEaseLinear` | `t` | |
| `AniEaseInFluent(p)` | `t^p` | |
| `AniEaseOutFluent(p)` | `1-(1-t)^p` | p=3 时**恰好等于** `CubicEase/EaseOut` |
| `AniEaseInBack(p)` | `t^p · cos(1.5π(1-t))` | `p = 3 - Power*0.5` |
| `AniEaseOutBack(p)` | `1-(1-t)^p · cos(1.5πt)` | **不是** WPF `BackEase` |
| `AniEaseInout(in,out,分界)` | 分段 | 默认分界 0.5 |

**左栏进出**（`MyPageLeft.vb:16-83`）
```
进入（AnimatedControl 已设）:
  每项: Opacity=0; RenderTransform=(−25,0)
        AaOpacity(项, 0.6 若为 TextBlock 否则 1, 100, Delay, AniEaseOutFluent(Weak))
        AaTranslateX(项, +5,  200, Delay, AniEaseOutFluent)        ' 与下一行并发
        AaTranslateX(项, +20, 300, Delay, AniEaseOutBack(Weak))
        Delay += max(15 - Id, 7) * 2 ; Id++
  未设 AnimatedControl: 整块 Scale 0.96→1（400ms AniEaseOutBack(2)）+ Opacity 0→1（100ms）
退出:
  每项 Delay = 70/项数 * Id ; AaOpacity(项, -当前值, 50, Delay) + AaTranslateX(项, -6, 50, Delay)
  未设 AnimatedControl: Scale 1→0.95（110ms AniEaseInFluent(Weak)）+ Opacity -当前值（80ms，延迟 30）
```

**右栏进出**（`MyPageRight.vb:392-462`）
```
进入: 每项 Opacity=0; RenderTransform=(0,-16)
      AaOpacity(项, 1, 100, Delay, AniEaseOutFluent(Weak))
      AaTranslateY(项, +5,  250, Delay, AniEaseOutFluent)      ' 与下一行并发
      AaTranslateY(项, +11, 350, Delay, AniEaseOutBack)
      Delay += 25
      另外：若找到第一个可见滚动条，AaTranslateX(Scroll, -当前X, 350, 0, AniEaseOutFluent)
退出: 每项 AaOpacity(项, -1, 70, Delay) + AaTranslateY(项, -6, 70, Delay); Delay += 15
      末尾把页面 Visibility = Collapsed
参与动画的控件类型（`_GetAllAnimControls`, 476-488）：
      MyCard / MyHint / MyExtraTextButton / TextBlock / MyTextButton 为动画单位，
      且不再向内递归；ContentControl 取 Content；Panel 遍历子项
```

**左栏参与动画的控件类型**（`MyPageLeft.vb:86-106`）：
`MyTextButton`、`MyListItem` 直接收集；ContentControl 取 Content；Panel 遍历子项；**其余类型也直接收集**。

---

## 5. 控件真值

### 5.1 MyCard（`MyCard.vb`）
| 项 | 值 | 行 |
|---|---|---|
| 结构 | `Grid` > `MainChrome`(MyDropShadow) + `MainBorder` + `MainGrid` | 65-74 |
| 阴影 | `Margin=(-3,-3,-3,-4)`（含 1px）`ShadowRadius=3` `CornerRadius=5`，闲置 `Opacity=0.07` `Color=ColorObject1` | 67-68, 175 |
| 底 | `Background=#F5FFFFFF`（A=245）`CornerRadius=5` | 70 |
| 标题 | `Margin=(15,12,0,0)` `Bold` `FontSize=13` `Foreground=ColorBrush1` | 81-82 |
| 指向 | 标题/箭头 → `ColorBrush2`（90ms）；阴影 → `ColorObject4`，`Opacity=0.4` | 178-188 |
| 高度动画 | ≤800px 用 150ms `AniEaseOutFluent(ExtraStrong)`；否则分「匀速段+减速段」 | 216-262 |
| 折叠态高 | `SwapedHeight = 40` | 325 |

### 5.2 MyHint（`MyHint.xaml` + `MyHint.xaml.vb`）
- 结构：`Border` `BorderThickness=3,0,0,0` `CornerRadius=2`；`TextBlock LineHeight=16 Padding=12,9`
- 三套配色（`UpdateUI`, 37-55）：
  | 主题 | 底 | 左条 | 文字 |
  |---|---|---|---|
  | Blue | `#D9ECFF` | `#1172D4` | `#0F64B8` |
  | Red（默认） | `#FFDDDF` | `#D82929` | `#BF0B0B` |
  | Yellow | `#FFEBD7` | `#F57A00` | `#D86C00` |
- **无整圈背景色以外的装饰**；`HasBorder=True` 时改成 `3,1,1,1`

### 5.3 MyListItem（`MyListItem.xaml` + `.vb`）
| 项 | 值 | 出处 |
|---|---|---|
| 默认高 | XAML `Height="42"`；各页显式覆盖（设置/帮助/下载=36，版本选择=40，添加类=34） | `MyListItem.xaml:5` |
| 列 | `Check / LeftPad / Logo / * / RightPad` | 同上 10-15 |
| 行 | `1* / Auto / Auto / 1*` | 同上 16-21 |
| 悬停底 `RectBack` | 圆角 `If(IsScaleAnimationEnabled OrElse Height>40, 6, 0)`；`BorderThickness=1`；`Background=ColorBrush7`；`BorderBrush=ColorBrush6`；初始 `Opacity=0` `Scale(0.8,0.8)` | `.vb:19-33` |
| 悬停时 | `RectBack.Background=ColorBrushBg1`（按下=`ColorBrush6`），`Opacity→1`，`Scale→1`；`Me.Scale` 按下 0.98 / 悬停 1 | 578-603 |
| 动画时长 | 悬停/按下 **120ms**，离开 **180ms** | 553-565 |
| 副文本 `LabInfo` | `FontSize=12` `Margin=(4,0,0,0)` `Opacity=0.6` **未设 Foreground** → 继承全局 TextBlock 隐式样式 = **ColorBrush1** | 56-78 |
| 图标列宽 | `If(Logo=="", 0, 34) + If(Height<40, 0, 4)` → 34 或 38 | 266 |
| 图标边距 | 矢量：`If(Height<40,6,8), 8, If(Height<40,4,6), 8` | 336 |
| 勾选列宽 | 无/Clickable：`If(Height<40,4,2)`；RadioBox/CheckBox：`6` | 303, 330 |
| 选中竖条 `RectCheck` | `Width=5` `CornerRadius=2` `Background=ColorBrush3` `Margin=(-1,6,0,6)` | 315-322 |
| 选中前景 | `If(Height<40, ColorBrush3, ColorBrush2)` | 448, 470 |
| 标题边距 | `Margin=(4,0,0,If(Height<40,0,2))` | 339 |
| 右侧按钮 | 单个或 StackPanel；尺寸默认 25×25；`Margin=(0,0,5,0)`；指向时淡入，右内边距 → `max(MinPaddingRight, 5+按钮数*25)` | 117-163, 575-576 |

### 5.4 MyIconButton（`MyIconButton.xaml` + `.vb`）
- 结构：外层 `Border CornerRadius=1000` > 内层 `PanBack`（同圆角，负责缩放）> `Path Margin=5`
- 主题 `Themes`：`Color / White / Black / Red / Custom`；默认 `Foreground=#808080`
- **Color 主题**：无动画时 `ColorBrush5`；有动画时目标 `ColorBrush4`（常态）/ `ColorBrush2`（指向）
  - 时长：进入 **120ms**，离开 **150ms**
- **White**：常态 `ColorBrush8`；指向底色 `MyColor(50,255,255,255)`
- **Black**：`MyColor(160,0,0,0)`（指向 230）
- 按下缩放：`0.8`（400ms `AniEaseOutFluent(Strong)`）；
  抬起 `1.05`（250ms `AniEaseOutBack(Weak)`）+ `-0.05`（250ms `AniEaseOutFluent(Strong)`）；离开回 `1`（250ms）

### 5.5 MyButton（`MyButton.xaml` + `.vb`）
- 三层：`PanBack`(ColorBrushSemiTransparent) > `PanFore`(圆角 3 / 描边 1 / 底 ColorBrushHalfWhite) > `LabText`(13)
- 边框色：Normal `ColorBrush1`↔`ColorBrush3`；Highlight `ColorBrush2`↔`ColorBrush3`；Red `ColorBrushRedDark`↔`ColorBrushRedLight`
- 悬停底：`ColorBrush7`（Red 时 `ColorBrushRedBack`）；离开回 `ColorBrushHalfWhite`
- 时长：进 100ms / 出 200ms
- 按下缩放：`0.955`（80ms `ExtraStrong`）→ 回弹 `-0.01`（700ms `Middle`）；
  抬起 300ms；离开 800ms

### 5.6 MyRadioButton（`MyRadioButton.xaml` + `.vb`）
- 结构：`Border CornerRadius=13.5` `Min/MaxHeight=27` `Background=ColorBrushSemiTransparent`
  > 水平 StackPanel：`Path Margin=(12,0,0,0)` `MaxWidth/Height=16` Fill=White + `TextBlock Margin=(8,0,12,0)` Foreground=White
- 两套配色：
  - **White**（标题栏导航）：常态 白字白图标 + 透明底；指向 白 + `MyColor(50,Color8)`；按下 `MyColor(120,Color8)`；选中 **白底** + `ColorBrush3` 字/图标
  - **Highlight**：常态 `ColorBrush3` + 透明底；指向 `ColorBrush7` + `ColorBrush3`；选中 `ColorBrush3` 底 + 白字
- 时长：指向 90ms / 移出 150ms / 勾选 120ms

### 5.7 MyCheckBox（`MyCheckBox.xaml` + `.vb`）
- 外框 18×18 `CornerRadius=3` `BorderThickness=1.1` `Margin=(1,0,0,0)` `Background=ColorBrushHalfWhite`
- 勾 12×12 `Margin=(4,0,0,0)`，`ScaleTransform` 从 0 弹出
- 文字 `Margin=(26,0,0,0)` `FontSize=13` `Foreground=ColorBrush1`
- 勾选动画：外框 12→18→(回弹)、勾 150ms*2 `AniEaseOutBack(Weak)`；描边色 → 选中 `ColorBrush2`（指向 `ColorBrush3`）/ 未选 `ColorBrush1`
- 按下：外框放到 16.5（1000ms `Strong`）；离开回 18（400ms）

### 5.8 MySearchBox（`MySearchBox.xaml`）
- 继承 `MyCard`（即带卡片阴影/圆角），`Height=40`
- 输入框：`Padding=(32,0,40,0)` `FontSize=13.5` `MaxLength=50` `HasBackground=False` `BorderThickness=0`
- 放大镜：`Width/Height=14` `Margin=(14,0,0,0)` `Fill=ColorBrush1`（左对齐）
- 清除：`24×24` `Margin=(0,0,10,0)` `LogoScale=0.75` `Theme=Black`（右对齐）

### 5.9 MyScrollBar / MyScrollViewer
- 滚动条：`Width=8 MinWidth=8`；Thumb `CornerRadius=3` `Margin=2`，外层底 `ColorBrushSemiTransparent`
- 状态：不可见 `Opacity=0`；拖动 `1` + ColorBrush4（50ms）；悬停 `0.9` + ColorBrush3（130ms）；常态 `0.5` + ColorBrush4（180ms）
- **平滑滚动**：滚轮 `AaDouble(..., -Delta*DeltaMult, 300, 0, AniEaseOutFluent(6))`（`MyScrollViewer.vb:22-29`）

### 5.10 MyExtraButton（`MyExtraButton.xaml`）
- 尺寸 `Width=40`；`Border CornerRadius=1000`；`PanColor` 底 `ColorBrush3` + `DropShadowEffect(0,10,0.2)`；`Path Margin=12` `Fill=ColorBrush8`

---

## 6. 各分类页面骨架（逐个 XAML 核对）

### 启动页 `PageLaunchLeft.xaml`
- 根：`MyPageLeft Width="300"`（**唯一显式写宽度的左栏**）
- 5 行 × 5 列 Grid；`PanInput` 带 `ScaleTransform`（启动中会缩放隐藏）
- `BtnVersion`：`MyButton Height=35 Margin="0,10,10,0"` 文本「版本选择」
- `BtnMore`：同尺寸，`TextPadding=36`，文本「版本设置」，默认折叠
- `PanLogin`：`Grid Margin="20,0"` 居中
- `PanTypeOne`：圆角 13 胶囊（高 27），`Path` 16×16 `Scale=1.05` + 文本 14 色 `ColorBrush3`
- `PanType`：`Margin="20,22,20,0"`，含「正版 / 离线」两个 `MyRadioButton ColorType=Highlight`
- **启动按钮**：`BtnLaunch Height=54 Margin="20,0"` `ColorType=Highlight` `Padding="30,0,30,15"`
- 版本名 `LabVersion`：`Margin="35,0,35,10"` `FontSize=11` 色 `ColorBrushGray3`，缩放跟随启动按钮
- 启动中视图 `PanLaunching`：加载环 50 高 + 标题 20 + 名称 13.5 + 进度条 4 高 + 信息表 12.5 + 取消按钮 35

### 启动页 `PageLaunchRight.xaml`
- `MyPageRight PanScroll="PanBack"` > `MyScrollViewer` > `StackPanel PanMain Margin="25,25,25,10"`
- `MyHint Theme=Blue CanClose=True Margin="0,0,0,15"`
- 自定义区 `PanCustom`
- `MyCard Title="启动日志" Margin="0,0,0,15"`，内容 `TextBlock Margin="20,38,20,18"`

### 版本选择 `PageSelectLeft.xaml` / `.vb`
- `AnimatedControl="PanList"`；`AdornerDecorator` > Grid(3 行) >
  `MyScrollViewer DeltaMult=0.7 MaxWidth=300 MinWidth=250 Style=ScrollViewerFullMargin` > `StackPanel PanList`
- 列表构建（`.vb:12-136`）：
  - 分组标题：`TextBlock { Margin=(13,18,5,4), Opacity=0.6, FontSize=12 }`
  - 文件夹项：`MyListItem { IsScaleAnimationEnabled=False, Type=RadioBox, MinPaddingRight=30, Title=名称, Info=路径, Height=40 }`，
    右侧一个 `MyIconButton { LogoScale=1.1 }` 打开右键菜单
  - 「新建 .minecraft 文件夹」「添加已有文件夹」「导入整合包」：`Type=Clickable` `Height=34`
  - 末尾 `FrameworkElement { Height=10, IsHitTestVisible=False }`

### 版本选择 `PageSelectRight.xaml` / `.vb` —— 右栏是**版本列表**，不是"版本详情"
- `Grid` > `PanAllBack` > `MyScrollViewer PanBack` > `StackPanel PanMain Margin="25,25,25,10"`
- 空态 `MyCard PanEmpty UseAnimation=False Margin="40"` 居中：
  `StackPanel Margin="20,17"` → 标题 `FontSize=19 Foreground=ColorBrush3` + `Rectangle Height=2 Fill=ColorBrush3`
  + 描述 `Margin="10,15,10,5"` + `MyButton Height=35 MinWidth=140 Padding="13,0" ColorType=Highlight`
- 加载态 `MyCard PanLoad Margin="40"` + `MyLoading Margin="20,20,20,17"`
- **内容构建**（`.vb:25-115` 的 `McInstanceListUI`）：`PanMain.Children.Clear()` 后按 `McInstanceCardType`
  逐组建卡——`MyCard{ Title = 组名 & " (" & 数量 & ")", Margin=(0,0,0,15), SwapType=0 }`（`:75-76`），
  卡片内容是 `StackPanel{ Margin=(20, MyCard.SwapedHeight, 18, 0) }`（`:77`），
  里面每个版本来自 `McInstance.ToListItem`（`ModMinecraft.vb:902-928`）：
  `MyListItem{ Info, Height=42, Type=Clickable, Logo=版本图标, Title=版本名, SnapsToDevicePixels=True }`。
  只有一个卡片时强制展开（`:90-92`）。列表为空才切到 `PanEmpty`（`:95-110`）。
- **版本项右侧的图标按钮**（`.vb:116-160` 的 `McInstanceListContent`）：`{收藏, 删除, 设置}` 三个
  `MyIconButton{ LogoScale=1.1 }`；「设置」是 `PageInstanceLeft.Instance = Instance` +
  `FrmMain.PageChange(PageType.InstanceSetup, 0)`。
- **点整行**（`.vb:167-177` 的 `Item_Click`）：`McInstanceSelected = Instance` 然后 **`FrmMain.PageBack()`**
  —— 即选中后自动退回上一页。

### 设置页 `PageSetupLeft.xaml`
- `AnimatedControl="PanItem"` > `StackPanel Margin="0,12,0,0"`
- 4 项 `MyListItem`：`IsScaleAnimationEnabled=False Height=36 MinPaddingRight=35 Type=RadioBox`
  标题「启动 / 联机 / 个性化 / 其他」，各带一个「重置本页设置」`MyIconButton LogoScale=0.9`

### 帮助页 `PageOtherLeft.xaml`
- `AnimatedControl="PanItem"` > `StackPanel Margin="0,12,0,0"`
- 5 项：帮助 / 关于与鸣谢 / 百宝箱 / 反馈 / 新功能投票；`Height=36 MinPaddingRight=35 Type=RadioBox`

### 下载页 `PageDownloadLeft.xaml`
- `AnimatedControl="PanItem"` > `MyScrollViewer Style=ScrollViewerFullMargin` > `StackPanel Margin="0,12,0,0"`
- 「原版游戏」后插入分组标题 `TextBlock { Text="社区资源", Margin=(13,20,5,4), Opacity=0.6, FontSize=12 }`
- 其余：Mod / 整合包 / 数据包 / 资源包 / 光影包；均 `Height=36 MinPaddingRight=35`，右侧「刷新」按钮 `LogoScale=0.85`

### 版本实例 `PageInstanceLeft.xaml`
- 概览 / 设置 / Mod 管理 / Mod 管理 / 导出；`Height=36 MinPaddingRight=35 Type=RadioBox`

---

## 7. 与 PSL 现状的差异清单（待修）

| 位置 | PSL 现状 | 源码真值 | 处置 |
|---|---|---|---|
| `PclPage.cs` 左栏进入 | 两段接力 `-25→-20(200ms)→0(500ms)` | **并发**：`+5 @200ms` 与 `+20 @300ms` 同时起，总 **300ms** | 改为并发 |
| `PclPage.cs` 右栏进入 | 两段接力 `-16→-11(250ms)→0(600ms)` | **并发**：`+5 @250ms` 与 `+11 @350ms` 同时起，总 **350ms** | 改为并发 |
| `PclPage.cs` 进入透明度 | TextBlock 也到 1 | 左栏 TextBlock 目标 **0.6** | 修正 |
| `PclPage.cs` 缓动 | `BackEase Amplitude=0.3` | `1-(1-t)^p·cos(1.5πt)`，p=2(Weak)/1.5(Middle) | 自写 EasingFunction |
| `PclPage.cs` 左栏退出 | 固定 70ms 总长 | 每项延迟 `70/项数*Id`，单项 50ms | 修正 |
| `PclControls.cs` `PclListItem.Info` | Foreground=`ColorBrushGray2` | **未设**，继承 ColorBrush1 + `Opacity=0.6` | 修正 |
| `PclControls.cs` `PclListItem` 高度 | 固定 36 | 各页 36/40/34，且多处依 `Height<40` 分支 | 允许外部设置并自适应 |
| `PclControls.cs` `PclListItem` 圆角 | 固定 0 | `If(启用缩放 OrElse Height>40, 6, 0)` | 修正 |
| `PclControls.cs` `PclListItem` 图标列 | 固定 34 / 固定 26px 尺寸 | `34 + If(Height<40,0,4)`；Path 靠 `Stretch=Uniform` + Margin | 修正 |
| `PclControls.cs` `PclListItem` 选中色 | 固定 ColorBrush3 | `Height<40 → ColorBrush3`，否则 `ColorBrush2` | 修正 |
| `PclControls.cs` `PclListItem` 动画 | 无 | 悬停 120ms / 离开 180ms，带 0.8→1 缩放 | 补 |
| `PclControls.cs` 搜索框图标 | Fill=`ColorBrushGray3` | `ColorBrush1`；输入框 `Padding=32,0,40,0`；清除按钮 24×24 `Margin=0,0,10,0` `Theme=Black` | 修正 |
| `PclIconButton` | 仅颜色切换 | 另有按下 0.8 / 抬起 1.05 缩放（400/250ms） | 补 |
| `PclPageLeft` | 用 `BorderThickness` 模拟分隔 | PCL 是独立 `RectLeftShadow`（4px、`Opacity=0.04`）+ 白底 `RectLeftBackground` 宽度动画 180ms | 已有近似，保留 |
| 导航栏 | 含「版本管理」项 | PCL 导航只有 5 项且**不含**版本选择/版本设置（它们是副页面） | 已去掉，改为标题栏双层 |

---

## 8. 页面层级：主页面 vs 副页面（2026-10-16 补充）

> 起因：用户指出「主页左下角两个按钮不应该是版本选择和版本设置吗，你做的版本选择为什么要单独移到版本管理页」。
> 回去读源码，确认 **PCL 的导航栏里根本没有「版本管理」这东西**——「版本选择」「版本设置」都是**副页面**，
> 只能从主页左栏按钮进入。此前 PSL 自创了一个可点击的「版本管理」导航项，属自创，已删除。

### 8.1 `PageType` 枚举（`FormMain.xaml.vb:1164-1200`）

| 值 | 名称 | 注释原文 | 是否在导航栏 |
|---|---|---|---|
| 0 | `Launch` | 启动 | ✅ 第 1 项 |
| 1 | `Download` | 下载 | ✅ 第 2 项 |
| 2 | `Link` | 联机 | ✅ 第 3 项（PSL 不用） |
| 3 | `Setup` | 设置 | ✅ 第 4 项 |
| 4 | `Other` | 更多 | ✅ 第 5 项（PSL 用「帮助」顶替） |
| 5 | `InstanceSelect` | **版本选择。这是一个副页面。** | ❌ |
| 6 | `TaskManager` | **任务管理。这是一个副页面。** | ❌ |
| 7 | `InstanceSetup` | **版本设置。这是一个副页面。** | ❌ |

判定「是不是副页面」的唯一依据是 `PageNameGet()`（`FormMain.xaml.vb:1235-1250`）：

```
InstanceSelect → "版本选择"
TaskManager    → "任务管理"
InstanceSetup  → "版本设置 - " & PageInstanceLeft.Instance.Name
其他           → ""      ← 返回空串即主页面
```

### 8.2 标题栏是**双层**结构（`FormMain.xaml:100-138`）

```
PanTitle (H=48, 渐变底)
├── BtnTitleClose / BtnTitleMin          ← 窗口按钮
├── PanTitleMain   （主层，H 全宽，HorizontalAlignment=Left）
│   ├── 3 列：1* / Auto / 1*
│   ├── Col0：ShapeTitleLogo（PCL 标志字形）/ LabTitleLogo / ImageTitleLogo  ← logo 在这一层里！
│   └── Col1：PanTitleSelect（StackPanel H=27 Margin=13,0）
│       └── BtnTitleSelect0..4  = 启动 / 下载 / 联机 / 设置 / 更多
└── PanTitleInner  （副层，Visibility=Collapsed, Opacity=0, Margin="-16,0,0,0"）
    ├── BtnTitleInner  MyIconButton Theme=White H/W=28 Margin="12,0,0,0" LogoScale=0.87
    │                   Logo = 左箭头（`M1097 584 250 584 562 896C591 925 …`，PSL 里是 `IconBack`）
    └── LabTitleInner  TextBlock Margin="47,1,60,0" FontSize=15 Foreground=ColorBrush8
```

> ⚠️ **踩到的坑**：logo 必须放在 `PanTitleMain` **里面**。PCL 切到副页面时，整个主层（含 logo）一起淡出，
> 所以不会和副层的「返回 + 页面名」重叠；PSL 一开始把 "PSL" 文字标识放在外层，结果副页面时两段文字叠在一起。

### 8.3 标题栏切换动画（进入 `:1427-1433` / 退出 `:1550-1556`）

进入副页面：

| 目标 | 属性 | 时长 | 延迟 | 缓动 | 终值 |
|---|---|---|---|---|---|
| `PanTitleMain` | Opacity | 150ms | 0 | 无（Linear） | 0 |
| `PanTitleMain` | `Margin.Left` | 150ms | 0 | `AniEaseInFluent(Weak)` p=2 | 12 |
| `PanTitleInner` | Opacity | 150ms | 200ms | 无 | 1 |
| `PanTitleInner` | `Margin.Left` | 350ms | 200ms | `AniEaseOutBack` p=3 | 0（从 −16 起） |
| — | `AaCode` | — | — | — | 主层 `Visibility=Collapsed` |

退出副页面：

| 目标 | 属性 | 时长 | 延迟 | 缓动 | 终值 |
|---|---|---|---|---|---|
| `PanTitleInner` | Opacity | 150ms | 0 | 无 | 0 |
| `PanTitleInner` | `Margin.Left` | 150ms | 0 | `AniEaseInFluent` p=3 | −18 |
| `PanTitleMain` | Opacity | 150ms | 200ms | 无 | 1 |
| `PanTitleMain` | `Margin.Left` | 350ms | 200ms | `AniEaseOutBack(Weak)` p=2 | 0（从 12 起） |

副页面 → 另一个副页面（`:1407-1411`）：**只换名字** —— `LabTitleInner` 淡出 130ms → 改文本 → 淡入 150ms（延迟 30ms）。

返回按钮（`:1381-1387`）：`PageBack()` → 有 `PageStack` 就回 `PageStack(0)`，否则回 `Launch`。

### 8.4 主页左栏底部两个按钮（`PageLaunch/PageLaunchLeft.xaml:9-28`）

外层 `Grid x:Name="PanInput"` 的列定义是 `Auto / 20 / 1* / Auto / 10`，行定义是 `Auto / * / Auto / Auto / 20`。

```
Row0 Auto  PanTypeOne（白胶囊：图标 + 版本类型）
Row1 *     PanLogin（登录区，垂直居中）
Row2 Auto  BtnLaunch「启动游戏」 H=54 Margin="20,0" Padding="30,0,30,15" ColorType=Highlight
           + LabVersion（小字，Margin="35,0,35,10" FontSize=11 Foreground=ColorBrushGray3）
Row3 Auto  BtnVersion「版本选择」 Grid.Column=2(1*) H=35 Margin="0,10,10,0"
           BtnMore   「版本设置」 Grid.Column=3(Auto) H=35 Margin="0,10,10,0" TextPadding="36"
Row4 = 20
```

- 因为 Col1 固定 20、Col4 固定 10，这行的**实际留白是「左 20 / 右 10」**（Grid 自身没有 Margin）——
  和我最初写的 `Margin="20,0"` 双倍左缩进不同。
- `BtnMore` 的 `TextPadding="36"`：`MyButton.xaml.vb:28-35` 里 `TextPadding` 直接赋给 `LabText.Padding`，
  即文字左侧留 36px（源码里并没有给 BtnMore 设 `Logo`，这 36px 就是个纯缩进）。

### 8.5 BtnLaunch 状态机（`PageLaunchLeft.xaml.vb:634-692`）

| 状态 | 条件 | BtnLaunch | LabVersion | BtnVersion | BtnMore |
|---|---|---|---|---|---|
| 0 加载中 | 列表 loader 未完成 | `正在加载` / disabled | `正在加载中，请稍候` | disabled | Collapsed |
| 1 无版本·下载已隐藏 | 未选中版本 + 下载页被隐藏 | `启动游戏` / disabled | `未找到可用的游戏版本` | enabled | Collapsed |
| 2 无版本 | 未选中版本 | `下载游戏` / enabled | `未找到可用的游戏版本` | enabled | Collapsed |
| 3 有版本 | 已选中版本 | `启动游戏` / enabled | **版本名** | enabled | **Visible** |

补充：`:688-691` 之后还有一层「功能隐藏」——`BtnVersion.Visibility` 由设置项 `UiHiddenFunctionSelect` 决定，
且仅在 `CurrentState = 3` 时把 `BtnMore.Visibility` 同步成与 `BtnVersion` 一致。

### 8.6 两个副页面的骨架

- **版本选择** = `PageSelectLeft.xaml`（`AdornerDecorator > Grid(Auto/1*/5) > MyScrollViewer MinWidth=250 MaxWidth=300 > StackPanel x:Name="PanList"`）
  + `PageSelectRight.xaml`（`ScrollViewer > StackPanel Margin="25,25,25,10"` 与居中空态卡 `PanEmpty` 同层）。
  左栏内容由 `PageSelectLeft.xaml.vb:12-136` 的 `McFolderListUI()` 动态生成：
  标题「文件夹列表」→ 每项 `MyListItem{RadioBox, H=40, MinPaddingRight=30, Title, Info}` + 齿轮 `MyIconButton`
  → 标题「添加或导入」→ 新建/添加/导入整合包（`Clickable, H=34`）→ 10px 间隔。
  >> PCL 的齿轮按钮弹的是「重命名 / 打开 / 刷新 / 删除」右键菜单，PSL 暂简化为直接打开版本目录。
- **版本设置** = `PageInstanceLeft.xaml`（`StackPanel Margin="0,12,0,0"`，每项
  `MyListItem{IsScaleAnimationEnabled=False, Type=RadioBox, MinPaddingRight=35, H=36, VerticalAlignment=Top}`）
  + 各分类右栏。**注意这里没有给左栏固定宽度**——宽度由列表项自然撑开，比主启动页那 300 窄得多。

---

## 9. 版本选择页的左右栏分工（2026-10-16 二次核对，**此前 PSL 实现做反了**）

用户指出「版本选择里面应该是左边选大版本文件夹，右边显示小版本」。回源码逐行核对，**用户说得对**。

### 9.1 源码事实

| 事实 | 出处 |
|---|---|
| 左栏 `PanList` 由 **`McFolderListUI()`** 生成 → 内容是**游戏文件夹列表** | `PageSelectLeft.xaml.vb:12-136` |
| 右栏 `PanMain` 由 **`McInstanceListUI()`** 生成 → 内容是**该文件夹下的版本列表** | `PageSelectRight.xaml.vb:25-115` |
| 左栏项 = `MyListItem{ IsScaleAnimationEnabled=False, Type=RadioBox, MinPaddingRight=30, Title=Folder.Name, Info=Folder.Location, Height=40, ContextMenu=…, Tag=Folder }` | `PageSelectLeft.xaml.vb:75` |
| 左栏项右侧 = 1 个 `MyIconButton{ Logo=Logo.IconButtonSetup, LogoScale=1.1 }`，点击打开那个 `ContextMenu` | `PageSelectLeft.xaml.vb:77-82` |
| 点文件夹项 → `McFolderSelected = Location` 并只刷新右栏版本列表 | `PageSelectLeft.xaml.vb:391-403` (`Folder_Change`) |
| 右栏版本项 = `MyListItem{ Info, Height=42, Type=Clickable, Logo, Title }` | `ModMinecraft.vb:906`（`ToListItem`） |
| 点版本项 → `McInstanceSelected = Instance` + `FrmMain.PageBack()`（**自动返回**） | `PageSelectRight.xaml.vb:167-177` |
| 右栏卡片标题格式 = `组名 & " (" & 数量 & ")"`（收藏夹组例外，不带数量） | `PageSelectRight.xaml.vb:75` |
| 卡片内容 StackPanel 上边距 = `MyCard.SwapedHeight`（卡片标题高度） | `PageSelectRight.xaml.vb:77` |
| PCL 的「文件夹」= 一个 `.minecraft` 根；它的 `versions\` 下每个子目录是一个版本 | `PageSelectRight.xaml.vb:5`（`ExtraPath:="versions\"`） |
| 「添加已有文件夹」只把路径登记进设置 `LaunchFolders`（格式 `名称>路径`，`\|` 分隔），**不动文件** | `PageSelectLeft.xaml.vb:168-245` |
| 文件夹列表为空时才在右栏显示 `PanEmpty`「无可用版本」 | `PageSelectRight.xaml.vb:95-110` |

### 9.2 PSL 此前的错误实现

- 左栏列的是**版本本身**（`GameLibrary.Versions`），右栏是「当前选中的版本」详情卡 + 一堆操作按钮
  —— 这套「详情卡 + 按钮」是**我自创的**，PCL 的 `PageSelectRight` 里根本没有。

### 9.3 本轮改成

- 新增 `PSL/Core/GameFolder.cs`：`GameFolder{ Path, Name, IsLibrary }` + 配置项 `FolderEntry{ Name, Path }`。
  游戏库根目录 `PslConfig.SfsDirectory` 本身即一个文件夹（对应 PCL 的 Vanilla），
  「添加大版本文件夹」加进来的是额外文件夹（对应 PCL 的 Custom）。
- `PslConfig` 新增 `ExtraFolders` / `LastFolder`；`VersionNames` / `LastVersion` 的 key 改成**完整路径**
  （读取时按文件夹名回退，兼容旧配置）。
- `GameLibrary` 新增 `Folders` / `SelectedFolder` / `VersionsOf()` / `FolderOf()` /
  `AddFolder()` / `RemoveFolder()` / `RenameFolder()` / `Find()` / `PathEquals()`。
- `PageSelect` 重写：左栏文件夹列表（RadioBox 40 + 齿轮菜单），右栏「版本 (N)」卡片 + 版本项 `Height=42`
  （点击即选中并 `PageBack()`，右侧「移除 / 版本设置」两个图标按钮）。
- 新增 `PSL/Controls/InputDialog.xaml(.cs)`（对应 PCL 的 `MyMsgBoxInput`），供「重命名文件夹」用。
- `MainWindow` 新增公开的 `PageBack()`。

### 9.4 已知差异（PSL 有意简化，**非源码行为**）

- PCL 的齿轮弹的是原生 `ContextMenu`；PSL 用同风格的 `PickDialog` 选项框表达同样四个操作。
- PCL 右栏按版本类型分多张卡片（常规 / Forge / Fabric / 收藏夹 / 错误 / 隐藏 / 不常用 / 愚人节），
  PSL 只有一个「版本 (N)」卡片（SFS 没有这些分类）。
- PCL 的版本项有「收藏」按钮，PSL 没有收藏功能，故只保留「移除 / 版本设置」。

---

## 10. 下载页 / 版本设置页 / 友站页对齐（2026-10-16 第三轮）

用户要求：① 顶栏加「友站」（内置浏览器）② 各种包的显示改成 PCL 的方式 ③ 同步 PCL 右栏
④ 下载页同步 PCL ⑤ 版本设置的「概览 + 设置」同步 PCL，并加「开启汉化版功能」。

### 10.1 下载页（源码：`Pages/PageDownload/PageDownloadLeft.xaml` + `Resource/PageResource.xaml` + `Resource/MyResourceItem.xaml`）

| 项 | PCL 真值 | 出处 |
|---|---|---|
| 左栏根 | `MyScrollViewer{ PanningMode=VerticalOnly, Style=ScrollViewerFullMargin } > StackPanel Margin="0,12,0,0"` | PageDownloadLeft.xaml:5-6 |
| 左栏项 | `MyListItem{ Checked, IsScaleAnimationEnabled=False, Type=RadioBox, MinPaddingRight=35, Height=36, VerticalAlignment=Top, Title, Logo }` | :8、:17、:25、:33、:41、:49 |
| 左栏项右侧 | 一个「刷新」`MyIconButton{ LogoScale=0.85 }` | :12、:21、:29、:37、:45、:53 |
| 分组标题 | 第一项之后插入 `TextBlock{ Text="社区资源", Margin=(13,20,5,4), Opacity=0.6, FontSize=12 }` | :16 |
| 右栏根 | `MyScrollViewer > StackPanel Margin="25"` | PageResource.xaml:8-9 |
| 搜索卡 | `MyCard Title="搜索XXX" Margin="0,0,0,15"` + `StackPanel Margin="25,40,25,15"` | :10-11 |
| 条件网格 | 列 `Auto / 1.3* / 1.2* / 50 / Auto / 1*`，行高 28 间距 9；名称 MyTextBox、来源/版本/类型 MyComboBox | :12-59 |
| 按钮行 | `Grid Height="35"`，三列 `SharedSizeGroup="Button"`：搜索(Highlight) / 重置条件 / 安装已有整合包 | :60-70 |
| 列表卡 | `MyCard CardProjects UseAnimation=False`（无标题）+ `StackPanel PanProjects Margin="12"` | :75-77 |
| 分页卡 | `MyCard CardPages HorizontalAlignment="Center"` + `StackPanel Orientation="Horizontal" Height="23" Margin="10,7"`：首页 / 上一页 / 页码 / 下一页 | :78-89 |
| 加载态 | `MyCard PanLoad Margin="40,50"` + `MyLoading ShowProgress="True"` | :91-93 |
| 资源项 | `MyResourceItem`：`Height=64`，列 `7 / 50 / 8 / Auto / 1*`，行 `1* / 17 / 18 / 16 / 1.1*` | MyResourceItem.xaml:5-20 |
| 资源项 Logo | 50×50、`RectangleGeometry Rect="0,0,50,50" RadiusX=RadiusY=6`、右对齐 | :22-28 |
| 资源项标题 | `LabTitle` 14px `ColorBrush1`；`LabTitleRaw` 12px `ColorBrushGray1` + `Opacity 0.4` | :32-33 |
| 资源项 Info | `LabInfo` 12px `ColorBrushGray3`，带 ToolTip | :41-48 |
| 资源项下边栏 | 四组「图标 + 文字」：版本(PathVersion 11px Gray3) / 下载量(PathDownload 10.5px Gray4) / 时间(PathTime 11.5px Gray4) / 来源(PathSource 11.5px Gray4) | :50-82 |

### 10.2 版本设置页（源码：`Pages/PageInstance/PageInstanceOverall.xaml`、`PageInstanceSetup.xaml`、`PageInstanceMod.xaml`、`MyLocalModItem.xaml`）

| 项 | PCL 真值 | 出处 |
|---|---|---|
| 概览根 | `MyScrollViewer > StackPanel PanMain Margin="25,10"` | PageInstanceOverall.xaml:8-9 |
| 概览版本头 | `MyCard Margin="0,15" Title=""` + `Grid Margin="10,7"` | :10-12 |
| 快捷方式 | `MyCard Title="快捷方式" Margin="0,0,0,15"` + `WrapPanel Margin="25,30,25,20"`，按钮 `MinWidth=140 Padding=13,0 Margin=0,13,20,0 Height=35` | :64-71 |
| 高级管理 | `MyCard Title="高级管理" Margin="0,0,0,15"` + `StackPanel Margin="25,40,25,15"` + `Grid Height="35"` 三按钮 | :72-85 |
| 设置页根 | `MyScrollViewer > StackPanel PanMain Margin="25,25,25,25"` | PageInstanceSetup.xaml:8-9 |
| 设置卡片 | `MyCard Title="启动选项" Margin="0,0,0,15"` + `StackPanel Margin="25,40,25,21"` | :12-13 |
| 设置行网格 | 列 `Auto(SharedSizeGroup="Name") / 1*`，行 `28 / 9 / 28 / 9 …`，左标签 `TextBlock Margin="0,0,25,0"`，右控件 MyComboBox / MyTextBox / MyRadioBox | :14-51 |
| Mod 页根 | `MyScrollViewer > StackPanel PanMain Margin="25,10,25,10"` | PageInstanceMod.xaml:8-10 |
| 搜索框 | `MySearchBox Margin="0,15"` | :11 |
| 操作卡 | `MyCard PanManage`（无标题）+ `Grid Margin="15,15,0,15" Height="35"`，四按钮 `MinWidth=110 Padding=13,0 Margin=0,0,15,0` | :12-26 |
| 列表卡 | `MyCard PanListBack Title=" " MinHeight="55"`；筛选 `StackPanel Margin="15,13,0,0" Height="28"`（全部/已启用/已禁用/可更新 `MyRadioButton`）；列表 `StackPanel Margin="20,48,18,22"` | :27-35 |
| 空态 | `MyCard PanEmpty` + `StackPanel Margin="20,17"`：19px ColorBrush3 标题 + 2px 横线 + 描述 + 按钮行 | :38-54 |
| 本地资源项 | `MyLocalModItem`：`Height=44`，列 `6 / 34 / 7 / Auto / 1* / 4`，行 `1* / 17 / 18 / 1*` | MyLocalModItem.xaml:5-20 |
| 本地项 Logo | 34×34、`RectangleGeometry Rect="0,0,34,34" RadiusX=RadiusY=6`、右对齐 | :22-28 |
| 本地项文字 | `LabTitle` 14px；`LabSubtitle` 12px `ColorBrushGray1` `Opacity 0.4`；`LabInfo` 12px `ColorBrushGray4` | :38-41、:53-55 |

### 10.3 顶栏「友站」

- 对应 PCL `FormMain.xaml:120-131` 的 `BtnTitleSelect2`（联机）位置，是**主页面**不是副页面。
  图标直接取 `BtnTitleSelect2` 的 Logo（FormMain.xaml:123）。
- 内置浏览器用 WebView2（`Microsoft.Web.WebView2` 1.0.2957.106），用户数据目录
  `PSL/WebView2`；运行时缺失时给出提示并引导用系统浏览器打开。
- 左栏站点列表用 `MyListItem{ RadioBox, Height=40, MinPaddingRight=30 }`（同版本选择页文件夹列表的参数）。

### 10.4 汉化版功能（PSL 自创，**非 PCL 行为**）

目录约定来自现成的汉化版 `K:/SFS1.6`：

- 翻译器配置：`<版本>\AutoTranslator\Config.ini`
- 字体：`<版本>\Spaceflight Simulator_Data\Custom Fonts\<字体名>`（无扩展名）
- 语言包：`<版本>\Spaceflight Simulator_Data\Custom Translations\<名称>.txt`
- ini 里两类路径基准不同：`[Behaviour]` 的字体路径相对**版本根目录**，
  `[Files]` 的各路径相对 **AutoTranslator 目录**（带 `..\` 前缀）——照原样沿用。
- 「应用字体」写 `OverrideFont` + `OverrideFontTextMeshPro`；
  「应用语言包」写 `[Files] OutputFile`（并顺带修 `Directory`）。
- 写 ini 用「只改指定键、其余行原样保留」的行式编辑器，保留 BOM / CRLF / 键顺序。

### 10.5 已知差异（有意简化）

- 下载页右栏只有一个「全部资源」列表卡；PCL 会按资源类型分成多张卡（Forge / Fabric / OptiFine …）。
- 分页是 PSL 自己加的（PCL 是一次性拉全量 + `MyCard.StackInstall` 虚拟化）。
- 搜索卡里的「版本 / 加载器 / 类型」下拉按 SFS 站点实际字段裁剪成「类型 / 来源」。

---

## 11. 下载页左栏失效修复 + 数据源候选（2026-10-16 第四轮）

用户反馈：① 下载页点左栏四项没反应 ② 左栏没给游戏版本下载留位置 ③ 不要「全部」④ 除 sfszhmod 外把 sfscnmod.7813.cc.cd 也做候选源。

### 11.1 左栏点击失效的根因

`ApplyFilter()` 里先写了

```csharp
ItemAll.Checked = false;
ItemMod.Checked = false;   // ← 先把所有选中态清掉
...
if (ItemMod.Checked)       // ← 再读，永远为 false
```

两个错误叠加：**先清后读**，且 `PclListItem` 本身不做单选组管理（不像 WPF `RadioButton`），
点击时不会自动置 `Checked`。所以无论点哪一项，最后都落到 `else ItemAll.Checked = true`，
看起来就是「点了没反应」。

**修法**：用 `_tab` 字段记录点的是哪一项（在 `Tab_Click(sender)` 里按 `item.Name` 判定），
`ApplyFilter()` 只负责**按 `_tab` 反写**左栏勾选态，不再读取它们。

### 11.2 左栏改为「游戏本体 + 社区资源」

照 PCL `PageDownloadLeft.xaml` 的第一项就是「原版游戏」（`:8`）。PSL 保留这个位置：

| 左栏项 | 说明 |
|---|---|
| 游戏本体 | WIP 占位卡（打开 Steam 商店页 / 去「版本选择」添加），对应 PCL 的 ItemInstall |
| 社区资源（分组标题 `Margin=(13,20,5,4)`） | |
| DLL 模组 / 涂装包 / 零件包 / 蓝图 | 按 SFS 站点 `installType` 实际字段（dll / textures / parts）加蓝图 |

原来的「全部」按用户要求删除。

### 11.3 数据源候选

`Core/ModSource.cs` 改成多站点：

```csharp
public static readonly ModSourceSite[] Sites =
{
    new("SFS 汉化模组站", "https://sfszhmod.pages.dev", "sfszhmod"),
    new("SFS 中文模组站", "https://sfscnmod.7813.cc.cd", "sfscnmod")
};
```

- 两个站的 `data/data.json` / `data/blueprints.json` **结构完全一致**（已实测 curl 比对 schema）。
- 缓存按站点分文件：`<短名>-mods.json` / `<短名>-blueprints.json`，切换不互相覆盖
  （默认站点的缓存文件名与原来一致，向后兼容）。
- 配置项 `PslConfig.ModSourceIndex`，搜索卡里加「数据源」下拉，切换即重拉。
- 第二个站带宽很小（data.json 71KB 要约 60–100s），`HttpClient.Timeout` 从 45s 提到 90s。

### 11.4 调试参数

新增 `--tab=game|mod|texture|parts|blueprint`，与既有 `--page=` / `--nav=` 同一用途
（无人工点击的环境下截图验证左栏分类切换）。
