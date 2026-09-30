# 拉取请求

## 这个 PR 做了什么

<!-- 一句话说清"为什么"，不只说"改了什么" -->

## 类型

- [ ] 修复（bug fix）
- [ ] 新功能（feature）
- [ ] 文档（docs）
- [ ] 重构（refactor）
- [ ] 其它：

## 实测证据（**行为变更必填**）

<!--
本项目的历史就是"推断被实测推翻"的历史，所以行为变更必须给证据：
- 游戏内日志片段（BepInEx\LogOutput.log / dlss5-feed.log / ReShade.log）
- 或截图
- 或复现步骤 + 前后对比
-->

```
（日志片段）
```

- 测试环境：游戏版本 / GPU / 驱动 / ReShade / Feeder / RenoDX 版本
- 是否开了 `Automation.SelfTest`：

## 自检

- [ ] 本机 `dotnet build src/NeuroMita.DLSS5/NeuroMita.DLSS5.csproj -c Release -p:GameDir=…` 通过
- [ ] 跑过 `tools\Verify-Install.ps1 -GameDir …` 且除已知项外无失败
- [ ] 在游戏内验证过改动（不是只看日志推断）

## 文档

- [ ] 改了参数 → 更新了 `docs/CONFIG.md`
- [ ] 改了机制 → 更新了 `docs/ARCHITECTURE.md`
- [ ] 用户可见变化 → 写进了 `CHANGELOG.md`

## 风险 / 已知限制

<!-- 例如：只在 RTX 50 上验证过；需要重启游戏才生效；依赖第三方某个版本 -->

## 关联 Issue

Closes #
