# NeuroMita DLSS5

**中文** | [Русский](README.ru.md)

> 仓库 / Repository: <https://github.com/younai666/NeuroMita.DLSS5>

给 **NeuroMita**（MiSide 的 AI 改版）加上**内置的 DLSS 5 神经渲染**：开关与参数直接做进游戏**自带的设置菜单**（`设置 → 图像 → DLSS5 神经渲染 → DLSS5 设置`），不使用 ReShade 那套独立浮层界面。

> **声明**：本项目**不是 NVIDIA 官方项目**，与 NVIDIA、ReShade、RenoDX、DLSS5-Feeder 的维护者均无隶属关系。项目只提供**集成与界面**，DLSS 5 的运行时（`nvngx_dlss.dll` / `nvngx_dlssnr.dll`）与消费端插件由各自的发布方提供，本项目**不重新分发**它们。DLSS、RTX 是 NVIDIA 的商标。

---

## 支持矩阵

| 项目 | 已验证的版本 / 要求 |
|---|---|
| 游戏 | NeuroMita `v2026.09.28`（Unity 6000.3.12f1，IL2CPP，metadata v39，64 位） |
| 渲染 | **内置管线 BiRP**，**D3D11 呈现**（进程内另有 D3D12 设备用于 NGX 计算） |
| 插件宿主 | BepInEx **6.0.0-be.788**（IL2CPP / CoreCLR） |
| 注入宿主 | ReShade **6.8.0.2155**（add-on 构建，作为 `dxgi.dll` 本地加载） |
| 喂帧层 | DLSS5-Feeder **1.18.0-beta.1**（`dlss5-feed.addon64`） |
| 神经消费端 | RenoDX `renodx-dlss5` **8.5.0-rc10**（+ `nvngx_dlssnr.dll` 310.8.3.0、`nvngx_dlss.dll` 310.9.1.0） |
| 运动向量 | LumeniteFX Kernel（`DLSS5_MV_PROVIDER=3`） |
| GPU | **神经模型仅 RTX 50 可用**（本项目在 RTX 5060 Ti / Blackwell2 上验证；RTX 20/30/40 只有 DLAA，没有神经渲染） |
| 驱动 | 官方口径要求 **≥ 616.56**；本项目在 **610.74** 上实测可用（走 RenoDX 签名 snippet 路径），但仍建议升级 |
| 系统 | Windows 10 / 11 x64 |

实测性能（1080p、DLAA + 神经渲染、同一场景）：**开启约 78 fps**，**关闭约 230 fps**；神经渲染本身约 **8–13 ms/帧** GPU 开销。也就是说：**这套 DLSS 5 是"画质换帧数"，不是超分提帧**（DLAA 在原生分辨率上工作）。

---

## 效果截图

| 文件 | 内容 |
|---|---|
| `docs/screenshots/settings-l1.png` | 图形页里的 **L1 入口行**：`DLSS5 神经渲染` |
| `docs/screenshots/settings-l2.png` | **L2 参数页**：`DLSS5 设置`（11 个参数 + 返回） |

> 截图由使用者在自己的游戏内补齐（见 [`docs/screenshots/README.md`](docs/screenshots/README.md)）。

---

- **界面文字跟随游戏语言**：行标签与游戏的语言设置联动（游戏自带的 11 种语言全覆盖），在游戏里改语言会立刻刷新，不需要重启；长文案会自动缩放字号，不会顶到数值与滑条上。
## 特性

