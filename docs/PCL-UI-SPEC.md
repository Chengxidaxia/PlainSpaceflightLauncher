# PCL 界面规范速查（从源码逐文件读出，附出处）

> 来源：`K:/WorkBuddyWorkspace/2026-10-12-21-52-30/_ref/PCL-main/Plain Craft Launcher 2/`
> 通读时间：2026-10-14。**本文件所有数值都来自源码，不是印象。**
> 与源码冲突时，以源码为准。

---

## 1. 色板（出处：`Application.xaml` 第 17-89 行）

### 主题蓝色阶

| 键 | 值 | 用途 |
|---|---|---|
| `ColorBrush1` / `ColorObject1` | `#343d4a` | 正文色、按钮常态描边、卡片闲置阴影色 |
| `ColorBrush2` / `ColorObject2` | `#0b5bcb` | **Highlight 按钮描边、选中项文字（高≥40）** |
| `ColorBrush3` / `ColorObject3` | `#1370f3` | 主色：选中竖条、hover 描边、聚焦边框 |
| `ColorBrush4` / `ColorObject4` | `#4890f5` | 标题栏底色、卡片 hover 阴影色、图标按钮常态 |
| `ColorBrush5` / `ColorObject5` | `#96c0f9` | 图标按钮禁用以外的浅色档 |
| `ColorBrush6` / `ColorObject6` | `#d5e6fd` | 列表项按下底色 |
| `ColorBrush7` / `ColorObject7` | `#e0eafd` | 按钮/输入框 hover 底色 |
| `ColorBrush8` / `ColorObject8` | `#eaf2fe` | White 主题图标常态 |
| `ColorBrushBg0` | `#96c0f9` | **输入框/下拉框/滑块常态描边** |
| `ColorBrushBg1` | `#bee0eafd` | 列表项 hover 底色 |

### 灰阶

`Gray1 #404040` `Gray2 #737373` `Gray3 #8c8c8c` `Gray4 #a6a6a6` `Gray5 #cccccc` `Gray6 #ebebeb` `Gray7 #f0f0f0` `Gray8 #f5f5f5`

### 功能色

- 红：`RedBack #80fbdddd`、`RedLight #ff4c4c`、`RedDark #ce2111`
- 白/透明：`HalfWhite #55ffffff`、`SemiWhite #bbffffff`、`White #ffffff`、`Transparent`、`SemiTransparent #01eaf2fe`
- 侧栏底：`ColorBrushBackgroundTransparentSidebar #F1FFFFFF`

---

## 2. 全局样式（出处：`Application.xaml`）

| 项 | 值 |
|---|---|
| `TextBlock` 默认 | `FontSize 13`、`Foreground ColorBrush1`、`TextTrimming CharacterEllipsis` |
| 字体 | `Resources/#PCL English, Microsoft YaHei UI` |
| `Label` | `Padding 0` |
| `MyScrollBar` | `Width/MinWidth 8`；Thumb 圆角 3、内缩 2 |
| `MyTextBox` | `BorderThickness 1`、`Background ColorBrushHalfWhite`、`BorderBrush ColorBrushBg0`、**`CornerRadius 默认 3`**、`FontSize 13`、hint 色 `Gray4` |
| `PasswordBox` | 圆角 3；禁用 `Gray5`/`Gray6`；hover `ColorBrush4`+`ColorBrush7`；聚焦 `ColorBrush3`+`ColorBrush7` |
| `MyComboBox` | `Foreground = ColorBrushBg0`（=边框色）、`Background HalfWhite`、圆角 3、`MaxDropDownHeight 320`；内容 `Margin 8.5,0,21.5,0` |
| `ComboBoxItem` | `Padding 6,4`，无圆角 |
| `ContextMenu` | 圆角 3、白底、`BorderBrush ColorBrush1`、阴影 `Opacity .4 Blur 4 Depth 2` |
| `MyMenuItem` | 高 26、图标 `12x15 Margin 8,0` |

---

## 3. 主窗体（出处：`FormMain.xaml`）

