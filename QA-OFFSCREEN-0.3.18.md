# 0.3.18 无窗口验收记录

- Windows x64 预览包：`Builds/XReality-0.3.18-Windows-x64-portable-preview.zip`；实算 SHA-256 与校验文件一致：`bb240746bfcaff5d0c33ddf672558b137f18f1a7be2c2b37d866ff6b36462000`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 通过：23 张 1280×720 PNG，1,498 次窗口采样没有发现可见窗口。证据目录 `Builds/QA-Packaged-pew5_hdw/`，含跨进程宠物存档、离线面捕、绿幕隔离、分段校验、模拟重试和新的下载总时限断言。
- 0.3.17 实测一次 HTTP 206 仍受 60 秒总时限截断；0.3.18 将每次下载总时限改为 90 秒，保留连续 24 秒无新字节的提前退出、一次自动重试、严格范围与文件校验。
- `python Tools/project.py packaged-screenshots --resume-screenshots` 退出码 0：12 张 1280×720 PNG，1,840 次窗口采样没有发现可见窗口。先验证取消清理，再主动中断一次真实下载，保留 397,356 字节；下一请求返回 HTTP 206，拼接后 SHA-256 匹配并安装，临时文件均清理，摄像头未启用。日志有 `XR_MODEL_DOWNLOAD_RESUMED` 和 `XR_PLAYER_RESUME_SCREENSHOTS_OK`，证据在 `Builds/QA-Packaged-_ni7ckd4/`。

未做实体摄像头、30 分钟真人面捕、实际鼠标键盘或第二台 Windows 电脑测试。不使用 OBS。

