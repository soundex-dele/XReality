# 0.3.35 通用主播 · 嘴唇形变验证

Windows x64 本地开发构建，未生成发布 ZIP。`python Tests/presenter_screenshots.py` 通过：35 张 1280×720 截图，379 次窗口采样未发现可见窗口。证据见 `Builds/QA-RealistDev-vonw_1y9/evidence.json`，完成标记 `XR_PLAYER_OFFSCREEN_OK 0.3.35`。

- `XR_PLAYER_LIP_ARTICULATION_OK`：合成输入验证圆唇可在同一下颌开度下明显改变嘴形；抿嘴能减小下颌开度并保证牙齿与下颌同步。相冲突的表情不会满权重叠加，恢复待机时两种形变退出。
- 人工对照 `LiveJawOnly.png`、`LiveLipRound.png` 和 `LiveLipPress.png`。圆唇收窄了嘴角；抿嘴截图不再出现首次试烘焙中明显外露的齿缝。并非所有开口、角度与表情组合均已验证。
- 离线人像面捕与校准、眨眼、视线、跟踪漏检、绿幕与隐私回归仍通过。未用实体摄像头、麦克风、真实键盘或 OBS，未做长测；真人讲话时的识别和口型同步质量仍待验证。
