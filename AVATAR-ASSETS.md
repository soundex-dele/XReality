# 写实主播素材与再生成

默认主播是一位通用 3D 半身角色，不代表特定人物。首页和直播间共享同一套资产，只有直播间包含面捕组件。皮肤以胸部和颈部两个关节做权重过渡，眼球与头发跟随头部，眉毛表面随皮肤一起形变。MediaPipe 驱动 `BlinkLeft`、`BlinkRight`、`JawOpen`、`SmileLeft`、`SmileRight`、`BrowLeft`、`BrowRight`、`LipRound`、`LipPress`；牙齿与皮肤同步张嘴。左右嘴角和眉毛独立工作，校准会扣除静息表情。眼睑应用更快的闭合响应，头部追踪目标限制在当前半身角色的颈部范围内。半身是取景范围，不做手势或躯干动捕。

## 来源与分发

原始素材是 MakeHuman / MPFB 团队发布的 **CC0 1.0 Universal** 图形数据。分发附许可与来源说明：`Assets/StreamingAssets/Notices/MakeHuman-CC0-1.0.txt`、`Assets/StreamingAssets/Notices/Provenance.txt`。不分发上游应用或插件代码，也不使用主播照片。

- MPFB 图形数据快照：`mpfb2-20261122.zip`（2.0.18），SHA-256 `e8103012013c829f6c9a0972b7ccd0a2f56b0596aa6f717d535e0bea79cd8910`。
- MakeHuman System Assets：`makehuman_system_assets_cc0.zip`，SHA-256 `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107`。

## 开发

日常用 Unity 直接生成场景；`Assets/XReality/Presenter` 已收录烘焙网格和纹理，不用安装 Blender、MPFB 或联网下载素材。生成的 Unity 网格和三个场景位于 `Assets/XReality/Generated/StorybookV19`。直播场景将现有眼球网格拆成左右眼，在眼球曲面拟合的旋转中心上驱动；首页仍使用同一静态双眼网格。

仅在需要重烘焙时，用带 NumPy 和 Pillow 的 Python 执行：

```powershell
python Tools/bake_presenter.py <MPFB归档> <MakeHuman-CC0素材归档>
```

脚本先检验两个原始归档的 SHA-256，再拟合衣物、裁切网格、生成 UV、骨权重和表情位移。修改网格数据后应使用新的生成目录版本，避免复用缓存的 Unity Mesh。无窗口预览用 `python Tools/project.py screenshots --unity <Unity.exe>`，输出中性、半眨眼、左右独立闭眼、张嘴、微笑、单侧嘴角、单侧抬眉和转头绿幕截图。Windows 开发目录编译后可运行 `python Tests/presenter_screenshots.py`，使用已有本地测试素材做短时无窗口回归，不生成 ZIP。
