# 美术资产清单

版本 v1.1｜2026-10-30
目录：`Assets/Art/Props/`、`Assets/Resources/Hero/`、`Assets/Resources/ModuleIcons/`

本清单分两类：

1. **项目内自绘**：道具、车厢、箱体、模块小图标，由 `tools/gen_art.py` 与 `tools/gen_array_props.py` 按 16 色色板脚本绘制。
2. **第三方主角形象**：`Assets/Resources/Hero/` 下的鲸鱼娘立绘与动画帧，**全部来自第三方 CC BY-NC-SA 4.0 二创素材**，项目内只做裁剪 / 缩放 / 抽帧等派生处理。

> **主角形象不得自绘、不得 AI 生成**（2026-10-30 第六轮定下的硬约束）。要换主角只能换另一份授权可查的第三方素材。

第三方素材的**完整来源台账**（来源仓库 URL、条目号、许可证原文位置、三级署名链、逐文件的派生参数）记在 `ArtInbox/SOURCES.md`，本文件只登记游戏实际用到的产物。

## 文件清单 —— 项目内自绘道具

| 文件 | 尺寸 | 帧数 | 用途 | 来源 |
| --- | --- | --- | --- | --- |
| box_int_16x16.png | 16×16 | 1 | 整数箱 | 本项目绘制 |
| box_real_16x16.png | 16×16 | 1 | 浮点箱 | 本项目绘制 |
| box_bool_16x16.png | 16×16 | 1 | 布尔翻牌箱 | 本项目绘制 |
| box_char_16x16.png | 16×16 | 1 | 字符铅字箱 | 本项目绘制 |
| box_string_16x16.png | 16×16 | 1 | 字符串卷轴箱 | 本项目绘制 |
| box_merge_16x16_4f.png | 64×16 | 4 | 两箱合并出新箱 | 本项目绘制 |
| switch_32x16_3f.png | 96×16 | 3 | if 道岔，直行、左转、右转 | 本项目绘制 |
| factory_wall_16x16.png | 16×16 | 1 | 循环工厂墙段 | 本项目绘制 |
| factory_roof_16x16.png | 16×16 | 1 | 循环工厂屋顶段 | 本项目绘制 |
| factory_floor_16x16.png | 16×16 | 1 | 循环工厂地面段 | 本项目绘制 |
| factory_chimney_16x16.png | 16×16 | 1 | 循环工厂烟囱 | 本项目绘制 |
| factory_gear_16x16.png | 16×16 | 1 | 循环工厂齿轮 | 本项目绘制 |
| factory_press_48x32_6f.png | 288×32 | 6 | 循环工厂冲压加工 | 本项目绘制 |
| dock_48x32_2f.png | 96×32 | 2 | 输出码头与秤台 | 本项目绘制 |
| array_loco_48x32.png | 48×32 | 1 | 数组列车车头（看板行首） | 本项目绘制 |
| array_car_32x32.png | 32×32 | 1 | 数组列车车厢（一格元素） | 本项目绘制 |
| array_coupler_16x8.png | 16×8 | 1 | 车厢之间的车钩连杆 | 本项目绘制 |
| array_rail_16x8.png | 16×8 | 1 | 铁轨段，横向平铺成整条轨 | 本项目绘制 |
| whale_carry_box_32x32_6f.png | 192×32 | 6 | 鲸鱼娘抱着数字箱搬运 | 基于项目已有 hero_carry.png 二次加工 |
| contact_sheet.png | 880×1980 | 预览 | 资产总览 | 本项目生成 |

## 文件清单 —— 第三方主角形象（派生）

这四张是游戏实际加载的主角素材，全部由 `ArtInbox/SOURCES.md` 里登记的第三方原图派生而来，**项目内未画一笔主角形象**。派生方式只有抽帧、裁包围盒、LANCZOS 缩放，属许可允许的修改，按规则逐条记为「修改」。

