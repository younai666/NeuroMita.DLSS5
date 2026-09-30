# 实现原理

本文记录**怎么做到的**、以及**为什么必须这么做**。所有"实测"字样的结论都在本项目的开发过程中
通过游戏内日志/截图验证过；复现这些结论最容易的方式是打开插件的 `Automation.SelfTest`。

---

## 0. 整体分层

```
NeuroMita.exe（Unity 6000.3.12f1 / IL2CPP / BiRP / 64 位）
│
├─ dxgi.dll ← ReShade 6.8.0.2155（add-on 构建）本地加载，注入 D3D11 呈现
│    ├─ lumenite_Kernel.fx       运动向量提供者（ReShade 效果）
│    ├─ DLSS5_Feed.fx            采集 帧+深度+运动向量，构造 DLSS 契约
│    ├─ dlss5-feed.addon64       DLSS5-Feeder：D3D11 采集 → 私有 D3D12 设备跑 NGX → 结果拷回
│    └─ renodx-dlss5.addon64     RenoDX 神经渲染消费端（读 ReShade.ini [RenoDX.DLSS5]）
│
└─ winhttp.dll / doorstop_config.ini ← BepInEx 6.0.0-be.788（IL2CPP / CoreCLR）
     └─ BepInEx\plugins\NeuroMita.DLSS5.dll ← 本插件
          · 往游戏设置菜单注入 L1 入口行 + L2 参数页
          · 把 UI 的读写接到 dlss5-feed.cfg 与 ReShade.ini
          · 喂帧看门狗 / 标签自愈 / 字体同步
```

本插件**不**碰渲染：它只做"设置界面 + 配置写入 + 状态观测"。

---

## 1. 把自定义设置项塞进游戏的设置系统

游戏的设置是数据驱动的：

| 类型 | 作用 |
|---|---|
| `MenuSettingId` | 设置项 id（枚举，游戏自带 0–41，中间有空洞） |
| `MenuSettingPreset` | 一项设置的元数据：id / 域 / 值类型 / prefsKey / 默认值 / min / max / step / 后缀 |
| `MenuSettingRegistry` | 静态注册表，私有静态成员 `_map`：`Dictionary<MenuSettingId, MenuSettingPreset>` |
| `MenuSettings` | 静态读写入口：`GetBool/GetFloat/GetInt`、`SetBool/SetFloat/SetInt`、`Changed` 事件 |
| `MenuSettingsDispatcher` | 值变化后按"域"分发给 `IMenuSettingsDomainHandler` |
| `MenuSliderSettingButton` / `MenuToggleSettingButton` | 行控件，`ConfigureSetting(MenuSettingId)` 绑定 |

做法：

1. 通过反射取 `MenuSettingRegistry._map`（私有静态属性），
   用 `Dictionary.set_Item` 注入自定义 id 的 `MenuSettingPreset`。
2. `MenuSettingPreset` 的构造函数（12 参数）是用元数据 dump 工具逐个试出来的：
   `(MenuSettingId id, MenuSettingDomain domain, MenuSettingValueType valueType, string prefsKey,
   bool defaultBool, float defaultFloat, int defaultInt, string defaultString,
   float min, float max, float step, string valueSuffix)`。
3. 本项目使用的自定义 id（50–63 区间，避开游戏已用的值）：

   | id | 键 | 行类型 |
   |---|---|---|
   | 50 | `mode`（总开关） | 开关 |
   | 51 | `work_resolution` | 滑条 |
   | 52 | `work_sharpness` | 滑条 |
   | 54 | `preset` | 滑条 |
   | 56 | `NRLookMode` | 滑条 |
   | 57 | `NRIntensity` | 滑条 |
   | 58 | `NRLocalTone` | 滑条 |
   | 59 | `NRLocalStructure` | 滑条 |
   | 60 | `NRSkinStructure` | 滑条 |
   | 61 | `NRAutoMask` | 开关 |
   | 63 | `NRPasses` | 滑条 |

> 注意：`MenuSettingPreset` 的 `ValueType` 决定控件怎么格式化数值（Int 走整数、Float 走小数 + `Suffix`），
> 但这个值**只影响显示**——真正的存储在下面第 3 节被我们整个接管。

