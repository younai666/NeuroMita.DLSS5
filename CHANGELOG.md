# CHANGELOG

本项目所有版本都在 **2026-09-30** 这一天的实机迭代中形成，版本号按"能力台阶"划分。
下面每一条都对应实测结论，不是计划项。

---

## 1.0.0 — 总开关改用 `mode`、看门狗、以及"能真正用起来"的一版

- **界面文字跟随游戏语言**：用 `LocalizationManager.CurrentLanguage` 判定语言，并挂 `LanguageChanged` 事件即时重刷，覆盖游戏自带的 11 种语言。启动早期游戏会先报一个占位语言（实测 `Ukrainian`），因此用 `CurrentRevision` 门控"提交语言"，避免先显示错语言再跳正。标签启用 best-fit 自适应字号，俄/德等长文案不再冲出到数值与滑条上。新增 250 ms 标签守卫并在 `RefreshLocalizedTexts` 后置补丁里立刻重刷——游戏切语言时会用它自己模板行的文本（如 `Fatigue reserve`）回写我们的克隆行，守卫负责压住它。
### 修复（都是实测踩出来的）

- **总开关失灵的真因**：Feeder 一旦读到 `enabled=0`，会连**配置文件监视**一起停掉
  （`dlss5-feed.log` 在 `config: enabled=0` 之后再无任何输出），此后写 `enabled=1` 也不会被读到。
  → 总开关改为驱动 **`mode`**（`2` = 完整 DLSS 通路，`0` = 惰性），`enabled` 全程保持 `1`。
- **关闭校验与再次开启的竞态**：关闭后的"是否真的停了"重试会把 `enabled`/`mode` 又写回关闭值，
  表现为"开了又被自己关掉"。→ 开启时立即取消待执行的关闭校验。
- **连点导致 Feeder 会话卡死**：快速 0/1 翻转撞上 effect runtime 重建，喂帧再也不恢复。
  → `OnSubmit` 前缀加 **800 ms 防抖**（连点只算一次）。
- **喂帧看门狗**：开启后 8 s 内 `dlss5-feed.log` 没有新活动 →
  自动 `mode 0→2` 循环（2.5 s 间隔）→ 仍无活动则 `rebuild++` 强制重建 NGX 特征 → 最后明确报错。
- **状态可读**：总开关行标签显示**真实**喂帧状态（`[运行中]` / `[已停止]`），
  依据是日志里的活动增量，而不是配置里那个标志位。
- **ReShade 启动横幅**：源码确认它没有任何配置开关（初始着色器编译期间绘制 + 编译后 5 秒，
  且 splash 窗口强制 `Alpha=1.0`）；跳过初始效果加载会让 DLSS5 直接失效
  （实测 `DLSS5_Feed.fx technique MISSING`）。→ 采用 `dxgi.dll` 单点跳转补丁：
  `0xD08E4: 0F 84 E7 09 00 00 → E9 E8 09 00 00 90`。
- 文档：驱动 610.74 实测可用（RenoDX 签名 snippet 路径）但官方要求 ≥ 616.56；
  `nvngx_dlssnr` 310.8.x 仅 RTX 50 能创建神经特征。

---

## 0.3.0 — 单页 10 参数 + 字体同步（可读、可点）

- **去掉 L3 页**：参数收敛到一页 10 行（先前 16 行会顶到面板之外，底部行既看不见也点不到）。
- **返回行移到标题正下方**：这些页面的可见区域到 `y ≈ -670` 就被裁掉，放最底下等于没有。
- **页面可见性交还菜单系统**：不再对克隆页 `SetActive(true)`——那会让它永远画在最上层，
  和主菜单叠在一起（实测过）。
- **标签只写标签对象**：行里有多个文字对象（标签 + 滑条数值），之前"全都写"导致数值位也被覆盖成标签。
- **字体同步**：从图形页克隆来的行（返回行等）缺少字体 → 文字不可见；
  页面可见后（游戏已套用中文）从模板行同步字体到所有行。
- **滑条数值**：`MenuSliderSettingButton` 的 `currentValue` 由我们喂入，避免模板值泄漏进配置。
- **RenoDX 参数接入**：新增 `ReShade.ini [RenoDX.DLSS5]` 读写
  （`NRLookMode` / `NRIntensity` / `NRLocalTone` / `NRLocalStructure` / `NRSkinStructure` / `NRAutoMask` / `NRPasses`）。
- **实测结论**：RenoDX 参数**只在加载时读一次**——改完之后 `rebuild++`、`mode 0→2` 都不会重读
  （telemetry 里 `look=0 / intensity=1` 不变），因此这些行标注 `(重启)`。
  → 之前设想的"关闭总开关调参、再打开即生效"被证伪。

---

## 0.2.0 — L1/L2 结构（从"一行开关"变成"一个设置页"）

- **L1 入口行**：克隆图形页自带的 `MenuGoToPanelButton` 返回行，改目标指向我们克隆出来的页面。
- **L2 参数页**：以"睡眠与疲劳"页（`Location Settings SleepFatigue`）为模板
  （标题 + 滑条行 + 开关行 + 返回行），删掉模板行、按目标顺序重建。
- **排版用游戏自己的助手**：这些页面的行是**固定坐标**，没有布局组——
  必须调用游戏私有静态方法 `InterfaceFastMenu.CopyRowPositionAndMakeRoom(源行, 新行)` 才会"让位"；
  单独 `SetSiblingIndex` 会导致多行叠在一起（实测）。
- **`DestroyImmediate` 换返回行**：`Destroy` 延迟到帧末，而布局当帧就重算，会出现两行"返回"。
- **`MenuSettings` 读写拦截**：原生 setter 对自定义 id **静默丢弃**（不报错、不触发 `Changed`），
  于是所有读写都接到我们的配置源上。
- **Harmony 必须精确匹配形参名**：IL2CPP 后端不按类型回退，
  `MenuSettings.SetFloat(MenuSettingId settingId, float value)` 的形参名写成 `id` 会直接
  `IL Compile Error`；没有形参名的方法（如 `OnSliderChanged`）改用 `object[] __args` 取位置参数。
- **标签漂移自愈**：游戏本地化会把我们行的标签覆盖成邻居行的文字（实测出现 `窗口化`、
  甚至 `Fatigue reserve`），用 `OnEnable` / `RefreshVisuals` / `RefreshLocalizedTexts` 钩子 + 每 2 s 巡检拉回。

---

## 0.1.0 — 第一版：图形页里的一个开关行

- 往 `MenuSettingRegistry._map` 注入自定义 `MenuSettingId` 的 `MenuSettingPreset`，让游戏设置系统认识这个开关。
- 克隆图形页现成的开关行、`ConfigureSetting` 到自定义 id。
- `OnSubmit` 前缀接管点击：翻转 → 写 `dlss5-feed.cfg` 的 `enabled` → 刷新勾选视觉。
- 标签漂移、页面重建后重注入、`RebuildPanelOrder` 重建导航顺序。
- 实测：16 项存储测试全绿；点击链路可翻转喂帧；`NR-VERDICT state=ENGAGED`、
  `inline feature 18 evaluation succeeded`。
