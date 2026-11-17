# 霓虹巨兽 NEON BEHEMOTH

PICO 头显上的混合现实拆城游戏：扫描你的房间，一座霓虹海滨城市从桌面上长出来，你用自己的双手当巨兽——拍楼、推楼、抓车、挡导弹、合掌发能量波，在核打击落下之前断掉电站、拆掉封印塔。只用手势和眼动，不用手柄；中英双语。

- 引擎：Unity 6000.5.5f1，URP，IL2CPP，arm64-v8a
- 设备：PICO 4 Ultra（透视、手势追踪、眼动追踪、空间扫描）
- 包名 `com.monstercity.game`
- 安装包在本仓库的 [Releases](../../releases)
- 供云端打包的安卓导出工程在 [neon-behemoth-android](https://github.com/laodou050505-spec/neon-behemoth-android)

## 打开工程

1. 安装 Unity 6000.5.5f1（带 Android Build Support）。
2. `Packages/manifest.json` 里 PICO SDK 是本机路径引用（`com.bytedance.pico.xr`），请改成你机器上 PICO Unity Integration SDK 6.0.0 的 `XR` 目录。
3. 以 Android 平台打开工程，场景是 `Assets/_Monster/Scenes/Main.unity`（打包时由脚本重新生成）。

## 打包

菜单或脚本调用 `Monster.EditorTools.MonsterBuild.BuildApk()`：会写入图标、启动画面、包名、版本号（`0.1.月日`），输出到 `Builds/`。

## 目录

| 路径 | 内容 |
|---|---|
| `Assets/_Monster/Scripts/Runtime` | 城市生成（布局、楼体模块、街道、车流、合批、远近模型） |
| `Assets/_Monster/Scripts/Runtime/Game` | 玩法逻辑（纯逻辑层 `MonsterGame` / `Enemies`）、表现层 `GameView`、手势输入、界面、结局 |
| `Assets/_Monster/Scripts/Editor` | 打包、导入规则、场景体检 |
| `Assets/_Monster/Shaders` | 三渲二城市、水面、护盾、天幕、受伤反馈等着色器 |
| `Assets/_Monster/Resources` | 运行时按名字加载的模型、贴图、音频、字体 |
| `Tools` | Blender 程序化建模脚本（楼、车、船、军用载具、巨兽之手）、贴图合成、音频合成、品牌图合成 |
| `Tools/editor_checks` | 编辑器里跑的自查脚本（难度模拟、弹药不穿楼体检、界面截图等） |

## 素材来源

- 模型：Blender 脚本程序化生成；贴图：脚本绘制与生成式模型出图后处理。
- 音乐与音效：`Tools/audio_synth.py` 程序合成，原创。
- 广播语音：语音合成后做喇叭处理。
- 字体：思源黑体、站酷快乐体、站酷庆科黄油体、龙藏体、Bangers、Permanent Marker、Outfit，均为 SIL OFL 授权。