- 窗口：`AllowsTransparency=True`、`WindowStyle=None`、`ResizeMode=CanMinimize`、`Background={x:Null}`、默认 `850x500`、`MinWidth 810 MinHeight 470`
- `BorderForm Margin="8"` + `RectangleGeometry RadiusX/Y=6`（圆角裁剪）
- **标题栏 `PanTitle Height="48"`**，底色为只有一个 `GradientStop` 的渐变 → **等价纯色 `ColorObject4 #4890f5`**
  - 窗口按钮 `MyIconButton Theme="White"` **28x28**，关闭 `Margin 0,0,12,0` / 最小化 `Margin 0,0,44,0`，`LogoScale 0.72`
  - 图标：关闭 `F1 M2,0 L0,2 8,10 0,18 2,20 10,12 18,20 20,18 12,10 20,2 18,0 10,8 2,0Z`；最小化 `F1 M0,0 h15 v2 h-15 v-2 Z`
  - 导航容器 `StackPanel Height="27" Margin="13,0"`，内部 `MyRadioButton Margin="5,0" Padding="2,0"`
- **左栏 `PanLeft`**：`RectLeftBackground` 底色 `ColorBrushBackgroundTransparentSidebar`（宽度由代码动画），`RectLeftShadow Width="4"` 渐变 **`Opacity 0.04`**（黑→透明）
- 缩放热区：四边 8px、四角 13px

### 页面切换时序（出处：`FormMain.xaml.vb` 第 1490-1545 行）

1. 旧左栏 `TriggerHideAnimation()` + 旧右栏 `PageOnExit()`
2. **110ms 后**：换入新页面（`Child = 新页面`，`Opacity=0`），左栏背景宽度动画 180ms
3. **再 30ms 后**：`Opacity=1` + 左栏 `TriggerShowAnimation()` / 右栏 `PageOnEnter()`

---

## 4. 控件规范

### MyCard（`Controls/MyCard.vb`）

- 结构：`MyDropShadow`（Margin `-3,-3,-3,-4`、`ShadowRadius 3`、圆角 5）+ `Border`（`Color.FromArgb(245,255,255,255)`、圆角 5）
- **阴影**：闲置 `Opacity 0.07` 色 `ColorObject1`；hover `Opacity 0.4` 色 `ColorObject4`（过渡 90ms）
- 标题：`TextBlock Margin 15,12,0,0`、`FontWeight Bold`、`FontSize 13`、`Foreground ColorBrush1`；hover → `ColorBrush2`
- 折叠态高 `SwapedHeight = 40`

### MyButton（`Controls/MyButton.xaml` + `.xaml.vb`）

- 外层底 `ColorBrushSemiTransparent`；内层 `Border CornerRadius 3 BorderThickness 1 Background ColorBrushHalfWhite`
- **文字色 = 描边色**（`Foreground` 绑定 `Parent.BorderBrush`）、`FontSize 13`、居中
- 状态：
  | ColorType | 常态 BorderBrush | hover BorderBrush | hover 背景 |
  |---|---|---|---|
  | Normal | `ColorBrush1` | `ColorBrush3` | `ColorBrush7` |
  | Highlight | `ColorBrush2` | `ColorBrush3` | `ColorBrush7` |
  | Red | `ColorBrushRedDark` | `ColorBrushRedLight` | `ColorBrushRedBack` |
  - 离开背景回到 `ColorBrushHalfWhite`；禁用描边 `Gray4`
- 按下缩放 0.955→1（动画）
- 页面里的常用尺寸：`Height 35`、`MinWidth 140`、`Padding 13,0`、`Margin 0,0,20,0`

### MyListItem（`Controls/MyListItem.xaml` + `.xaml.vb`）

- 默认 `Height 42`；**PCL 各页面显式设为 `Height="36"`**，并带 `MinPaddingRight="35"`、`Type="RadioBox"`、`IsScaleAnimationEnabled="False"`
- 指向背景 `RectBack`：`Background ColorBrush7`→hover `ColorBrushBg1`→按下 `ColorBrush6`；`BorderBrush ColorBrush6`；**圆角 = (IsScaleAnimationEnabled 或 Height>40) ? 6 : 0** → 36 高时为 **0**
- 选中竖条 `RectCheck`：`Width 5`、`CornerRadius 2`、`Background ColorBrush3`、`Margin -1,6,0,6`
- **选中文字色**：`Height<40 → ColorBrush3`；`Height≥40 → ColorBrush2`
- 图标列宽：有图标 `34`（`Height<40` 时不额外 +4）；图标 `Margin (6,8,4,8)`、`Fill` 绑定 `Foreground`
- 标题 `Margin 4,0,0,0`、`FontSize 默认 14`
- 右侧按钮组 hover 才淡入，右内边距扩到 `5 + Buttons.Count*25`

