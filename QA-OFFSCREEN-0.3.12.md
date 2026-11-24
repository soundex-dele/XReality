# 0.3.12 无窗口验收记录

- ZIP SHA-256：`0ab1348b4f89481d75473ba9f0d98786290d371441c6d73082b2505d773f0a27`。
- 命令：`python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --download-screenshots`。
- 证据：`Builds/QA-Packaged-00hqbr_r/`；两次启动、24 张 1280×720 PNG，1,681 次采样未检测到可见窗口。

隔离故障注入确认：第一次临时连接错误进入可取消的退避等待；取消后未执行下一次尝试。两次连接错误后不再重试，无残留文件，界面提供浏览器或手动安装。见 `LiveModelAutoRetry.png`、`LiveModelRetryExhausted.png`、`XR_PLAYER_RETRY_SCREENSHOTS_OK`。独立的真实请求随后通过下载、手动取消清理、重试和校验安装流程。真请求未发生自动重试，注入分支并不冒充物理网络重现。其余回归包括离线推理、绿幕面板隔离、宠物动作与重启恢复。

仍未验收物理输入、真实摄像头连续 30 分钟运行、第二台 Windows；未启用 OBS。此包仍是预览版。
