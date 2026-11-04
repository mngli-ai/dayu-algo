# 美术资产清单

版本 v1.2｜2026-11-04
目录：`Assets/Art/`（Props / Sprites / Personas / ModuleIcons / Audio / Font）与对应的 `Assets/Resources/` 运行时副本，另加 `Assets/Resources/Backdrop/`、`Assets/Resources/Hero/`

本清单分五类：

1. **项目内自绘**：道具、车厢、箱体、模块小图标、彩带与特效，以及音效（`tools/gen_art.py`、`tools/gen_array_props.py`、`tools/gen_sfx.py` 脚本绘制 / 合成）。
2. **第三方主角形象**：`Assets/Resources/Hero/` 下的鲸鱼娘立绘与动画帧，**全部来自第三方 CC BY-NC-SA 4.0 二创素材**，项目内只做裁剪 / 缩放 / 抽帧等派生处理。
3. **第三方各家 AI 拟人立绘**（第十七轮新增形象位、第十八轮换成搜集素材）：`Assets/Resources/Personas/` 29 张，其中 24 张来自 ZipZipPipe《AI娘化》系列成图。
4. **第三方像素背景**（第十八轮新增）：`Assets/Resources/Backdrop/` 5 张，来自 CraftPix.net 的 OGA-BY 3.0 像素海底图层。
5. **第三方字库栅格化的位图图集**：`Assets/Resources/atlas.{png,json}` 的字形来自 TakWolf《Fusion Pixel Font》（SIL OFL 1.1），见下面「字体图集」一节。

> **角色与背景美术不得自绘、不得 AI 生成**（2026-10-30 对主角定下的硬约束，2026-11-04 第十八轮玩家明令扩大到一切角色与背景：「太丑了，以后不要再自己画了，必须去网上找别人画好的素材」）。要换形象只能换另一份授权可查的第三方素材，转换脚本只允许抠底 / 裁切 / 降采样这类不改设计的变换。搜集渠道、筛选口径与登记流程见 `ArtInbox/SOURCES.md` §九「美术工作流」。

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

## 文件清单 —— 关卡背景条（第十六轮新增，**第十八轮起停用**）

**状态**：这四处程序化横带自第十八轮起不再上屏——玩家反馈「都是毛刺，看不出来是什么东西」，运行时贴图已换成下面「画布背景五层」一节搜集来的第三方像素图层（`Backdrop.Defs` 只加载 `Backdrop/uw_*`）。文件暂留，因为 `Backdrop.TextureKeyFor()` 仍返回 `bg_dock` / `bg_factory` / `bg_shelf` / `bg_deep` 这四个键，只是**当章节配色键用**，不再当贴图键用。

由 `tools/gen_art.py` 的 `gen_backdrops()` 生成（`python tools/gen_art.py bg`），只用 16 色色板 + 全透明，alpha 仅取 0 / 255。源图在 `Assets/Art/Props/`，运行时副本在 `Assets/Resources/Props/`（`gen_backdrops()` 用 `write_bytes` 同步，逐字节一致）。

| 文件 | 尺寸 | 原用途（现仅作 `Backdrop.TintFor` 的章节键名） |
| --- | --- | --- |
| bg_dock.png | 160×80 | 序章其余小节的码头吊臂 + 货轮 + 木栈桥（默认回落键） |
| bg_factory.png | 160×80 | 「条件分支」「循环」的厂房屋脊 + 水塔 + 烟囱 + 传送带 |
| bg_shelf.png | 160×80 | 「数组」小节（L3/L4）的货架排 + 箱格 |
| bg_deep.png | 160×80 | 「栈」「队列」及第一章的深海渐层带 |

## 文件清单 —— 画布背景五层（第十八轮新增，第三方像素素材）

来源：CraftPix.net《Underwater World Pixel Background》，许可证 **OGA-BY 3.0**（署名 CraftPix.net，允许商用与再分发）。台账见 `ArtInbox/SOURCES.md` §九 条目 16。原图 5 张各 576×324 RGBA 真像素，`tools/gen_backdrop.py` **原字节拷贝**、未修改，落到 `Assets/Resources/Backdrop/`（无 `Assets/Art/` 副本）。运行时 `Backdrop.cs` 按镜像瓦片平铺、五档横向视差 `.04 / .08 / .12 / .20 / .30` 加三条纵向视差，并按章节乘色。

| 文件 | 源文件 | 尺寸 | 层内容与实测 |
| --- | --- | --- | --- |
| uw_water.png | `1.png` | 576×324 | 全幅不透明水体渐变，4 色，顶锚铺满视口当「天空」 |
| uw_fish.png | `2.png` | 576×324 | 鱼群，alpha 包围盒 (51,126)-(517,235)，29 色，自带横向漂移 |
| uw_rocks.png | `3.png` | 576×324 | 礁石剪影，占 y130..324，5 色，底锚远景 |
| uw_sand.png | `4.png` | 576×324 | 沙底，占 y243..324，5 色，底锚中近景 |
| uw_coral.png | `5.png` | 576×324 | 珊瑚前景，占 y169..324，14 色，底锚近景 |

