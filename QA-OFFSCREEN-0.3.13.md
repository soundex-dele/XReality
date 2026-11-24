# 0.3.13 无窗口验收记录

- 预览 ZIP SHA-256：`d1e33f8d86b23f2decd099fd6df0fae376499ee57f3a880191a42cbaa6fb21fa`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 通过：解包后共 23 张 1280×720 PNG，1,595 次窗口采样未发现可见窗口。证据目录 `Builds/QA-Packaged-ubnp7bub/`。
- 首页按钮使用真实的按钮回调，分别进入宠物房间、直播间。相机在两个入口默认不会启用。测试程序负责从宠物房间返回首页，因此不代表用户返回键已实测。`Home.png` 和 `HomeReturned.png` 为实际首页控件，不是静态样稿。
- 通过：宠物动作及跨进程存档、离线面捕与恢复、绿幕控制面板隔离、手动模型安装、模拟异常下的下载自动重试和回退。
- `python Tools/project.py packaged-screenshots --download-screenshots` 未通过：单独的在线下载流程重试后仍未完成，相关日志 `Builds/QA-Packaged-cjx84h8t/player.log`。手动模型安装仍通过，0.3.12 的线上记录不替代此包实测。

不含实际鼠标或键盘、摄像头连续 30 分钟运行及第二台电脑。未启用 OBS。