### MyRadioButton（`Controls/MyRadioButton.xaml` + `.xaml.vb`）

- 尺寸：`MinHeight/MaxHeight 27`、`CornerRadius 13.5`
- 图标 `Margin 12,0,0,0`、`MaxHeight/MaxWidth 16`；文字 `Margin 8,0,12,0`
- **ColorState.White**（标题栏用）：
  - 常态：底 `ColorSemiTransparent`、图标/文字 **白色**
  - hover：底 `#32eaf2fe`
  - 按下：底 `#78eaf2fe`
  - **选中：底纯白 `#ffffff`、图标/文字 `ColorBrush3`**
- **ColorState.Highlight**（页面筛选用）：
  - 常态：底 `ColorSemiTransparent`、图标/文字 `ColorBrush3`
  - hover：底 `ColorBrush7`
  - 按下：底 `ColorBrush6`
  - **选中：底 `ColorBrush3`、图标/文字 纯白**

### MyIconButton（`Controls/MyIconButton.xaml` + `.xaml.vb`）

- 圆形（圆角 1000）、`BorderThickness 0.001`、底 `ColorBrushSemiTransparent`
- 图标 `Margin 5`、`Stretch Uniform`
- Theme.Color：常态 `ColorBrush4`（无动画分支为 `ColorBrush5`）；hover → `ColorBrush2`；**hover 不铺底色**
- Theme.White：常态图标 `ColorBrush8`；hover 底 `#32ffffff`
- 禁用 → `Gray5`

### MyHint（`Controls/MyHint.xaml` + `.xaml.vb`）

- `BorderThickness 3,0,0,0`（左 3px 竖条）、`CornerRadius 2`；文字 `Padding 12,9`、`LineHeight 16`、`TextWrapping Wrap`
- **配色（三档都是「浅底 + 深条 + 深字」，不是半透明色）**：

| Theme | 背景 | 竖条 | 文字 |
|---|---|---|---|
| Blue | `#d9ecff` | `#1172D4` | `#0F64B8` |
| Red | `#ffdddf` | `#d82929` | `#bf0b0b` |
| Yellow | `#ffebd7` | `#f57a00` | `#d86c00` |

### MyCheckBox / MyRadioBox（`Controls/MyCheckBox.xaml`、`MyRadioBox.xaml`）

- 方块：`18x18`、`CornerRadius 3`、`BorderThickness 1.1`、`BorderBrush ColorBrush1`、`Background ColorBrushHalfWhite`
- 选中：勾 `Path 12x12` 缩放出现，勾色 = 边框色；边框 → `ColorBrush2`（鼠标不在）/ `ColorBrush3`（鼠标在）
- 文字 `Margin 26,0,0,0`、`FontSize 13`、`Foreground ColorBrush1`
- 单选为 `Ellipse 18x18` + 内圆点
- **设置页里统一 `Height="22"`**

### MySearchBox（`Controls/MySearchBox.xaml`）

- 高 40、继承 MyCard（圆角 5 + 阴影）
- 放大镜 `14x14 Margin 14,0,0,0` `Fill ColorBrush1`；输入 `Padding 32,0,40,0`、`FontSize 13.5`
- 清除按钮 `24x24` 右 `Margin 0,0,10,0`，Theme.Black，`LogoScale 0.75`

### MyLoading（`Controls/MyLoading.xaml`）

- `MinWidth/MinHeight 50`；镐子动画 `Width 60 Height 47`；文字 `FontSize 16`、`Margin 0,10,0,0`
- 常用摆放：`Margin="20,20,20,17"` 居中放在 `MyCard` 里

### MySlider（`Controls/MySlider.xaml`）

- 高 16；背线 `Height 4 StrokeThickness 1 ColorBrushBg0 Opacity 0.3`；前线 `StrokeThickness 2`；圆点 `10x10`

---

## 5. 页面范式

### 页面 = 左栏 + 右栏（出处：`Pages/**`）