采样口径由 `Assets/Editor/ImportSettings.cs` 统一：Point 过滤、不压缩、关 mipmap。同批搜集但**未入 `Assets/`** 的备用素材（ansimuz《Warped Ocean View》、CraftPix《Underwater World Parallax》）只在 `ArtInbox/SOURCES.md` §九 条目 17 / 18 登记。

## 文件清单 —— 音效（第十六轮新增，程序化合成，无第三方音源）

`tools/gen_sfx.py` 用 numpy 现算带限方波 / 三角 / 噪声 + A-D 包络，**19 条 cue**（第十七轮追加 `confetti` / `wa`）落到 `Assets/Resources/Audio/<cue>.wav`（44100Hz·16bit·单声道，实测合计 288.5KB，最长 0.49s，峰值实测 0.68、脚本上限 0.8，已减 DC、首尾淡入淡出）。噪声用固定种子，`--check` 会比对磁盘文件与现算结果，防止「改了脚本没重新生成」。运行时由 `SoundSystem` 池化 6 路 + 每 cue 去抖播放，cue 名单与顺序由 `SoundCueTests` 钉住。

## 文件清单 —— 字体图集（第三方字库栅格化）

画面里的每一个汉字都是一张位图格，运行时不走 Unity 文本渲染：`Assets/Resources/atlas.png`（384×616 RGBA）+ `atlas.json`（单元格 12×14、32 列 × 44 行共 1408 个槽位、实填 **1402** 格），加载器 `Assets/Scripts/Text/GlyphAtlas.cs`，上屏由 `Assets/Scripts/UI/PixelText.cs` 逐格绘制。

| 件 | 来源 | 许可 |
| --- | --- | --- |
| `Assets/Art/Font/fusion-pixel-12px.ttf`（6,378,960 字节，`sha256` `f2cbbbca…`） | TakWolf《Fusion Pixel Font》Proportional Zh-hans，https://github.com/TakWolf/fusion-pixel-font | **SIL Open Font License 1.1**，原文已入库 `docs/licenses/fusion-pixel-12px-OFL.txt` |
| `atlas.{png,json}`（`Assets/Art/Font/` 与 `Assets/Resources/` 两份逐字节一致） | `tools/gen_art.py font` 用 Pillow `truetype(..., 12)` 把当轮上屏字符栅格化 | 位图字形属字体的使用结果；字体本体的再分发义务由上面的 OFL 原文满足 |

- **字体本体不进玩家包**：实测 `Build/` 下 `*.ttf` 命中 0 个，玩家包内只有生成的位图图集。
- **改动玩家可见中文必须重生成图集**：`python tools/gen_art.py font` → 把 `Assets/Art/Font/atlas.{png,json}` 拷到 `Assets/Resources/` → 用 `Evidence/<轮>/font_missing.py` 逐字核对上屏字符 100% 有字形格，否则新字在画面里是空格。
- 台账见 `ArtInbox/SOURCES.md` §十一 条目 20（第十八轮收尾自查补登记：这条此前只存在于初始提交，没有任何来源与许可记录）。

## 文件清单 —— 第三方主角形象（派生）

这四张是游戏实际加载的主角素材，全部由 `ArtInbox/SOURCES.md` 里登记的第三方原图派生而来，**项目内未画一笔主角形象**。派生方式只有抽帧、裁包围盒、LANCZOS 缩放，属许可允许的修改，按规则逐条记为「修改」。

| 文件 | 尺寸 | 帧数 | 用途 | 来源条目（见 `ArtInbox/SOURCES.md`） | 处理 |
| --- | --- | --- | --- | --- | --- |
| `hero_full_260x460.png` | 260×460 | 1 | 主菜单 / 关卡选择全身立绘 | 条目 1 `Small-tailqwq/dsh-deep-whale` → `maid-atelier/assets/maid-atelier-maid-left-v5.webp` | 修改：等比缩放 + 透明底整理 |
| `hero_face_96x96.png` | 96×96 | 1 | 右侧 inspector 的头像（`App.cs:694`） | 条目 1 同源的脸部区域 | 修改：裁脸部包围盒 |
| `hero_idle_48x48_8f.png` | 384×48 | 8 | 画布内搬运角色与数据流动画（`App.cs:2531`、`App.cs:2649`） | 条目 8 `PC2005-cloud/dsh-pet` 的 `dshpet_whale-maid_待机呼吸休闲.webm`（经 `FAYER-0610/dafeiyu-vtuber` 原字节转发） | 修改：`-c:v libvpx-vp9 -pix_fmt yuva420p` 抽 241 帧，取 alpha≥24 并集包围盒 (180,59)-(452,331) 扩正方形，等间隔取 8 帧降到 48×48 横排 |
| `hero_happy_48x48_8f.png` | 384×48 | 8 | **已入库但暂无调用点**：`HeroAssets.Happy` 已加载，代码里还没人用，留给通关 / AC 庆祝动画 | 条目 8 同源 `dshpet_whale-maid_女仆屈膝礼仪.webm` | 修改：同上抽帧管线 |