---

## 2. 页面与行的构造（L1 / L2）

### 2.1 页面结构

| 对象 | 名字 | 说明 |
|---|---|---|
| L1 入口行 | `Button DLSS5` | 图形页（`Location Options Graphics`）里的一行，克隆自该页自带的返回行（`MenuGoToPanelButton`） |
| L2 页面 | `Location Options DLSS5` | 克隆自"睡眠与疲劳"页 `Location Settings SleepFatigue`（标题 + 滑条行 + 开关行 + 返回行） |
| L2 标题 | `Location Options DLSS5Title` | 文案 `DLSS5 设置` |
| L2 参数行 | `Button DLSS5 <键名>` | 每行一个参数 |
| L2 返回行 | `Button DLSS5 back` | `MenuGoToPanelButton`，目标 = 图形页 |

`MenuGoToPanelButton.ConfigureTarget(MenuPanel target, bool clearStack)` 是游戏自己的跳转机制，
按目标页面所在页的 `clearStack` 取值照抄即可（实测 `False`）。

### 2.2 行是**固定坐标**，必须用游戏的排版助手

这些设置页的行**不是**由 `VerticalLayoutGroup` 排的，每行有自己的 `anchoredPosition`。
所以克隆出来的新行会**直接叠在模板行上**；单独 `SetSiblingIndex` 完全没用（实测：两行文字互相压住）。

正确做法是调用游戏自己的私有静态方法：

```csharp
InterfaceFastMenu.CopyRowPositionAndMakeRoom(RectTransform source, RectTransform clone)
```

它的行为（实测反推）：把 `clone` 放到 `source` 的**正下方**，并把 `source` 下面的行整体下移一格。
因此构造顺序是**自顶向下**依次调用：第一行取模板首行的坐标，第二行以第一行为 `source`，依此类推。

### 2.3 其它构造细节（每一条都对应一次实测故障）

| 现象 | 原因 | 做法 |
|---|---|---|
| 页面永远画在最上层，和主菜单叠在一起 | 对克隆页调了 `SetActive(true)`；游戏是"一次只激活一个页" | **绝不**改克隆页的激活状态，可见性交还菜单系统 |
| 出现两行"返回" | 用 `Destroy` 删模板返回行，而它延迟到帧末才消失，布局当帧就重算 | 用 `DestroyImmediate` |
| 返回行点不到 | 这些页面的可视区到 `y ≈ -670` 就被面板裁掉 | 返回行放到**标题正下方**（第一行） |
| 参数行不显示/不可点 | 页面的链式过渡按"它认识的行"缓存了每行 `CanvasGroup`，后加的行 alpha 还是 0 | 构造完后对每行强制 `alpha=1 / interactable / blocksRaycasts`，并调用 `MenuCompleteShowImmediately` 一类方法 |
| 文字变成"邻居行的文字"（如 `窗口化`、`Fatigue reserve`） | 游戏本地化按位置/顺序覆盖行标签 | 见第 5 节的自愈 |
| 克隆自图形页的行**整行文字不可见** | 图形页那些行的字体来自本地化系统，克隆后没有字体 | 见第 6 节的字体同步 |
| 滑条显示成了模板的值（锐化=1、预设=5） | 构造期间控件的引导流程把模板值写进了配置 | 构造期加 `_constructing` 闸门，吞掉这一阶段的写请求 |

---

## 3. 值存到哪儿：拦截 `MenuSettings`

**实测**：游戏的 `MenuSettings.SetBool/SetFloat/SetInt` 对**未知 id** 是**静默丢弃**的——
不抛异常、不写值、连 `Changed` 事件都不触发。（最初我们以为"注册了 preset 就能用原生存储"，结果
`SetBool(50,false)` 之后回读仍然是 `true`。）

所以本插件用 Harmony 前缀**接管**这几个静态方法：

