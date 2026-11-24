# 0.3.17 无窗口验收记录

- 对象：`Builds/XReality-0.3.17-Windows-x64-portable-preview.zip`；SHA-256 实算与旁签一致，`1aa57abf8e43e24c13f591adfd34abcdc6309962a0a305c240ff3861b19637df`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 退出码 0。23 张 1280×720 PNG，1,567 次窗口采样未发现可见窗口。`Builds/QA-Packaged-hz6m97z5/` 记录首页路由和展示、宠物互动与跨进程恢复、离线推理、绿幕隔离、模拟网络重试及分段校验。
- `python Tools/project.py packaged-screenshots --resume-screenshots` 第一次未通过。HTTP 连接在取得数据前失败，未到达强制中断条件；1,297 次窗口采样均未发现可见窗口。失败证据 `Builds/QA-Packaged-_mm27ehw/`。
- 第二次触发真下载中断，保留 393,216 字节，下一请求收到 HTTP 206、实收 2,814,305 / 3,365,380 字节，但触发 60 秒上限，未安装。2,503 次窗口采样均未发现可见窗口。续传和失败截图及日志见 `Builds/QA-Packaged-udruj8rt/`。
- 回退到普通联网截图的独立运行在下载前终止：恢复后的控件布局与文字都正确，但按钮过渡中的色差超过像素阈值。见 `Builds/QA-Packaged-uq8nea5b/`；不计作联网传输失败。

未测试实体相机、系统鼠标键盘、30 分钟真人追踪，或第二台干净 Windows 机器。未启用 OBS。