| 文件 | 尺寸 | 帧数 | 用途 | 来源条目（见 `ArtInbox/SOURCES.md`） | 处理 |
| --- | --- | --- | --- | --- | --- |
| `hero_full_260x460.png` | 260×460 | 1 | 主菜单 / 关卡选择全身立绘 | 条目 1 `Small-tailqwq/dsh-deep-whale` → `maid-atelier/assets/maid-atelier-maid-left-v5.webp` | 修改：等比缩放 + 透明底整理 |
| `hero_face_96x96.png` | 96×96 | 1 | 右侧 inspector 的头像（`App.cs:694`） | 条目 1 同源的脸部区域 | 修改：裁脸部包围盒 |
| `hero_idle_48x48_8f.png` | 384×48 | 8 | 画布内搬运角色与数据流动画（`App.cs:2531`、`App.cs:2649`） | 条目 8 `PC2005-cloud/dsh-pet` 的 `dshpet_whale-maid_待机呼吸休闲.webm`（经 `FAYER-0610/dafeiyu-vtuber` 原字节转发） | 修改：`-c:v libvpx-vp9 -pix_fmt yuva420p` 抽 241 帧，取 alpha≥24 并集包围盒 (180,59)-(452,331) 扩正方形，等间隔取 8 帧降到 48×48 横排 |
| `hero_happy_48x48_8f.png` | 384×48 | 8 | **已入库但暂无调用点**：`HeroAssets.Happy` 已加载，代码里还没人用，留给通关 / AC 庆祝动画 | 条目 8 同源 `dshpet_whale-maid_女仆屈膝礼仪.webm` | 修改：同上抽帧管线 |

署名链（三级，任何用到主角形象的页面都要带）：① 上善无形 —— 溟月原型（pixiv 62155430）② ZipZipPipe —— 女仆鲸鱼娘设计（pixiv 18604994）③ Small-tailqwq —— 三创再设计；动画帧另加 ④ FAYER-0610 —— 分层与转发 ⑤ PC2005-cloud —— dsh-pet 桌宠原作者。

游戏内署名位置：主菜单底部常驻一行（原型 / 设计 / 三创 + `CC BY-NC-SA 4.0` + 「仅限非商业使用」）。

红线：**DeepSeek 官方鲸鱼 logo 与「DeepSeek」字样一律不得出现在游戏内**；本项目免费、无广告、无内购、不售卖周边，属非商业使用；任何商业化前必须先换形象或取得逐图书面授权。

## 文件清单 —— 模块小图标

`Assets/Resources/ModuleIcons/` 下 54 张，由 `tools/gen_art.py` 按 `美术调研方案.md` 的视觉语言总表绘制，本项目自绘，随项目共享。第九轮补 `queue_new` / `queue_push` / `queue_pop` / `queue_front` / `queue_empty` / `queue_size` 六张（同一套 `_quay` 横向轨道 + `_car` 车厢画法，与栈的竖井图标成对）；生成后需把 `Assets/Art/ModuleIcons/*.png` 复制到 `Assets/Resources/ModuleIcons/`，运行时按 `Resources/ModuleIcons/mod_<key>_16x16` 加载。

## 授权与来源

角色基于溟月二创谱系，按 CC BY-NC-SA 4.0 使用。本项目免费、无广告、无内购、不售卖周边，属于非商业使用。发布时必须保留署名与许可证链接，衍生作品继续以 CC BY-NC-SA 4.0 共享，并明确声明不隶属于 DeepSeek 官方。

其余道具为项目内脚本绘制，随项目以 CC BY-NC-SA 4.0 共享。

## 导入设置

Texture Type 使用 Sprite，Filter Mode 使用 Point，Compression 使用 None，Generate Mip Maps 关闭，Pixels Per Unit 与项目一致。sprite sheet 在 Unity Sprite Editor 中按等宽帧切片，pivot 使用底部中心。像素图不要旋转或非整数缩放。

## 后续替换方向

当前资产是占位版本，重点验证尺寸、帧数和视觉语言。正式版可以按美术调研方案替换为更精细的手绘帧，或引入授权明确的 CC0 工业与列车素材后统一调色。替换时保持文件名、尺寸和帧数不变，避免改代码。