| 方法 | 前缀行为 |
|---|---|
| `GetBool/GetFloat/GetInt(MenuSettingId settingId)` | 是我们的 id → 从配置读，`return false`（跳过原方法） |
| `SetBool/SetFloat/SetInt(MenuSettingId settingId, …)` | 是我们的 id → 写配置 + 刷新行视觉，`return false` |

**Harmony 在 IL2CPP 后端必须精确匹配形参名**：`MenuSettings.SetFloat` 的形参名是 `settingId`
（不是 `id`）。写错名字会得到 `IL Compile Error`，而且**没有类型回退**。
对于生成代码里**完全丢失形参名**的方法（例如 `MenuSliderSettingButton.OnSliderChanged`），
改用位置参数：

```csharp
private static void Prefix_SliderChanged(MenuSliderSettingButton __instance, object[] __args)
{
    float value = Convert.ToSingle(__args[0], CultureInfo.InvariantCulture);
    ...
}
```

另外，Harmony 在 IL2CPP 后端**失败时不一定抛异常**，所以要回查：
`Harmony.GetPatchInfo(method)` 里确实出现我们的 patch 才算装上了（插件启动日志会逐条打印 `patch <名字>: OK`）。

### 存储实现

| 参数类别 | 文件 | 写法 |
|---|---|---|
| Feeder 键 | `<游戏目录>\dlss5-feed.cfg` | 逐行替换 `key=value`；**UTF-8 无 BOM**（带 BOM 时 Feeder 不认，实测） |
| RenoDX 键 | `<游戏目录>\ReShade.ini` 的 `[RenoDX.DLSS5]` 段 | 同上（找不到键就插到段首） |

写值前先比较：值没变就**不碰文件**（减少无谓的 Feeder 重读与日志噪音）。

---

## 4. 总开关为什么用 `mode` 而不是 `enabled`（重要）

最初总开关直接写 `dlss5-feed.cfg` 的 `enabled`。实测发现：

1. 写 `enabled=0` 之后，`dlss5-feed.log` 打出一条 `config: enabled=0`，**然后彻底安静**——
   连后续的配置重读日志都没有了。
2. 此后写 `enabled=1`：文件确实变了，但 Feeder 不再重读 → **喂帧永远不恢复**，只能重启游戏。

即：`enabled=0` 会让 Feeder 把**配置文件监视**一起停掉（文档只写了"不喂帧"，
但实测的后果更强）。所以：

- `enabled` 全程保持 `1`（插件在 `Load` 时先写一次 `enabled=1` 兜底）；
- 总开关驱动 **`mode`**：`2` = 完整 DLSS 通路，`0` = 惰性。这条路径**可反复开关**（实测）。

配套的两个保护：

- **防抖 800 ms**：`OnSubmit` 前缀里做，连点只算一次。实测快速 0/1 翻转会撞上
  effect runtime 的销毁/重建，把喂帧会话卡死。
- **喂帧看门狗**（每 2 s 采样一次 `dlss5-feed.log` 的"喂帧活动行数"）：
  - 开启后 8 s 内没有新增活动 → 写 `mode=0`，2.5 s 后写 `mode=2`（强制重绑 effect runtime）；
  - 再 10 s 仍无活动 → `rebuild++`（强制重建 NGX 特征）；
  - 再 10 s 仍无活动 → 日志里明确报 `FEED NOT RESUMED`。

总开关行标签显示的是**实测状态**（`[运行中]` / `[已停止]`），来源就是上面的活动采样，
而不是配置里那个标志位——配置写了不等于真的在跑。

---

## 5. 标签漂移自愈

游戏的本地化流程会按自己的顺序往行里写文字，导致我们的行显示成**别的行的标签**
（实测出现过 `窗口化`、`Fatigue reserve`、`音乐音量` 等）。三重保护：

1. **钩子**：`MenuToggleSettingButton.OnEnable`、`RefreshVisuals`、`InterfaceFastMenu.RefreshLocalizedTexts`
   三个前缀/后缀里重设标签；
