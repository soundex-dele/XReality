# 0.3.14 无窗口验收记录

- 测试对象：`Builds/XReality-0.3.14-Windows-x64-portable-preview.zip`；实算 SHA-256 与同名校验文件一致：`bffe0259032d83c7f25df1527199bd27b1b30505bdad0f12dace35ea115adef0`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --download-screenshots` 退出码 0。运行解包后的 Windows x64 Player，生成 26 张 1280×720 PNG；3,564 次窗口可见性采样均未发现可见窗口。
- 截图及日志：`Builds/QA-Packaged-8yvi51zk/`。主进程记录 `XR_PLAYER_OFFSCREEN_OK 0.3.14`；跨进程宠物存档见 `player-pet-restore.log`。

| 场景 | 本次结果 |
|---|---|
| 首页、宠物房间、重启恢复 | 首页按钮回调进入两个场景，相机保持关闭；猫打盹动耳、清醒蹭手、注视和追球，隔离测试档跨进程恢复。 |
| 化身和绿幕 | 本地测试人像推理通过；模拟丢脸后回到待机、重新检测后恢复，绿幕隐藏控制面板并可恢复。没有启用实体摄像头。 |
| 模型安装 | 手动安装覆盖缺失、损坏拒绝和有效模型就绪；慢速持续传输、停滞、无数据和总时限通过逻辑断言。 |
| 下载和重试 | 模拟连接异常时验证可取消退避、两次失败后回退；真实联网场景完成下载启动、取消清理、重新发起。本次联网时有一次进度预算到期，自动重试后下载并通过哈希校验。 |

未验证系统鼠标或键盘、实体相机、30 分钟实时运行或第二台干净 Windows 机器。不使用 OBS。仍为预览版，完整发布验收见 `QA-WINDOWS.md`。
