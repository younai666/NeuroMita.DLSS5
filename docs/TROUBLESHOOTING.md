# 故障排查

排查任何问题前，先按住这个顺序看三份日志（它们分别回答三个不同的问题）：

| 日志 | 回答的问题 |
|---|---|
| `BepInEx\LogOutput.log` | **插件**干了什么：patch 是否装上、值写没写、看门狗有没有动作 |
| `<游戏目录>\dlss5-feed.log` | **喂帧**是否真的在跑：`feature ready` / `600 frames` / `config:` |
| `<游戏目录>\ReShade.log` | **神经渲染**是否成立：`NR-VERDICT` / `feature 18 evaluation succeeded` / `NR effective settings` |

---

## 1. 开关点了没反应

**先看 `dlss5-feed.log` 的字节数/最后修改时间是否还在增长。**

```powershell
Get-Item .\dlss5-feed.log | Select-Object Length, LastWriteTime
Get-Content .\dlss5-feed.log -Tail 5
```

| 现象 | 原因 | 处理 |
|---|---|---|
| 日志完全不增长，最后一行是 `config: enabled=0` | **Feeder 被 `enabled=0` 关死了**：它不仅停止喂帧，还会停掉配置文件监视 | 把 `dlss5-feed.cfg` 的 `enabled` 改回 `1` 并**重启游戏**。永远不要写 `enabled=0`（本插件已经不再这么做） |
| 日志增长，但总开关标签显示 `[已停止]` | 真的没在喂帧 | 看插件日志里 `watchdog:` 的恢复过程；若最终是 `FEED NOT RESUMED after A+B`，重启游戏 |
| 插件日志里没有 `CLICK row='Button DLSS5 enabled'` | 点击没走到插件（行没绑到我们的 id，或被漂移改掉了） | 看同一条日志里有没有 `DRIFT 'Button DLSS5 enabled'`；持续出现就附日志报告 |
| 连点多次只有第一次生效 | 这是**设计**：800 ms 防抖（快速 0/1 翻转会把喂帧会话卡死，实测） | 正常现象 |
| 标签是 `[运行中]` 但画面没有变化 | `mode=2` 生效但喂帧契约没被消费端接住 | 在 `ReShade.log` 里找 `NR-VERDICT`；若 `state=NOT_ENGAGED`，把该行与 `ReShade.log` 一起报上来 |

---

## 2. 调了 NR 参数（NR 强度 / 风格 / 局部…）没有变化

**这是预期行为，不是 bug：需要重启游戏。**