**左栏 `MyPageLeft`**
```xml
<local:MyPageLeft AnimatedControl="PanItem">
  <StackPanel Margin="0,12,0,0" Name="PanItem">
    <local:MyListItem Checked="True" IsScaleAnimationEnabled="False" Type="RadioBox"
                      MinPaddingRight="35" Height="36" VerticalAlignment="Top"
                      Title="xxx" Logo="<path data>" LogoScale="0.9" />
    <TextBlock Text="分组标题" Margin="13,20,5,4" Opacity="0.6" FontSize="12" />
    ...
  </StackPanel>
</local:MyPageLeft>
```
- `PageLaunchLeft` 宽 **300**；`PageSelectLeft` 的 ScrollViewer `MinWidth 250 MaxWidth 300`
- 若该页带列表滚动：外面套 `MyScrollViewer Style="{StaticResource ScrollViewerFullMargin}"`

**右栏 `MyPageRight`**
```xml
<local:MyPageRight PanScroll="PanBack">
  <local:MyScrollViewer>
    <StackPanel x:Name="PanMain" Margin="25,25,25,10">   <!-- 设置页用 25,10 -->
      <local:MyHint Theme="Blue" Margin="0,0,0,15" Text="..." />
      <local:MyCard Margin="0,0,0,15" Title="标题">
        <StackPanel Margin="25,40,25,15">…</StackPanel>
      </local:MyCard>
    </StackPanel>
  </local:MyScrollViewer>
</local:MyPageRight>
```
- 卡片外边距：普通页 `0,0,0,15`；设置类页第一个卡片 `0,15`、其余 `0,0,0,15`
- **卡片内容顶部必须留 40（设置/资源类）或 38（右栏信息卡）给标题**：实测取值有 `25,40,25,15`、`25,40,25,17`、`25,40,25,20`、`25,40,25,21`、`25,40,25,25`、`20,38,20,18`、`20,40,18,15`
- 卡片内多按钮一行：外层 `Grid Height="35"`（列 `Auto`×N，`SharedSizeGroup="Button"`），按钮 `MinWidth 140 Padding="13,0" Margin="0,0,20,0"`；管理类是 `MinWidth 110 Margin="0,0,15,0"`
- 设置类页面：标签列 `Auto` + 控件列；行高 `28`、间隔 `9`；标签 `Margin 0,0,25,0`（启动页/设置页）或 `0,0,20,0`

**空态卡片**
```xml
<local:MyCard HorizontalAlignment="Center" VerticalAlignment="Center" Margin="40">
  <StackPanel Margin="20,17">
    <TextBlock Text="标题" Margin="0,0,0,9" HorizontalAlignment="Center"
               FontSize="19" Foreground="{DynamicResource ColorBrush3}" />
    <Rectangle Height="2" HorizontalAlignment="Stretch" Fill="{DynamicResource ColorBrush3}" />
    <TextBlock Text="说明" Margin="10,15,10,5" TextWrapping="Wrap" />
    <Grid Margin="15,20,0,5" Height="35" HorizontalAlignment="Center"> …按钮… </Grid>
  </StackPanel>
</local:MyCard>
```

### 列表项（资源列表用，出处：`Pages/PageDownload/Resource/MyResourceItem.xaml`、`PageInstance/MyLocalModItem.xaml`）

- `MyResourceItem`：`Height 64`，列 `7 / 50 / 8 / Auto / 1*`；Logo `50x50` 圆角 6；标题 `FontSize 14`；下边栏 版本/下载量/时间/来源 图标+文字（`FontSize 12`、`Gray3/Gray4`）
- `MyLocalModItem`：`Height 44`，列 `6 / 34 / 7 / Auto / 1* / 4`；Logo `34x34` 圆角 6；标题 `FontSize 14`；副标题 `FontSize 12 Gray1 Opacity 0.4`；信息 `FontSize 12 Gray4`
- 列表容器放在 `MyCard` 内：`StackPanel Margin="20,48,18,22"`

### 关于页条目（出处：`Pages/PageOther/PageOtherAbout.xaml`）

```xml
<local:MyCard Margin="0,15" Title="关于">
  <Grid Margin="21,40,21,16">
    行高 45；列 40 / * / 150
    <Image 40x40 圆形裁剪 /> + <MyListItem IsHitTestVisible="False" Title="名字" Info="说明" />
    + <MyButton Grid.Column="2" Height="35" />
```