2. **巡检**：每 2 s 检查每一行的 `setting` 与标签，发现漂移立即重设（并打 `DRIFT` 日志）；
3. **只写标签对象**：行里有多个文字对象——标签（直接子物体 `Text`）与滑条数值（`Slider`/`Value` 下）。
   最初"把行里所有文字组件都写一遍"会把数值位也写成标签，看起来就是"每行两份文字、还叠在一起"。
   现在只写标签，并跳过 `Slider` / `Case` / `CaseCheck` / `Value` 子树。

顺便：`SetLabel` 会把 `Text.horizontalOverflow/verticalOverflow` 设为 `Overflow`，
避免我们较长的中文标签被控件裁剪；TMP 标签还会把 `maxVisibleCharacters` 抬到足够大
（游戏的打字机限流会把标题截成 `DLSS5 设`）。

---

## 6. 字体同步

从**图形页**克隆来的行（返回行、导航行）在这些页面里最终文字不可见，原因是它们的字体来自本地化系统，
克隆体没有拿到。而克隆自**睡眠页**的行（滑条行）是好的。

做法：等 L2 页真正可见（此时游戏已经把中文字体套到模板行上）之后，从**第一行能读到的有效标签**
取 `Font` + `FontSize`，同步到我们所有行（含标题与返回行），并打一条日志：

```
font sync: 'FontXxx' size 40 applied to 12 rows
```

⚠️ 不要从**页面标题**或**尚未本地化的行**抓字体——实测那样会拿到一个中文字形不全的字体，
结果是"神经__总开关[__行中]"这种缺字。

---

## 7. ReShade 启动横幅（二进制补丁）

ReShade 6.8 启动时会画一条横幅（`ReShade 6.8.0 / 访问 https://reshade.me … / 按 X 打开配置面板`）。
它的行为（源码 `source/runtime_gui.cpp`，v6.8.0）：

```cpp
const bool show_splash_window = _show_splash &&
      (is_loading()
    || (_reload_count <= 1 && (_last_present_time - _last_reload_time) < std::chrono::seconds(5))
    || (!_show_overlay && _tutorial_index == 0 && _input != nullptr));
```

并且 splash 窗口自己 `ImGui::PushStyleVar(ImGuiStyleVar_Alpha, 1.0f)`（所以调 `[STYLE] Alpha` 遮不住）。
`_show_splash` 只在 `reload_effects()` 里被置真，而启动时必然会重载一次效果。

实测过的**无效**方案：
- `[OVERLAY] ShowFPS/ShowClock/ShowPresetTransitionMessage/ShowScreenshotMessage = 0`：只能关掉别的角标；
- `[STYLE] FontSize=1`：splash 的排版不跟着塌（且 ReShade 会把自己的样式写回 ini）；
- `[GENERAL] NoReloadOnInit=1`：横幅确实没了，但**效果根本没加载**，
  `dlss5-feed.log` 直接 `DLSS5_Feed.fx technique MISSING` → DLSS5 全废；
- 让 `_tutorial_index` 非 0（`TutorialProgress=4/10`）：只关掉了上面第三个条件，
  编译期与"编译后 5 秒"这两条依旧。

因此采用**一处跳转补丁**（本地 `dxgi.dll`，即 ReShade 本体；改前请备份）：

| 项 | 值 |
|---|---|
| 文件偏移 | `0xD08E4`（VA `0x1800D14E4`） |
| 原始字节 | `0F 84 E7 09 00 00`（`je` 到 `0x1800D1ED1`） |
| 补丁后 | `E9 E8 09 00 00 90`（`jmp` 到同一目标 + 1 字节 `nop`） |
| 含义 | 把"`show_splash_window` 为假则跳过"的守卫改成**无条件跳过** splash 绘制块 |

⚠️ **一个字节的教训**：`jmp rel32` 是 5 字节，`je rel32` 是 6 字节。若沿用原来的 rel32
（`E9 E7 …`），落点会变成 `0x1800D1ED0`——比正确目标**早一个字节**，正好落在
`mov r12,[rbp-20h]` 中间，游戏启动即崩。必须把 rel32 加 1（`E8`）。
定位这个位置用的方法：在二进制里找到唯一的 `"Splash Window"` 字符串引用
（`lea rcx,[rip+…]`）→ 用 BepInEx 自带的 `Iced.dll` 反汇编 → 找到跳向同一目标的**两处**条件跳转
（对应 `show_splash_window` 与 `!(show_spinner && show_overlay)`）。

