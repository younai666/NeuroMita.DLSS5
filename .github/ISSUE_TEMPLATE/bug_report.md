---
name: Bug report
about: 报告一个可复现的问题
title: "[Bug] "
labels: bug
---

<!--
请先读 docs/TROUBLESHOOTING.md：
- 「开关点了没反应」大多是 dlss5-feed.cfg 里被写过 enabled=0（Feeder 会连配置监视一起停掉，只能重启游戏）
- 「NR 参数改了没变化」是预期行为，需要重启游戏
-->

## 环境

| 项 | 值 |
|---|---|
| 游戏版本（`NeuroMita-Unity\_version.txt`） |  |
| 插件版本 |  |
| BepInEx 版本 |  |
| ReShade 版本 |  |
| DLSS5-Feeder 版本 |  |
| RenoDX 消费端版本（`ReShade.log` 里的 build） |  |
| GPU / 驱动（`nvidia-smi --query-gpu=name,driver_version --format=csv,noheader`） |  |
| Windows 版本 |  |
| 是否使用了 `dxgi.dll` splash 补丁 | 是 / 否 |

## 复现步骤

1.
2.
3.

## 期望结果

## 实际结果

## 日志（三个都请附上）

- [ ] `BepInEx\LogOutput.log`
- [ ] `dlss5-feed.log`
- [ ] `ReShade.log`

关键片段（可直接粘贴）：

```
（插件日志里的 patch / STORE / CLICK / DRIFT / watchdog / font sync 行）
```

```
（dlss5-feed.log 的尾部，尤其是最后一条 config: 行）
```

```
（ReShade.log 里的 NR-VERDICT / NR effective settings 行）
```

## 补充

- 截图（含 L1/L2 页面或异常界面）：
- 是否改动过 `dlss5-feed.cfg` / `ReShade.ini` 的其它键：
- 其它：