实测结论（见 [CONFIG.md](CONFIG.md#5-为什么-renodx-参数必须重启游戏实测结论)）：
RenoDX DLSS5 只在**加载时**读 `ReShade.ini`，运行中改文件后
`rebuild++`、`mode 0→2` 都不会重读。所以：

1. 在游戏内调好带 `(重启)` 的行；
2. **退出并重新启动游戏**；
3. 想确认是否生效，看 `ReShade.log` 的 `NR effective settings` 一行里的
   `intensity` / `look` / `passes` 等字段。

> Feeder 那几行（总开关 / 工作分辨率 / 锐化 / DLSS 预设）是**即时**的，不需要重启。

---

## 3. 驱动与 GPU 相关

| 症状 | 说明 |
|---|---|
| 校验脚本说 `NVIDIA driver 610.74 is older than 616.56` | 官方口径：DLSS 5 神经渲染要求驱动 ≥ **616.56**。**实测 610.74 也能跑**（RenoDX 走"签名 snippet"路径，`feature 18 evaluation succeeded`、`NR-VERDICT state=ENGAGED`），但建议升级到 616.56+ 与官方兼容矩阵对齐 |
| NGX 报 `0xBAD0000C (OutOfDate)` | 同一个驱动版本问题；实测不影响 RenoDX 路径可用 |
| NGX 报 `0xBAD00001` / `feature 18 create failed` | **GPU 不是 RTX 50**。`nvngx_dlssnr` 310.8.x 的神经模型只在 RTX 50（Blackwell）上创建成功；RTX 20/30/40 只有 DLAA，没有神经渲染 |
| 笔记本双显卡（NVIDIA + 核显） | 确认游戏确实跑在 NVIDIA 上；混合显卡上"游戏在核显、NGX 在独显"的跨适配器路径不成立 |
| 打开面板/设置后显示异常 | ReShade 的 Generic Depth 可能选错了深度缓冲：开 ReShade overlay → Add-ons → Generic Depth 重选；或直接看 `dlss5-feed.log` 的 `Depth probe`（`100% finite` 且 `min≠max` 才正常） |

---

## 4. 界面相关

| 症状 | 原因 | 处理 |
|---|---|---|
| DLSS5 页面**一直浮在所有界面之上**、和主菜单叠着 | 克隆页被强制激活了（历史 bug，已修） | 用当前版本；若仍出现，附 `LogOutput.log` 里的 `page built:` 行 |
| 页面最后几行**看不见也点不到** | 面板可视区在 `y ≈ -670` 处裁掉，参数行多了就会被裁 | 设计上参数上限约 10 行；`返回` 行固定在标题下方。需要更多参数就改配置文件 |
| 某行显示成**别的行的文字**（如 `窗口化`） | 游戏本地化覆盖 | 插件每 2 s 自愈；日志里会有 `DRIFT '<行名>'`。若持续超过几秒仍错，附日志 |
| 文字**缺字**（如 `神经__总开关[__行中]`） | 字体不完整（历史上从"未本地化的行/标题"抓字体导致） | 当前版本在页面可见后从已本地化的行同步字体，日志里有 `font sync: '<字体>' size … applied to N rows`。若仍缺字，把该行 + `TEXTPROPS` 行附上 |
| 每行出现**两份文字**、数值位被标签覆盖 | 历史 bug：曾把行内所有文字组件都写一遍 | 当前版本只写标签对象 |
| **滑条数值自己变成 1 / 5** | 历史 bug：构造期控件把模板值写进了配置 | 当前版本构造期有闸门；如仍出现，检查 `dlss5-feed.cfg` 的值并按需改回 |

---

## 4.1 语言与文字长度

- **语言不对**：菜单文字跟随游戏的语言设置，在游戏里改语言后会立即刷新（日志里会打印 `language detected:` / `language changed ->`）。若个别行没跟上，等 1 秒即可（250 ms 守卫 + 2 秒漂移自愈会补上）。
- **启动瞬间闪过别的语言**：游戏在启动早期会报占位语言（实测 `Ukrainian`），插件用 `LocalizationManager.CurrentRevision` 门控，正常不会显示出来；若日志反复出现 `language changed`，请附日志。
- **文字压到数值/滑条上**：标签启用了 best-fit 自动缩放，正常不会溢出；若仍溢出请截图并注明语言（长语言为俄语/德语）。
- **整页突然变成 `Fatigue reserve`**：那是游戏把模板行的本地化文本写回我们的克隆行，250 ms 内会被改回；若持续存在，请附 `BepInEx\LogOutput.log`。
## 5. 配置文件相关

| 症状 | 原因 |
|---|---|
| 改完 `dlss5-feed.cfg` 完全不生效 | 文件被写成了 **UTF-8 with BOM**（Feeder 不认 BOM 的第一行，实测）。用无 BOM 的 UTF-8 保存 |
| `dlss5-feed.cfg` 里出现某个键的重复行 | 手工编辑造成。Feeder 取第一次出现的值；本插件写值时只改第一处并保持其余不变 |
| `ReShade.ini` 的改动被"改回去" | ReShade 关闭 overlay/退出时会保存自己的配置（样式等）。本插件只写它不接管的键（`[OVERLAY]`/`[RenoDX.DLSS5]`），一般不会被覆盖 |

---

## 6. ReShade 启动横幅还在

横幅是 ReShade 自己的 splash（**没有任何配置开关**，见 [ARCHITECTURE.md](ARCHITECTURE.md#7-reshade-启动横幅二进制补丁)）。
本项目用一个字节的跳转补丁去掉它：

- 补丁位置：`dxgi.dll` 文件偏移 `0xD08E4`，`0F 84 E7 09 00 00` → `E9 E8 09 00 00 90`；
- 想还原：把备份 `dxgi.dll.bak` 覆盖回去（或重新安装 ReShade）；
- ReShade 升级后偏移会变，需要重新定位（方法见 ARCHITECTURE）；
- 若补丁偏移写错一个字节（落点 `…ED0`），游戏会**启动即崩**——这也是为什么改前必须备份。

---

## 7. 完全卸载

1. 删掉 `BepInEx\plugins\NeuroMita.DLSS5.dll`（以及 `BepInEx\config\nm.dlss5menu.cfg`）。
2. 想彻底回到原版：删掉游戏目录里的
   `dxgi.dll`、`dlss5-feed.addon64`、`renodx-dlss5.addon64`、`nvngx_dlss*.dll`、
   `reshade-shaders\`、`ReShade.ini`、`ReShadePreset.ini`、`dlss5-feed*.cfg/.log`、`ReShade*.log`，
   以及（如果不再需要 BepInEx）`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`dotnet\`、`BepInEx\`。
3. 本插件**不改动**游戏的任何 `.assets` / `GameAssembly.dll` / `global-metadata.dat`，所以删除文件即可完全还原。

---

## 8. 报告问题时请附

- `BepInEx\LogOutput.log`
- `dlss5-feed.log`
- `ReShade.log`
- 游戏版本：`NeuroMita-Unity\_version.txt`
- 驱动与 GPU：`nvidia-smi --query-gpu=name,driver_version --format=csv,noheader`
- 你点了哪一行、期待什么、实际看到什么（有截图最好）

模板见 [.github/ISSUE_TEMPLATE/bug_report.md](../.github/ISSUE_TEMPLATE/bug_report.md)。