- **内置设置菜单**：L1 入口行做进游戏原生图形页，点进去是 L2 参数页；行的样式、勾选动画、导航（鼠标/键盘/手柄）全部复用游戏控件。
- **即时生效**：Feeder 侧参数（总开关、工作分辨率、锐化、DLSS 预设）写入 `dlss5-feed.cfg`，Feeder 运行时热重读，**不用重启**。
- **状态可见**：总开关一行的标签会显示**真实**喂帧状态（`[运行中]` / `[已停止]`），依据是 `dlss5-feed.log` 的实际活动，而不是配置文件里的一个标志位。
- **防抖 + 喂帧看门狗**：连点 800 ms 内只算一次；开启总开关后若 8 s 内没有恢复喂帧，自动执行 `mode 0→2` 循环，仍失败则 `rebuild++` 强制重建 NGX 特征。
- **自愈**：游戏的本地化流程会覆盖我们行的标签、甚至把字体换掉，插件在 `OnEnable` / `RefreshVisuals` / `RefreshLocalizedTexts` 三个钩子 + 每 2 s 巡检里拉回；中文字体在页面可见后从模板行同步。
- **可撤除**：所有改动都在游戏目录内，插件与配置可分文件删除，不改动游戏本体数据。

---

## 工作原理（一句话版）

```
NeuroMita.exe
├── dxgi.dll = ReShade 6.8（D3D11 注入）
│   ├── lumenite_Kernel.fx   → 运动向量提供者
│   ├── DLSS5_Feed.fx        → 把 帧+深度+运动向量 打包成 DLSS 契约
│   ├── dlss5-feed.addon64   → DLSS5-Feeder（D3D11 采集 → 私有 D3D12 跑 NGX → 拷回）
│   └── renodx-dlss5.addon64 → 神经渲染消费端（读 ReShade.ini [RenoDX.DLSS5]）
└── BepInEx 6
    └── NeuroMita.DLSS5.dll  → 本插件：往游戏设置菜单里注入 L1/L2 页面，并把开关/参数接到上面两个配置源
```

细节（含所有踩过的坑与偏移级细节）见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

---

## 安装

前置：游戏本体、BepInEx 6（IL2CPP）、ReShade 6.8 add-on 构建、DLSS5-Feeder、RenoDX DLSS5 消费端与两个 NGX 运行时。**这些第三方组件请从各自官方渠道获取**。