---

## 6. 切换动画（出处：`Controls/MyPageLeft.vb`、`MyPageRight.vb`）

| 位置 | 起始 | 序列 | 延迟 |
|---|---|---|---|
| 左栏逐项 | `Opacity=0`、`TranslateX(-25)` | X：-25 →(200ms 缓出) -20 →(300ms 回弹) 0；透明度 0→1 100ms（TextBlock 目标 0.6） | `Delay += Max(15-Id,7)*2` |
| 左栏整块（无 AnimatedControl） | `Scale 0.96`、`Opacity 0` | `Scale 0.96→1`(400ms 回弹) + `Opacity 0→1`(100ms) | — |
| 左栏退出 | — | `Opacity→0` 50ms + `TranslateX→-6` 50ms | `70/count*Id` |
| 右栏逐项 | `Opacity=0`、`TranslateY(-16)` | Y：-16 →(250ms 缓出) -11 →(350ms 回弹) 0；透明度 0→1 100ms | `Delay += 25` |
| 右栏退出 | — | `Opacity→0` 70ms + `TranslateY→-6` 70ms | `Delay += 15` |

缓动：`AniEaseOutFluent ≈ CubicEase/EaseOut`；`AniEaseOutBack ≈ BackEase/EaseOut Amplitude 0.3`。
同一属性多段动画串行 → 用 `DoubleAnimationUsingKeyFrames` 分段还原。

---

## 7. PCL 页面 → PSL 对应关系

| PCL 页面 | 文件 | PSL 对应 |
|---|---|---|
| 启动页 | `PageLaunch/PageLaunchLeft.xaml`(300宽) + `PageLaunchRight.xaml` | 主页 |
| 版本选择 | `PageSelectLeft.xaml` + `PageSelectRight.xaml`（含空态） | 版本管理 |
| 下载 | `PageDownload/PageDownloadLeft.xaml` + `Resource/PageResource.xaml` + 各资源子页 | 下载页 |
| 版本概览 | `PageInstance/PageInstanceLeft.xaml` + `PageInstanceOverall.xaml` | 版本管理 |
| Mod 管理 | `PageInstance/PageInstanceMod.xaml` + `MyLocalModItem.xaml` | 版本管理-资源列表 |
| 设置 | `PageSetup/PageSetupLeft.xaml` + `PageSetupUI.xaml` / `PageSetupSystem.xaml` | 设置页 |
| 关于 | `PageOther/PageOtherAbout.xaml` | 设置-关于 |
| 帮助 | `PageOther/PageOtherLeft.xaml`(首项) + `PageOtherHelp.xaml`（含 MySearchBox） | 帮助页 |

---

## 8. 启动页左栏细节（PSL 主页直接照搬，出处：`PageLaunch/PageLaunchLeft.xaml`）

```xml
<local:MyPageLeft Width="300">
  <Grid Name="PanInput">            <!-- 行: Auto / * / Auto / Auto / 20；列: Auto / 20 / 1* / Auto / 10 -->
    <MyButton Grid.Row="3" Grid.Column="2" Height="35" Margin="0,10,10,0" Text="版本选择" />
    <MyButton Grid.Row="3" Grid.Column="3" Height="35" Margin="0,10,10,0" TextPadding="36" Text="版本设置" />
    <!-- 顶部类型胶囊：底 ColorBrushSemiTransparent、圆角 13、高 27 -->
    <Grid Margin="20,24,20,-4">
      <Border Background="{StaticResource ColorBrushSemiTransparent}" CornerRadius="13" MinHeight="27" MaxHeight="27">
        <Path Margin="12,0,0,0" Fill="{DynamicResource ColorBrush3}" MaxHeight="16" MaxWidth="16" />
        <TextBlock FontSize="14" Foreground="{DynamicResource ColorBrush3}" Margin="15,0,12,0" />
    <!-- 启动按钮（Row 2，不是最底部） -->
    <MyButton Height="54" Margin="20,0" Text="启动游戏" ColorType="Highlight" Padding="30,0,30,15" />
    <!-- 按钮下方的版本提示小字 -->
    <TextBlock FontSize="11" Foreground="{DynamicResource ColorBrushGray3}" Margin="35,0,35,10"
               VerticalAlignment="Bottom" HorizontalAlignment="Center" />
```