署名链（三级，任何用到主角形象的页面都要带）：① 上善无形 —— 溟月原型（pixiv 62155430）② ZipZipPipe —— 女仆鲸鱼娘设计（pixiv 18604994）③ Small-tailqwq —— 三创再设计；动画帧另加 ④ FAYER-0610 —— 分层与转发 ⑤ PC2005-cloud —— dsh-pet 桌宠原作者。

游戏内署名位置：`App.cs CreditLines`（主菜单落款取前两行，说明书「素材来源」页整份列出）。第十七轮底栏瘦身之后署名**不再常驻关卡底栏**——CC BY-NC-SA 要求的是「按媒介惯例署名」，不是「必须常驻游戏主界面」。

红线：**DeepSeek 官方鲸鱼 logo 与「DeepSeek」字样一律不得出现在游戏内**；本项目免费、无广告、无内购、不售卖周边，属非商业使用；任何商业化前必须先换形象或取得逐图书面授权。

## 文件清单 —— 各家 AI 拟人立绘（第十七轮新增形象位，第十八轮换成第三方成图）

**状态变化**：第十七轮这 29 张由 `tools/gen_personas.py` 程序自绘；第十八轮玩家否决自绘后，除 `dayu_*` 之外的 24 张**全部换成搜集来的第三方成图派生**。自绘脚本加了闸门——不带 `--legacy` 直接拒绝运行，防止误跑刷回旧样式；转换走新增的 `tools/gen_personas_from_photo.py`：抠白底（只删与边界连通的白，白裙白袜保住）→ 最大连通域定前景包围盒 → 按包围盒高固定比例裁头（0.30）/ 半身（0.62）→ LANCZOS 降采样 + 硬 alpha。项目内未画一笔角色本身。

| 文件（每个 key 同名一组） | 尺寸 | 帧 | 来源 |
| --- | --- | --- | --- |
| `dragon_{face,idle,wave,talk}.png` | 32×32 / 64×80 | 头像 + 3 姿势 | ZipZipPipe《AI娘化》pixiv 148186519 p03 + BV1tE9XBbErS 截帧（`SOURCES.md` §十 条目 19） |
| `claude_*` | 同上 | 同上 | 同系列 p02 + 17s 截帧 |
| `gemini_*` | 同上 | 同上 | 同系列 p04 + 11s 截帧 |
| `qianwen_*` | 同上 | 同上 | 同系列 p12 + 53s 截帧 |
| `grok_*` | 同上 | 同上 | 同系列 p07 + 29s 截帧 |
| `doubao_*` | 同上 | 3 帧同图 | 仅公开一张（bilibili opus 1236803362705375305），不换姿势 |
| `dayu_{face,idle,wave,talk,eat}.png` | 32×32 / 64×80 | 头像 + 4 动作 | **未被替换的那 5 张**：仍是第十七轮 `tools/gen_personas.py` 的自绘件（`git show --name-only` 本轮恰好只改 24 张 persona，`dayu_*` 一个没动）。大肥鱼是本项目原创角色，用深蓝短发 + 大白围裙 + 饭碗 + 鲸尾，刻意避开女仆鲸鱼娘的签名式构图，无第三方依赖、不占署名链（见 `SOURCES.md` §八 条目 15） |

合计 29 张，`Assets/Art/Personas/` 与 `Assets/Resources/Personas/` 逐字节一致（7 张 32×32 头像 + 22 张 64×80 立绘帧）。加载器 `Assets/Scripts/UI/Personas.cs`；一致性、尺寸、缺帧由 `Assets/Editor/PersonaAssetTests.cs` 判红。许可口径照原仓库 `NOTICE.md`：作者开放分享、允许再创作，但**不授予标准许可证，DeepSeek 二次设计明确 CC BY-NC-SA 4.0 不可商用** → 本项目非商业可用，商用红线见 `ArtInbox/SOURCES.md` §头部。

署名落点：`App.cs CreditLines` 第三行 + 说明书「素材来源」页。

## 文件清单 —— 模块小图标