1. 把 BepInEx 装进游戏目录（含 `NeuroMita.exe` 的那一层），启动一次游戏，让它生成 `BepInEx\interop\`。
2. 按 DLSS5-Feeder 的官方说明把 ReShade + Feeder + 消费端装进游戏目录（本项目环境用官方一键脚本 `Install-DLSS5Feeder.ps1`，参数 `-Api D3D -Consumer RenoDX`）。
3. 把 `NeuroMita.DLSS5.dll` 放进 `BepInEx\plugins\`。
4. 启动游戏 → `设置 → 图像` → 找到 `DLSS5 神经渲染`。

> 建议同时关掉 ReShade 自己的启动横幅（它在初始着色器编译期间强制绘制，没有任何配置开关）。本项目采用的做法见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md#reshade-启动横幅二进制补丁)。

---

## 使用

- **打开/关闭**：L2 页第一行 `神经渲染总开关`。关 = `mode=0`（惰性），开 = `mode=2`（完整 DLSS 通路）。
- **调参**：Feeder 类参数随时可调、立即生效；RenoDX 类参数（标了 `(重启)` 的行）**改完要重启游戏**才生效。
- **返回**：L2 页的 `返回` 行放在标题正下方（页面底部会被面板裁掉，放在最下面点不到）。
- **注意**：一行标签里显示 `[已停止]` 但开关是"开"的状态，说明喂帧没起来 —— 看门狗会自动尝试恢复，日志在 `dlss5-feed.log`。

---

## 参数（摘要）

完整表（显示名 / 底层键 / 类型 / 范围 / 默认 / 生效方式）见 **[`docs/CONFIG.md`](docs/CONFIG.md)**。

| 显示名 | 底层键 | 位置 | 生效 |
|---|---|---|---|
| 神经渲染总开关 | `mode`（2/0） | `dlss5-feed.cfg` | 即时 |
| 工作分辨率 | `work_resolution` (50–100%) | `dlss5-feed.cfg` | 即时 |
| 锐化 | `work_sharpness` (0–1) | `dlss5-feed.cfg` | 即时 |
| DLSS 预设 | `preset` (0–11) | `dlss5-feed.cfg` | 即时 |
| NR 风格 / 强度 / 局部色调 / 局部结构 / 皮肤结构 / 自动遮罩 / Multi Pass | `NRLookMode` / `NRIntensity` / `NRLocalTone` / `NRLocalStructure` / `NRSkinStructure` / `NRAutoMask` / `NRPasses` | `ReShade.ini [RenoDX.DLSS5]` | **重启游戏** |

> ⚠️ **永远不要**把 `dlss5-feed.cfg` 的 `enabled` 写成 `0`。实测：Feeder 读到 `enabled=0` 之后会连**配置文件监视**一起停掉（`dlss5-feed.log` 从此再无输出），之后写 `enabled=1` 也不会被读到，只能重启游戏。总开关必须走 `mode`。

---

## 配置

| 文件 | 内容 |
|---|---|
| `BepInEx\config\nm.dlss5menu.cfg` | 插件自身：`UI.Label`、`Automation.SelfTest` |
| `<游戏目录>\dlss5-feed.cfg` | Feeder 参数（热重载） |
| `<游戏目录>\ReShade.ini` | ReShade 与 `[RenoDX.DLSS5]` 参数（加载时读一次） |

---

## 故障排查（摘要）

见 [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md)。最常见的三条：

1. **开关点了没反应** → 先看 `dlss5-feed.log` 的字节数/时间戳还在不在增长；不增长说明 Feeder 已被 `enabled=0` 关死（见上面的警告）。
2. **NR 参数调了没变化** → 正常，需要重启游戏；改完关开总开关**不会**让它重读（已实测）。
3. **行的文字变乱/缺字** → 游戏本地化覆盖 + 字体不同步，插件会自动修；若持续存在，附上 `BepInEx\LogOutput.log` 里的 `TEXTPROPS` / `font sync` 行。

报告问题时请附：`BepInEx\LogOutput.log`、`dlss5-feed.log`、`ReShade.log`（见 [CONTRIBUTING.md](CONTRIBUTING.md)）。

---

## 从源码构建

```powershell
# 需要游戏目录（含 NeuroMita.exe 与 BepInEx），因为要引用 BepInEx 生成的 interop 程序集
dotnet build src/NeuroMita.DLSS5/NeuroMita.DLSS5.csproj -c Release `
    -p:GameDir="D:\Games\NeuroMita"
# 产物
# src/NeuroMita.DLSS5/bin/Release/NeuroMita.DLSS5.dll  → 丢进 BepInEx\plugins\
```

`target framework = net6.0`。**CI 无法完整构建**：interop 程序集必须由本机游戏生成，GitHub Actions 里没有游戏 → 工作流只做仓库布局检查并尝试构建，遇到 "interop/GameDir 缺失" 这类预期错误时降级为警告（见 [`.github/workflows/build.yml`](.github/workflows/build.yml) 与 [CONTRIBUTING.md](CONTRIBUTING.md)）。

---

## 许可证

MIT，署名 **NeuroMita DLSS5 contributors**。见 [LICENSE](LICENSE)。

本文档有俄文版：[README.ru.md](README.ru.md)（面向俄语玩家与模组作者，内容与本节一一对应）。

## 致谢与引用

- DLSS5-Feeder — <https://github.com/jlrouzies-fr/DLSS5-Feeder>
- DLSS5-Swapper（生态索引 / MIT） — <https://github.com/rakanki911/DLSS5-Swapper>
- RHI 仓库（`renodx-dlss5`、`nvngx_dlssnr` 发布源） — <https://github.com/RankFTW/rhi-repo/releases>
- LumeniteFX（运动向量提供者） — <https://github.com/umar-afzaal/LumeniteFX>
- ReShade — <https://reshade.me> · 源码 <https://github.com/crosire/reshade>
- BepInEx — <https://github.com/BepInEx/BepInEx> · 文档 <https://docs.bepinex.dev>
- Magpie（DLSSNR 参数对照参考；**屏幕采集**管线，本项目**不采用**其架构） — <https://github.com/SAOG0721/Magpie>
- NeuroMita（游戏本体） — <https://github.com/VinerX/NeuroMita>
