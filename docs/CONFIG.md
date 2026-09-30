# 参数总表

本项目有两套参数源，**生效方式完全不同**，这是最容易踩的坑：

| 来源 | 文件 | 何时读取 | 改完什么时候生效 |
|---|---|---|---|
| DLSS5-Feeder | `<游戏目录>\dlss5-feed.cfg` | 运行中**热重读**（文件变化即重读） | **即时** |
| RenoDX DLSS5 消费端 | `<游戏目录>\ReShade.ini` → `[RenoDX.DLSS5]` | **只在加载时读一次** | **重启游戏** |

游戏内 L2 页（`设置 → 图像 → DLSS5 神经渲染 → DLSS5 设置`）暴露了其中 11 行；其余键可以直接改配置文件。

---

> 所有界面文字都跟随游戏的语言设置（游戏自带的 11 种语言，见 `docs/ARCHITECTURE.md` 第 7.5 节）；下表里的显示名按**中文**口径列出。

## 1. 游戏内 L2 页的 11 行

| 显示名 | 底层键 | 类型 | 范围 | 默认 | 生效方式 |
|---|---|---|---|---|---|
| 神经渲染总开关 | `mode` | 开关 | `2` 开 / `0` 关 | `2` | 即时 |
| 工作分辨率 | `work_resolution` | 滑条 | 50–100（%，步进 5） | `100` | 即时 |
| 锐化 | `work_sharpness` | 滑条 | 0–1（步进 0.05） | `0.30` | 即时 |
| DLSS 预设 | `preset` | 滑条 | 0–11（步进 1） | `0` | 即时 |
| NR 风格 | `NRLookMode` | 滑条 | 0–2（步进 1） | `0` | **重启游戏** |
| NR 强度 | `NRIntensity` | 滑条 | 0–2（步进 0.05） | `1` | **重启游戏** |
| 局部色调强度 | `NRLocalTone` | 滑条 | 0–2（步进 0.05） | `1` | **重启游戏** |
| 局部结构强度 | `NRLocalStructure` | 滑条 | 0–2（步进 0.05） | `1` | **重启游戏** |
| 皮肤结构强度 | `NRSkinStructure` | 滑条 | −1–2（步进 0.05） | `0`（`-1` = 关闭） | **重启游戏** |
| 自动遮罩 | `NRAutoMask` | 开关 | `0`/`1` | `0` | **重启游戏** |

> 页面上标 `(重启)` 的行就是"需要重启游戏"的那一类。
> `Multi Pass`（`NRPasses`，1–10）与 `界面修正`（`NRUICorrection`）、`抗闪烁`（`NRDetailStability`）、
> `跟随输入分辨率`（`NRFollowInputRes`）当前没有放进菜单（页面可视区最多约 10 行，再多会被面板裁掉、点不到），
> 需要时直接改 `ReShade.ini`。

---

## 2. DLSS5-Feeder 键（`dlss5-feed.cfg`，即时生效）

### 2.1 一定要知道的三个

| 键 | 默认 | 说明 |
|---|---|---|
| `enabled` | `1` | **必须恒为 `1`。实测：Feeder 读到 `enabled=0` 之后会连"配置文件监视"一起停掉** —— `dlss5-feed.log` 在 `config: enabled=0` 之后再无任何输出，此后写 `enabled=1` 也不会被读到，只能重启游戏。所以本插件的总开关**走 `mode`，永远不写 `enabled=0`**。 |
| `mode` | `2` | `0` = 惰性（inert）、`1` = 只测传输（不跑 NGX）、`2` = 完整 DLSS 通路。**本插件的总开关就是 2 ↔ 0。** |
| `rebuild` | `0` | 手工触发一次特征重建：**改这个数字**即可（本插件的看门狗在恢复失败时会 `rebuild++`）。 |

### 2.2 画面 / 质量相关

| 键 | 默认 | 范围 / 取值 | 说明 |
|---|---|---|---|
| `work_resolution` | `100` | 50–100（%） | **仅 D3D**。低于 100% 是"降采样 → 处理 → 回扩"，是一个**成本旋钮**，不是 DLSS 超分；会牺牲锐度（低于 100% 时本插件会在行标签上提示"降锐度"）。 |
| `work_upscale` | `0` | `0` 双线性 / `1` FSR1 / `2` 合成抖动上的 DLSS-SR（不推荐） | 上面的"回扩"方式；`2` 实测不比原生便宜且会抖动。 |
| `work_sharpness` | `0.30` | 0–1 | RCAS 锐化强度。 |
| `preset` | `0` | `0` 默认 / `5`,`6` 经典 CNN（E,F）/ `10`,`11` transformer（J,K） | DLSS 渲染预设提示（按 DLSS 契约传递）。 |
| `jitter_sign` / `jitter_phases` | `±1` / `0`(自动 Halton) | 诊断用 | 仅在 `work_upscale=2` 时才有意义。 |
| `hold_strength` | `0.000` | 0–1 | 实验性输出稳定（把 NR 的编辑沿运动向量做时域过滤）。当前没有放进菜单。 |
| `hold_tolerance` | `0.040` | 0.01–0.30 | 上面的容差。 |

