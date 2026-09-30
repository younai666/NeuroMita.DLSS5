# 贡献指南

感谢你愿意帮忙。这个项目的**所有行为都必须在实机验证过**——本项目的历史就是"想当然的推断被实测推翻"的历史
（详见 [CHANGELOG.md](CHANGELOG.md)），所以请按下面的方式来。

---

## 1. 环境准备

| 需要 | 说明 |
|---|---|
| 游戏 | NeuroMita `v2026.09.28`（Unity 6000.3.12f1 / IL2CPP / 64 位） |
| 插件宿主 | BepInEx **6.0.0-be.788**（IL2CPP）。装好后**启动一次游戏**，让它生成 `BepInEx\interop\` |
| DLSS5 栈 | ReShade 6.8（add-on 构建）+ DLSS5-Feeder + RenoDX DLSS5 消费端 + `nvngx_dlss*.dll` |
| GPU | 神经渲染模型**仅 RTX 50** 能创建（RTX 20/30/40 只有 DLAA） |
| SDK | .NET SDK 6.0（`net6.0`） |
| 反汇编（可选） | BepInEx 自带 `BepInEx\core\Iced.dll`，可用于核对 `dxgi.dll` 补丁 |

> 第三方组件请从各自官方渠道获取，仓库里**不放**它们的二进制。

## 2. 构建

```powershell
dotnet build src/NeuroMita.DLSS5/NeuroMita.DLSS5.csproj -c Release -p:GameDir="D:\Games\NeuroMita"
```

- 必须给 `GameDir`（或环境变量 `NEUROMITA_DIR`，或仓库根放一行的 `game.dir`）：
  工程要引用**游戏生成的 interop 程序集**（`BepInEx\interop\*.dll`），这些文件不在仓库里。
- 产物：`src/NeuroMita.DLSS5/bin/Release/NeuroMita.DLSS5.dll` → 丢进 `BepInEx\plugins\`。

### 关于 CI

`.github/workflows/build.yml` 在 ubuntu 上跑，**没有游戏、也就没有 interop**，所以它：
1. 检查仓库布局（必需文件是否齐全）；
2. 尝试构建，但把"interop / GameDir 缺失"这类**预期错误**降级为警告。

也就是说：**CI 绿 ≠ 能编译**。真正的编译验证必须在本机做。若你的改动让 `csproj` 支持
`-p:SkipGameRefs=true`（用桩类型顶掉 interop 引用），可以把该开关接进 workflow，让 CI 真正编译——欢迎提 PR。

## 3. 自检（提交前请跑）

```powershell
# 环境自检：检查各组件是否就位、版本是否匹配、补丁是否在
powershell -ExecutionPolicy Bypass -File tools\Verify-Install.ps1 -GameDir "D:\Games\NeuroMita"
```

自检脚本会核对：ReShade / Feeder / 消费端 / 两个 NGX 运行时 / `dlss5-feed.cfg` 的
`enabled=1` 与 `mode` / 插件 DLL / `dxgi.dll` 的 splash 补丁。

另外建议开一次 `Automation.SelfTest=true`（`BepInEx\config\nm.dlss5menu.cfg`）跑自动验证：
它会做参数存储往返测试、翻转总开关并核对喂帧活动、自动导航到 L1/L2 并在日志里打印每行状态。
**这个开关默认关闭**，因为它会接管菜单。

## 4. 提交规范

- 分支：`fix/<简短描述>`、`feat/<简短描述>`、`docs/<简短描述>`。
- 提交信息（Conventional Commits 风格，一句话说清"为什么"）：

  ```
  fix(switch): drive mode instead of enabled

  enabled=0 makes the feeder stop watching the config file entirely (measured),
  so the switch could never be turned back on without restarting the game.
  ```

- 一个 PR 只做一件事；**行为变更必须附实测证据**（日志片段 / 截图 / 复现步骤）。
- 文档与代码同 PR 更新：改参数就更新 [`docs/CONFIG.md`](docs/CONFIG.md)，
  改机制就更新 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)，用户可见的变化写进 [CHANGELOG.md](CHANGELOG.md)。

## 5. 报告 Bug 需要附上的日志

请**三个都附**（缺少会让排查变成猜）：

| 文件 | 位置 | 用来看什么 |
|---|---|---|
| `LogOutput.log` | `BepInEx\LogOutput.log` | 插件的 patch 是否 OK、`STORE/CLICK/DRIFT/LOCKED/watchdog/font sync/TEXTPROPS` |
| `dlss5-feed.log` | 游戏目录 | 喂帧是否真的在跑（`feature ready` / `600 frames` / `config: …`） |
| `ReShade.log` | 游戏目录 | NGX/神经渲染是否成立（`NR-VERDICT`、`feature 18 evaluation succeeded`、`NR effective settings`） |

再补上：游戏版本（`NeuroMita-Unity\_version.txt`）、驱动版本（`nvidia-smi --query-gpu=driver_version --format=csv,noheader`）、
GPU 型号、以及你**具体点了哪一行、期待什么、实际看到什么**。

## 6. 不要做的事

- 不要把 `dlss5-feed.cfg` 的 `enabled` 写成 `0`（会让 Feeder 停掉配置监视，只能重启游戏救回来）。
- 不要对克隆页面 `SetActive(true)`（会永远画在最上层）。
- 不要在行内"写所有文字组件"（会覆盖滑条数值）。
- 不要提交第三方二进制（ReShade / Feeder / RenoDX / NGX 运行时 / 游戏文件）。
- 不要用 PowerShell 的 `Get-Content`/`Set-Content` 处理含中文的源文件（PS 5.1 默认 ANSI，会把中文写坏；
  本项目在开发中确实因此损坏过一次 `Plugin.cs`）。请用 UTF-8 读写。