> 这是对**第三方二进制**的修改，仅在你自己的游戏副本上做；ReShade 升级后偏移会变，需重新定位。

---

## 7.5 本地化：界面文字如何跟随游戏语言

我们的行是克隆出来的，携带的是模板行的身份（例如 `SleepFatigue`），所以**不能**依赖游戏为这些行生成文字。文字由插件自己生成，语言取自游戏本身：

| 用途 | 游戏 API | 说明 |
|---|---|---|
| 语言判定 | `LocalizationManager.CurrentLanguage` | 游戏保存当前语言（形如 `Chinese` / `Russian` / `Ukrainian`） |
| 切换通知 | `LocalizationManager.LanguageChanged`（静态 `Action` 事件） | 订阅后切语言立刻重刷我们的行 |
| 字体 | `LocalizationManager.CurrentFont` | 中日韩与俄文用不同字体，直接取游戏已选好的那个 |
| 就绪判定 | `LocalizationManager.CurrentRevision` | 启动早期它会先报**占位语言**（实测 `Ukrainian`），`> 0` 才代表语言表已应用 |

生成规则（见 `src/NeuroMita.DLSS5/Localization.cs`）：

1. 所有可见文字都走 `T(key)`，**在写标签的那一刻**解析，不在构建期固化；参数表里的 `Label` 存的是翻译键（如 `param.workres`）。
2. 语言变化 → `ApplyLocalization(true)`：重写 11 行参数 + 导航行 + 标题 + 返回键，并置 `_fontsSynced = false` 让字体跟着换。
3. **不借用游戏自己的文本**：早期试过复制"返回"按钮的文字，结果切语言时游戏自己的行还停在旧语言，出现俄文 `НАЗАД` 压在一堆中文行上面的混搭状态，已回退为只用本地表。
4. **250 ms 标签守卫**：游戏在 `RefreshLocalizedTexts` / `ApplySettingLocalization` 时会把模板行的文字写回我们的克隆行（实测表现为整页变成 `Fatigue reserve`）。守卫与 `RefreshLocalizedTexts` 后置补丁会在同一帧或 250 ms 内改回来。
5. **长文案**：标签槽宽度固定、右侧是数值与滑条，因此标签启用 `resizeTextForBestFit`（下限取最大字号的一半），长语言（俄语、德语）自动缩小而不是压到滑条上；同时把文案压短（`NR style` 而非带括号的完整解释），解释放在 `docs/CONFIG.md`。

支持的 11 种语言：中文、Nederlands、English、Français、Deutsch、Polski、Română、Русский、Español、Türkçe、Українська（顺序即 `Lang` 枚举）；未知语言回退英文。

---
## 8. 自测与诊断（`Automation.SelfTest`）

默认关闭（会接管菜单）。打开后插件在启动时自动执行并打日志：

1. **存储往返**：对每个 Feeder 键写一个中间值再写回原值，核对文件内容（`STORETEST … OK`）；
2. **开关 + 喂帧**：点一次总开关 → 等 4 s 稳定 → 再等 12 s 确认活动不增长（`[OK: feeding stopped]`）
   → 点回来 → 15 s 内确认活动增长（`[OK: feeding resumed]`）；
3. **导航**：调用 `设置` → `图形页` → L1 行的 `OnSubmit`，然后打印 L2 每行的
   标签 / 位置 / 键 / 值（`L2 ACTIVE -- rows:`）；
4. **结构 dump**：图形页与 L2 页的每个子物体（名字 / 类型 / 坐标 / 标签 / setting），
   以及关键行的文字对象树（`TEXTPROPS`，含 `font` / `size` / `colorA` / `enabled`）。

排查用法：先看 `patch …: OK` 是否齐全，再看 `STORE/CLICK/DRIFT/LOCKED/watchdog/font sync`，
最后对照 `dlss5-feed.log` 与 `ReShade.log`。详见 [TROUBLESHOOTING.md](TROUBLESHOOTING.md)。