### 2.3 HDR / 深度 / 色彩

| 键 | 默认 | 取值 | 说明 |
|---|---|---|---|
| `hdr` | `-1` | `-1` 自动 / `0` 强制 SDR / `1` 强制 HDR | 本游戏当前是 SDR 输出，实际落在 SDR。 |
| `hdr_bridge` | `-1` | `-1` 自动 / `0` 关 / `1` 强制 | HDR 桥接（HDR10 场景才有意义）。 |
| `hdr_paper_white` | `203` | nits | 纸白亮度。 |
| `depth_inverted` | `-1` | `-1` 跟随 ReShade / `0` / `1` | 深度方向覆盖。实测放在 `-1`（跟随）即正确。 |
| `flags` | `-1` | `-1` 默认 | 直接覆盖 NGX 建特征标志，调试用。 |

### 2.4 时序 / 诊断（一般不用动）

| 键 | 默认 | 说明 |
|---|---|---|
| `reset_every` | `0` | 每帧 NGX 重置（诊断，会毁掉时域质量）。 |
| `warmup_rebuild` | `180` | 前 N 帧强制重建（RenoDX 路径）。 |
| `create_delay` | `60` | runtime 重建后延迟多少帧再建特征。 |
| `settle_evals` | `0` | 稳定前先跑几次评估（0–8）。 |
| `gpu_timeout_ms` | `2000` | 单次评估的 GPU 超时。 |
| `mv_scale_x` / `mv_scale_y` | `1.000` | 运动向量倍率。 |
| `log_frames` | `3` | 头几帧逐帧打日志。 |
| `stall_log_ms` | `50` | 超过这个帧时间就记 stall。 |
| `buffer_home` / `async_home` / `sync_home` | `1` / `0` / `0` | 缓冲归属 / 异步 / 同步的传输模式。 |
| `host_*`（如 `host_mode`、`host_width`、`host_height`、`host_fps`） | — | **仅 32 位游戏**的 64 位 helper 模式使用；本游戏是 64 位，走进程内，不需要。 |

> 完整列表以你机器上 Feeder 自己生成/维护的 `dlss5-feed.cfg` 为准（它会在运行时把当前值打进 `dlss5-feed.log` 的 `config:` 行）。

---

## 3. RenoDX DLSS5 键（`ReShade.ini` → `[RenoDX.DLSS5]`，需重启游戏）

### 3.1 本插件暴露的部分

| 显示名 | 键 | 范围 | 默认 | 备注 |
|---|---|---|---|---|
| NR 风格 | `NRLookMode` | 0–2 | `0` | `0` 默认 / `1` 自然 / `2` 电影（与 Magpie 的 "NR Style" 对应） |
| NR 强度 | `NRIntensity` | 0–2 | `1` | 神经渲染的整体强度 |
| 局部色调强度 | `NRLocalTone` | 0–2 | `1` | 局部对比/色调修正强度 |
| 局部结构强度 | `NRLocalStructure` | 0–2 | `1` | 局部结构修正强度 |
| 皮肤结构强度 | `NRSkinStructure` | −1–2 | `0` | `-1` 表示关闭该项 |
| 自动遮罩 | `NRAutoMask` | 0/1 | `0` | 自动生成遮罩，避免在 UI/文字上乱动 |
| 界面修正 | `NRUICorrection` | 0/1 | `0` | UI 元素看起来被神经渲染弄坏时再开 |
| Multi Pass | `NRPasses` | 1–10 | `1` | 每帧串联多次神经通路；**成本成倍增长**（单次已在 RTX 5060 Ti 上约 8–13 ms） |
| 抗闪烁（细节稳定） | `NRDetailStability` | 0/1 | `0` | 沿运动向量对 NR 结果做时域过滤（RenoDX 侧） |
| 跟随输入分辨率 | `NRFollowInputRes` | 0/1 | `0` | 会降低 DLSSNR 质量 |

### 3.2 该段里的其它键（直接改文件即可）

`ConfigVersion`(=7)、`EnableHooks`(2 = 只挂 NGX)、`NeuralUplift`、`NRChainedHistory`、`NRChromaClamp`、
`NRCodecMode`、`NRColorStrength`、`NRCostMeter`、`NRDepthMode`、`NRDiffuseWhiteNits`、`NRDisableRelease`、
`NRDisplayPedestal`、`NREditTrace`、`NREnableUpscaling`、`NRFeedMode`、`NRGpuTimers`、`NRHookPoint`、
`NRLinearUnitNits`、`NRLook*`（`Brighten`/`Colour`/`Darken`/`Detail`/`DetailRadius`/`Halo`/`Highlights`/`Hue`/
`Midtones`/`Shadows`/`Stabilize`/`StabilizeDetail`/`StabilizeMs`/`Strength`/`Tone`/`Upsample`/`Max*`）、
`NRMaxWorksets`、`NRMVecScaleX/Y`、`NRNeuralFloorGuard`、`NRNorm*`（归一化/governor）、`NRPaperWhiteScale`、
`NRPass2*` / `NRPass3*` / `NRPass4*`（多层模式下每层独立参数）、`NRPQCalibration`、`NRPresent*`、
`NRPreset`、`NRProxyAnchor`、`NRResolutionScale`、`NRScreenshot*`（`NRScreenshotKey=116`）、
`NRShutdownQuiesce`、`NRSkinIndependent`、`NRSourceEncoding`、`NRSourcePrimaries`、`NRStyle`、
`NRStyleHighlightGuard`、`NRToggleKey`(=117)、`NRTransferMode`、`NRTransferStrength`、`NRUICorrection`、
`NRWorksetFreePolicy`、`UiLanguage`、`UiMode`、`UiWelcomed`。

