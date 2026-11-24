# 0.3.10 无窗口验收记录

预览 ZIP 的 SHA-256：`a30943cd84b149baae02f0aa7b0cd2641f9bd9d793a0c574f13b387e43f01f19`。

- `python Tools/project.py screenshots --unity <Unity.exe>`：新版宠物房间与动作关键帧、化身表情离屏回归通过，见 `Builds/unity-offscreen-expressions.log`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction`：解包后两次运行通过，19 张截图，1,441 次检查未检测到可见窗口；见 `Builds/QA-Packaged-4y11zdlf/`。
- 加 `--download-screenshots` 的在线回归两次都未完成。第二次日志明确记录连接被重置；面板进入可重试或手动安装的错误状态。见 `Builds/QA-Packaged-fwx9zqs6/player.log` 与 `Builds/QA-Packaged-r7t8m3d5/player.log`。手动安装回归通过，0.3.9 的在线成功记录不能替代 0.3.10 的在线复验。

不含摄像头、OBS、桌面输入或第二台电脑之实测，仍是预览版。