`Assets/Resources/ModuleIcons/` 下 30 张（`Assets/Art/ModuleIcons/` 同数同字节），由 `tools/gen_art.py` 按 `美术调研方案.md` 的视觉语言总表绘制，本项目自绘，随项目共享。第九轮补 `queue_new` / `queue_push` / `queue_pop` / `queue_front` / `queue_empty` / `queue_size` 六张（同一套 `_quay` 横向轨道 + `_car` 车厢画法，与栈的竖井图标成对）；生成后需把 `Assets/Art/ModuleIcons/*.png` 复制到 `Assets/Resources/ModuleIcons/`，运行时按 `Resources/ModuleIcons/mod_<key>_16x16` 加载。

## 授权与来源

逐文件的来源页 URL、附件直链、许可证原文位置、派生参数与实测像素数据都在 `ArtInbox/SOURCES.md`（§一~§七 主角谱系、§八 自绘件、§九 背景与工作流、§十 各家拟人立绘、§十一 字体）。四条来源链：

| 来源 | 许可 | 可否商用 | 署名落点 |
| --- | --- | --- | --- |
| 主角「溟月」鲸鱼娘（`Hero/*`、`dayu` 之外的主角出场帧） | CC BY-NC-SA 4.0，三级链 上善无形 / ZipZipPipe / Small-tailqwq（动画帧另加 FAYER-0610、PC2005-cloud） | **否**，原型作者 2026-09-30 明确未授权任何商业销售 | 主菜单落款 + 说明书「素材来源」页（同一份 `App.cs CreditLines`，落款取前两行、说明书整份列出） |
| 各家 AI 拟人立绘（`Personas/` 24 张） | ZipZipPipe《AI娘化》同人成图，作者开放分享、允许再创作，原收录仓库不授予标准许可证；其中 DeepSeek 二次设计 CC BY-NC-SA 4.0 | **否** | `App.cs CreditLines` 第三行 |
| 画布背景五层（`Backdrop/uw_*.png`） | **OGA-BY 3.0**，CraftPix.net | 是（署名即可） | `App.cs CreditLines` 第五行「画布背景为第三方像素素材：CraftPix.net《Underwater World》· OGA-BY 3.0 署名」 |
| 字库（`Assets/Art/Font/fusion-pixel-12px.ttf` → `atlas.{png,json}`） | **SIL OFL 1.1**，TakWolf《Fusion Pixel Font》；许可证原文入库 `docs/licenses/fusion-pixel-12px-OFL.txt` | 是 | README + `SOURCES.md` §十一（OFL 不要求 UI 署名） |

本项目免费、无广告、无内购、不售卖周边，属非商业使用。发布时必须保留署名与许可证链接；含 NC / NC-SA 的部分衍生后继续以相同方式共享，并明确声明不隶属于任何 AI 厂商官方。**要商用必须先换掉主角与各家拟人形象**（背景与字体这两路本身可商用）。

其余道具、车厢、箱体、模块小图标、彩带特效、19 条音效、「大肥鱼」这只像素形象均为项目内脚本绘制 / 合成，随项目以 CC BY-NC-SA 4.0 共享。

## 导入设置

`Assets/Editor/ImportSettings.cs` 的 `Apply()` 统一施加：不缩放（`npotScale = None`）、不压缩、关 mipmap、可读写、`maxTextureSize` 2048。覆盖范围是 `Resources/atlas.png` 与 `Art/Font/atlas.png`、`Art/Sprites`、`Art/Props`（按 Sprite 型）、`Resources/Props`、`Resources/Backdrop`、`Art/ModuleIcons` + `Resources/ModuleIcons`、`Resources/Hero`；主角立绘是第三方高清插画不是像素图，这一路单独用 **Bilinear**（点采样会出锯齿），其余为 **Point**。sprite sheet 在 Unity Sprite Editor 中按等宽帧切片，pivot 使用底部中心。像素图不要旋转或非整数缩放。

**一处不一致，本轮登记未改**：`Assets/{Art,Resources}/Personas/` 没进 `ImportSettings.Apply()` 的清单，走的是 Unity 默认导入（meta 实测 `filterMode: 1` 即 Bilinear + 默认压缩）。主菜单把 64×80 立绘放大到约 3.5×，本轮所有认可的截图都是这个双线性观感，改成点采样会露硬格——要统一这条得连截图与发布包一起重做，本轮没动，记为下一轮待办（要么把 Personas 纳入并显式保留 Bilinear，要么改点采样并重拍重发）。

## 后续替换方向

角色与背景一律走「搜集现成授权素材」这条路（`ArtInbox/SOURCES.md` §九 的渠道与筛选口径），**不再自绘、不用 AI 生成**（2026-11-04 玩家明令）。道具与图标这一批仍是程序自绘，若要替换：保持文件名、尺寸和帧数不变，避免改代码；新素材必须先在本台账登记来源与许可。第十六轮那批程序化背景横带（`Props/bg_*.png`）已停用，只作章节配色键名保留，日后大版本可连同 `gen_art.py` 的 `gen_backdrops()` 一起清掉。
