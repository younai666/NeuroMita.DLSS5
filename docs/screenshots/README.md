# 截图目录

| 文件 | 说明 |
|---|---|
| `settings-l2.png` | 游戏内 L2 参数页（实机截图，示例中游戏语言为乌克兰语，可看到文字随游戏语言变化、长文案自动缩小不溢出） |
| `ingame-dlss5.png` | 游戏内效果：DLSS 5 神经渲染开启后的画面 |

## 还缺一张

`settings-l1.png` —— 图形页里的 **L1 入口行**（标签为 `DLSS5 神经渲染` / `DLSS5 Neural Rendering`）。

补图方法：游戏内进入 `设置 → 图像`，对准那一行截图（建议 1920x1080 全窗口），存成 `docs/screenshots/settings-l1.png`，
然后在 `README.md` 与 `README.zh-CN.md` 的"效果截图 / Скриншоты"表里加一行即可。

## 注意

- 截图**不进发布包**（`package.ps1` 会把 `docs/screenshots/*.png` 从 zip 里剔除），只放在仓库里供 README 引用，这样发布包保持 ~90 KB。
- 提交前请压缩到 1 MB 以内（`pngquant` / `oxipng` 或游戏内 F12 截图后的压缩版均可）。