### 3.3 本插件对 `ReShade.ini` 的其它修改（为了"干净"）

| 键 | 值 | 原因 |
|---|---|---|
| `[OVERLAY] ShowFPS` | `0` | 关掉 ReShade 的 FPS 角标 |
| `[OVERLAY] ShowClock` | `0` | 同上 |
| `[OVERLAY] ShowPresetTransitionMessage` | `0` | 关掉预设切换提示 |
| `[OVERLAY] ShowScreenshotMessage` | `0` | 关掉截图提示 |
| `[OVERLAY] KeyOverlay` | `0` | 解绑 overlay 热键（DLSS5 全部走游戏内置界面） |
| `[RenoDX.DLSS5] NRCostMeter` | `0` | 关掉消费端的开销 HUD |
| `[RenoDX.DLSS5] UiWelcomed` | `1` | 不再显示欢迎卡片 |

> ReShade 的**启动横幅**（`ReShade 6.8.0 / 访问 https://reshade.me ... / 按 X 打开配置面板`）
> **没有任何配置开关**（源码：`show_splash_window` 在初始着色器编译期间为真，编译后 5 秒内仍为真，
> 且 splash 窗口自己 `PushStyleVar(Alpha, 1.0)`）。本项目用一处二进制跳转补丁处理，见
> [ARCHITECTURE.md](ARCHITECTURE.md#reshade-启动横幅二进制补丁)。

---

## 4. 与 Magpie 的参数对照（以及为什么只借参数、不借架构）

[Magpie（SAOG0721 分支）](https://github.com/SAOG0721/Magpie) 的 DLSSNR 面板暴露了同一批每层参数，
它的文档给出的默认值与范围：

| 每层独立参数 | 默认值 | 范围 | 本项目的键 |
|---|---|---|---|
| NR Style | 0 | 0–2 | `NRLookMode` |
| NR Intensity | 1 | 0–2 | `NRIntensity` |
| Local Tone Strength | 1 | 0–2 | `NRLocalTone` |
| Local Structure Strength | 1 | 0–2 | `NRLocalStructure` |
| Skin Structure Strength | 0 | 0–2 | `NRSkinStructure`（本项目额外支持 `-1` = 关闭） |
| Automatic Mask | 关闭 | 开/关 | `NRAutoMask` |
| NR UI Correction | 关闭 | 开/关 | `NRUICorrection` |
| Multi Pass | 1 | 1–3（Magpie 面板上限） | `NRPasses`（RenoDX 侧可到 10） |
| 抗闪烁 | 无 | 无 / 静态累积 / 光流累积 / 光流累积+ / 低频时域重建 | `NRDetailStability`（RenoDX 侧只有开关；Magpie 的五档是它**自己**的时域稳定实现，存 `antiFlicker=0..4`） |

**架构差别（重要）**：Magpie 是**屏幕采集**管线——抓屏 → 在自己的进程里跑 DLSSNR → 显示；
本项目走**游戏自己的渲染管线**——ReShade 注入游戏帧，Feeder 用合成契约把
帧/深度/运动向量喂给 DLSS，消费端在同一进程内做神经渲染。因此：
Magpie 的"光流方法/光流质量"是它自己的采集侧实现，本项目**不适用**（我们的运动向量来自
ReShade 侧的 LumeniteFX Kernel，由 `DLSS5_Feed.fx` 的 `DLSS5_MV_PROVIDER=3` 选择）。
我们只借它的**参数集与默认值**，不借它的架构。

---

## 5. 为什么 RenoDX 参数必须重启游戏（实测结论）

RenoDX DLSS5（`renodx-dlss5` 8.5.0-rc10）**只在加载时读取它的 ini 段**。实测过程：

1. 启动前把 `NRLookMode=2`、`NRIntensity=1.6` 写进 `ReShade.ini` → 启动后 `ReShade.log`
   的 `NR effective settings` 显示新值 → **启动时确实会读**。
2. 游戏运行中改 `ReShade.ini`（先确认文件里数值已改）：
   - 等 40 s → `NR effective settings` 仍是旧值；
   - `rebuild` 加一（强制重建 NGX 特征）→ 仍是旧值；
   - `mode` 0 → 2（关开总开关，强制重建喂帧会话）→ 仍是旧值。
3. 所以此前设想的"关掉总开关再调参、打开就生效"**不成立**，UI 上这些行统一标注 `(重启)`。

Feeder 侧参数不受此限制：`dlss5-feed.cfg` 变化会被立刻重读（`dlss5-feed.log` 每次都会打出新的 `config:` 行）。
