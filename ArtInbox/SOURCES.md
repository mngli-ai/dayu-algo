# ArtInbox 素材台账（SOURCES.md）

维护说明：本表登记 ArtInbox 内所有第三方素材的来源与授权。任何素材进入 `Assets/` 前必须在此有对应条目，且发布物须按 §4.5 模板署名（见《大肥鱼形象考据与美术资产指南》）。
登记日期：2026-10-30。核验方式：Python 3.13 + Pillow 12.1.1 实测像素数据 + 逐张目视复核。

许可证速记：
- **CC BY-NC-SA 4.0** = 署名 + 非商业 + 相同方式共享（https://creativecommons.org/licenses/by-nc-sa/4.0/）
- 所有条目均为**非 AI 自绘**（本项目未生成任何新形象），但注意谱系上 ZipZipPipe 女仆版母版本身由 GPT Image 2 辅助生成，属原作者行为，与本仓库无关。
- 通用红线：素材上**不得出现 DeepSeek 官方鲸鱼 Logo / 文字商标**。带 Logo 的条目已明确标记「禁止入游戏」。
- **2026-11-03 补充（商用口径）**：原型作者上善无形于 2026-09-30 公开声明**未授权任何第三方商业销售**，CC BY-NC-SA 中的 NC（非商业）据此更严：只要上架 / 内购 / 打赏 / 任何形式的收款，一律按商用处理，本表全部条目即不可用。发布前若涉收款，须先换掉鲸鱼娘形象或取得书面授权。
- 游戏内文案口径（同次补充）：各 AI 产品名一律只以**角色名/昵称**出现在故事文本里（大肥鱼、龙娘、大小姐、文昭、豆包……），不做标题 logo、不用官方图标字体、不配官方 logo、不暗示「官方合作」，关卡名与按钮文案不挂这些词；主菜单与页脚已写明「各 AI 形象均为同人虚构」。

---

## 一、可直接用于主角形象（授权明确，优先）

### 1. stdw_maid-atelier_maid_left_v5.webp ★推荐主立绘
- 来源仓库：https://github.com/Small-tailqwq/dsh-deep-whale（`maid-atelier/assets/maid-atelier-maid-left-v5.webp`）
- 许可：美术 **CC BY-NC-SA 4.0**（仓库内 `maid-atelier/LICENSE-ARTWORK` + `NOTICE`，NOTICE 已随拷为 `stdw_maid-atelier_NOTICE.txt`）。署名链：①上善无形（溟月原型，pixiv 62155430）②ZipZipPipe（女仆鲸鱼娘设计，pixiv 18604994）③Small-tailqwq（三创再设计）
- 下载日期：2026-10-30（git clone，HTTPS 公开）
- 修改：**未修改**（原字节）
- 实测：WEBP RGBA，1122×2048→实际 1122×2019，透明区 47.5%，不透明包围盒 (8,8)-(1116,2014)，主色 #303060/#f0f0f0/#486090（深蓝白裙+奶油白围裙）。目视：女仆头饰+呆毛+鲸类头鳍耳+大鲸尾+白围裙（围裙上为社区小鲸插画，**非** DeepSeek 商标），无 DeepSeek 文字/Logo。可直接当透明底立绘用。

### 2. stdw_maid-atelier_booterror_right.webp（备选全身立绘）
- 来源：同上仓库 `maid-atelier/assets/boot-error/right.webp`
- 许可：CC BY-NC-SA 4.0（同上 NOTICE；本图为仓库作者用 OpenAI imagegen 依上述双角色参考图再生成的三创，署名链不变）
- 下载日期：2026-10-30；修改：**未修改**
- 实测：WEBP RGBA，627×1254，透明区 69.7%，包围盒 (172,207)-(621,1202)。目视：深蓝白女仆长裙+白围裙（口袋有小鲸刺绣）+呆毛+鲸尾+抱书姿势，无 Logo/文字。

### 3. stdw_maid-atelier_booterror_left.webp（溟月一创风全身，非女仆装）
- 来源/许可/日期/修改：同条目 2
- 实测：RGBA 627×1254，透明区 57.2%，包围盒 (14,48)-(512,1217)。目视：白衬衫+深蓝马甲长裙+怀表链，鲸鳍耳+呆毛+大鲸尾，无 Logo。风格偏一创溟月，可做剧情/过场形象。

### 4. stdw_maid-atelier_maid_right_v6.webp / stdw_maid-atelier_maid_right_v7.webp（左立绘的另一侧版本）
- 来源/许可/日期/修改：同条目 1（`assets/maid-atelier-maid-right-v6/v7.webp`）
- 实测：v6 RGBA 1394×2649 透明 49.7%；v7 RGBA 1077×2048 透明 49.7%。目视同款女仆装。

### 5. stdw_maid-atelier_icon_delighted_cutout.png / determined_cutout.png / sleepy_cutout.png / delighted512.png（Q 版表情半身）
- 来源：同上仓库 `maid-atelier/assets/icons/`
- 许可：CC BY-NC-SA 4.0（NOTICE 注明：Small-tailqwq 提供表情原图，本地脚本抠除浅色背景生成，沿用署名链）
- 下载日期：2026-10-30；修改：**未修改**（仓库方已做过抠图，本仓库未再改）
- 实测：delighted_cutout RGBA 1254×1254 透明 38.0%；determined_cutout RGBA 912×981 透明 10.3%（几乎满幅）；sleepy_cutout RGBA 1254×1254 透明 47.1%；delighted512 RGBA 512×512 透明 40.2%。目视：Q 版女仆鲸鱼娘大头，呆毛+头饰+鲸鳍耳清晰，无 Logo。适合 UI 头像/表情（32×32 重绘源）。

### 6. fayer_whale-maid_composite_neutral.png ★推荐 UI 半身立绘
- 来源仓库：https://github.com/FAYER-0610/dafeiyu-vtuber（`overlay/avatar/packs/whale-maid/`，29 个分层 PNG + `rig.json`）
- 许可：美术 **CC BY-NC-SA 4.0**（仓库 `ATTRIBUTION.md`：原型溟月@上善无形、女仆设计@ZipZipPipe；代码 MIT 不覆盖美术）。母版 1024×1536 由仓库方本地修复分层，无伪造放大。
- 下载日期：2026-10-30（git clone）
- 修改：**是（程序合成）**——按 rig.json 的 rect/z 顺序用 Pillow 把 29 层合成为 1024×1536 中性表情全身（胸像+裙上段），未重绘任何像素。若入正式资源，建议保留合成脚本并同步署名 FAYER-0610（三创）。
- 实测：RGBA 1024×1536，透明区 38.2%，包围盒 (9,288)-(1015,1536)，主色奶油白+深蓝（#f0f0f0/#304890）。目视：女仆头饰+呆毛+蓝渐变长发+鲸鱼发夹（社区小鲸，非 DeepSeek Logo）+深蓝白女仆装，无 DeepSeek 文字/Logo。

### 7. fayer_whale-maid_layer_body / headdress / ahoge / whaleClip .png（原始分层件）
- 来源/许可/日期：同条目 6；修改：**未修改**
- 实测：body RGBA 692×525 透明 23.1%；headdress RGBA 861×536 透明 85.6%；ahoge RGBA 233×171 透明 72.5%；whaleClip RGBA 107×120 透明 45.8%。留作二次合成/换表情用。

### 8. dshpet_whale-maid_待机呼吸休闲.webm / 吃白饭.webm / 女仆屈膝礼仪.webm（+ 同名 frame_*.png）★Q 版动画基准
- 来源链：https://github.com/PC2005-cloud/dsh-pet（`assets/webm/`，桌宠原作者动画）→ 经 FAYER-0610/dafeiyu-vtuber `overlay/avatar/packs/dsh-whale/clips/` 原字节转发（其 ATTRIBUTION 注明"未做修改"）
- 许可：形象 **CC BY-NC-SA 4.0**（署名链同上）；dsh-pet 素材条款「允许开源使用、禁止商用、二创注明原作者 GitHub 地址」；pack.json 标注 `commercialUse: false`
- 下载日期：2026-10-30；修改：webm **未修改**；frame_*.png 为 ffmpeg（`-c:v libvpx-vp9 -pix_fmt rgba`）抽取的首帧
- 实测：webm 640×360 透明 VP9；抽帧 RGBA 640×360，透明区约 83%，角色包围盒约 210×267（吃白饭 (211,63)-(422,324)）。目视：最流行的圆滚滚 Q 版二头身女仆鲸鱼娘（对应"这个刀子真甜"风格谱系），呆毛+头饰+围裙小鲸+大鲸尾，无 Logo/文字。**做 48×48 像素动画时的比例与轮廓基准**。dsh-whale 包内共 90+ 个动作 clip，需要更多动作时从 FAYER 仓库直接取。

### 9. fayer_dafeiyu-vtuber_poster.jpg / expressions.jpg（演示图，仅参考）
- 来源/许可：同条目 6 仓库 `docs/media/`；许可：CC BY-NC-SA 形象 + MIT 代码；修改：**未修改**
- 实测：poster JPEG 1280×720 无透明（直播界面截图，含中文 UI 文字）；expressions JPEG 1400×670 无透明（14 表情网格）。只作形象核对参考，不直接入游戏。

---

## 二、授权待人工核验（萌百图，Cloudflare 挡住许可页）

萌百 HTML 与文件页被 Cloudflare 拦截，匿名 API 仅开放 `prop=images`，`imageinfo/parse` 返回 Unauthorized；图片经 `Special:FilePath` 可正常下载（storage.moegirl.org.cn）。**以下条目许可状态未定，人工打开文件页确认前不得进 Assets/。**

### 10. moegirl_女仆装鲸鱼娘.png（与条目 1 同图，白底版）
- 来源页：https://zh.moegirl.org.cn/File:女仆装鲸鱼娘.png（实际文件 https://storage.moegirl.org.cn/moegirl/commons/5/56/女仆装鲸鱼娘.png）
- 作者：页面未抓取到，待人工核验（构图与 stdw 条目 1 完全一致，应为同一女仆鲸鱼娘立绘的白底原稿）
- 下载日期：2026-10-30；修改：**未修改**
- 实测：名义 RGBA 但**透明区 0.0%（纯白底不透明）**，1152×2048，主色 #f0f0f0/#303060。要用需先抠白底。既然条目 1 有授权明确的透明版，此图仅作交叉验证，**建议弃用**。

### 11. moegirl_DeepSeek娘OS1.jpg / moegirl_大肥鱼的打工小曲.jpg
- 来源页：https://zh.moegirl.org.cn/File:DeepSeek娘OS 1.jpg 、https://zh.moegirl.org.cn/File:大肥鱼的打工小曲.jpg
- 实测：JPEG 无透明；OS1 675×1200、打工小曲 850×1133。目视：均为**带大量对白文字的漫画**（OS1 作者水印 Pixiv@康売火火火；打工小曲为 Q 版女仆鲸鱼娘四格）。含文字，不能直接当立绘；打工小曲可作 Q 版姿势参考。
- 结论：仅参考，不入游戏。

## 三、明确禁止入游戏（含 DeepSeek 商标/文字）

### 12. moegirl_DeepSeek娘设定图.jpg
- 来源页：https://zh.moegirl.org.cn/File:DeepSeek娘设定图.jpg；作者水印 Pixiv@康売火火火 UID56521010
- 实测：JPEG 1200×1738 无透明。**画面含 DeepSeek 官方鲸鱼 Logo（左上大图）、"deepseek" 文字、工牌上 Logo** → 商标风险，任何情况不可用，只留作社区形象谱系参考。

### 13. moegirl_Deepseek4f.jpg
- 来源页：https://zh.moegirl.org.cn/File:Deepseek4f.jpg
- 实测：JPEG 1200×1613 无透明。多格漫画，含 "DeepSeek V4 Flash" 标题文字、DeepSeek 鲸鱼 Logo 发饰、ChatGPT 徽标，且构图不宜。**禁止使用**。

## 四、授权含糊，不建议采用

### 14. 1190fasheqi_dafeiyu-pet_正面.png / 侧面.png / 背面.png
- 来源仓库：https://github.com/1190fasheqi/dafeiyu-pet（`sprites/`）
- 许可：仓库整体标 **MIT**（含素材），但 README 未给美术署名链，仅称「DeepSeek V4 Pro 二创形象」。角色本体版权属于上善无形/ZipZipPipe，仓库方无权以 MIT 授权他人形象 → **美术授权存疑**。
- 下载日期：2026-10-30（codeload tarball）；修改：**未修改**
- 实测：正面 RGBA 255×340 透明 31.6%；侧面 222×340 透明 31.4%；背面 253×340 透明 28.7%。目视：Q 版女仆鲸鱼娘三视图（呆毛+头饰+围裙小鲸+鲸尾），无 Logo，风格与条目 8 接近。**若要用必须先向仓库作者或原 UP 主确认，当前仅作构图参考。**

---

## 五、已验证但无可用美术的仓库（排查记录）

| 仓库/URL | 结论 |
| --- | --- |
| https://github.com/Alterssss/dsh-bili-whale | 树内无任何 .png/.jpg/.webp 美术；LICENSE=MIT（仅代码），NOTICE 不涉及形象素材。无素材可用。 |
| https://github.com/cloverzz/dsh-bili-scroll | **404，仓库不存在**（文档§五该条记录有误）。 |
| https://github.com/liyupi/dafeiyu-cleaner | LICENSE=MIT（仅字体单列）。README 声明「鲸鱼娘清道夫为矢量绘制，形象设定参考 Neko3000/deepseek-whalechan 的文字角色」→ 是其自绘角色，**不是女仆鲸鱼娘形象**，不满足"必须用第三方鲸鱼娘"的要求，未下载。 |
| https://github.com/Neko3000/deepseek-whalechan | 有全套女仆鲸鱼娘参考图（standard-character-portrait.webp、各姿势 isolated webp），但 `THIRD-PARTY-NOTICES.md` 白纸黑字声明：**"Unless an asset has a separate explicit license or permission, no blanket MIT or CC grant is made for these images"**，且承认无逐图授权映射 → 授权含糊，按任务规则排除。 |
| https://github.com/xiaoshihou514/dsh-desktop-pet | THIRD_PARTY_NOTICES：精灵图由另一插件 vlln/whale-girl 分发，本仓库不含素材。未继续深挖（已有更优条目 1/6/8）。 |
| https://github.com/PC2005-cloud/dsh-pet | codeload 直连被中途截断（gzip 校验失败）；其素材已确认经 FAYER 仓库 dsh-whale 包原字节获得（条目 8），无需重复下载。 |
| 萌娘百科条目页 https://zh.moegirl.org.cn/DeepSeek娘 | HTML 与 imageinfo/parse API 均被 Cloudflare/权限拦截，仅 `prop=images` 与 `Special:FilePath` 可用。 |

## 六、推荐结论

1. **主立绘（剧情/UI 全身）**：条目 1 `stdw_maid-atelier_maid_left_v5.webp` — 全身女仆装、透明底、CC BY-NC-SA 4.0 三级署名链完整可查，授权可用性最好。
2. **半身表情立绘（对话框）**：条目 6 `fayer_whale-maid_composite_neutral.png`（+ 分层件可换表情），授权明确，1024×1536 与项目 UI 缩放兼容。
3. **48×48 像素化基准**：条目 8 的 dsh-pet Q 版动画帧（83% 透明、角色净高约 267px，降到 48px 为 5.6:1 非整数——重绘时按项目规范逐像素重建，不做非整数缩放）。
4. 全部使用场景须：页脚署名（上善无形 / ZipZipPipe / Small-tailqwq / FAYER-0610 / PC2005 按实际用到的条目列）+ 许可证链接 + 「非 DeepSeek 官方」免责声明；衍生像素图同协议 CC BY-NC-SA 4.0 发布；任何商业化前先换形象或取得书面授权。

---

## 七、已落盘到 `Assets/Resources/Hero/` 的衍生件（2026-10-30）

生成脚本：`tools/cut_hero.py`（只做抽帧 / 裁切 / 缩放，不重绘任何一笔）。
重新生成：`python tools/cut_hero.py`。

| 落盘文件 | 上游条目 | 处理 | 显示尺寸 |
| --- | --- | --- | --- |
| `hero_idle_48x48_8f.png` | 条目 8 `dshpet_whale-maid_待机呼吸休闲.webm` | `-c:v libvpx-vp9 -pix_fmt yuva420p` 抽 241 帧（保留 VP9 旁路 alpha），取 alpha≥24 的并集包围盒 (180,59)-(452,331) 扩成正方形，等间隔取 8 帧 LANCZOS 降到 48×48 横排 | 画布内搬运角色 44×44 |
| `hero_happy_48x48_8f.png` | 条目 8 `dshpet_whale-maid_女仆屈膝礼仪.webm` | 同上，裁切 (184,62)-(456,334) | 预留（通关/鼓励） |
| `hero_face_96x96.png` | 条目 6 `fayer_whale-maid_composite_neutral.png` | 按母版比例取头部窗口 (0.20W, 0.18H)-(0.82W, 0.61H)，等比贴入 96×96 | 弹窗头像 64×64 |
| `hero_full_260x460.png` | 条目 1 `stdw_maid-atelier_maid_left_v5.webp` | 按不透明包围盒等比贴入 260×460 | 主菜单立绘 184×300 |

补充说明：
- 上游条目 8 的 webm 用默认参数抽帧会丢掉 alpha（背景压成纯黑），必须显式 `-c:v libvpx-vp9 -pix_fmt yuva420p`。
- 48×48 是从 272px 净高一次性 LANCZOS 降采样，属于**制作期**缩放；运行时按 44/48 双线性显示，`ImportSettings` 对 `Assets/Resources/Hero` 用 Bilinear（其余像素资源仍 Point）。条目 §六.3 说的「禁止非整数缩放」针对的是车厢等横向拼接件，角色单精灵不受限。
- 游戏内署名见 `App.cs` 的 `BuildFooter()`；旧自绘搬运鲸鱼 `whale_carry_box_32x32_6f.png` 已从 `PropAssets` 摘掉加载（文件仍在 `Assets/Art/Props`、`Assets/Resources/Props`，未被任何代码引用）。
- `ArtInbox/whale-maid/` 是 FAYER 分层包的重复下载残留（仅 1 个文件、被占用未能删除），与条目 7 同源，可手工删除。

---

## 八、程序自绘原创形象（2026-11-03 追加，非第三方素材）

> 本节只追加，不改动上方 §一~§七 的任何既有条目与结论。

### 15. `Assets/Art/Personas/**` + `Assets/Resources/Personas/**`（7 位 AI 拟人的像素立绘/头像，共 29 张 PNG）★自绘原创
> **【2026-11-04 第十八轮：本节六家 AI 的自绘件已被 §十 的第三方素材整批取代】**
> 玩家反馈「角色画得太丑，以后不要再自己画了，必须去网上找别人画好的素材」。
> `dragon/claude/gemini/qianwen/grok/doubao` 的 24 张 PNG 已换成 §十 条目 19 的转换产物；
> 本节保留的口径只适用于仍在用的 `dayu_*`（大肥鱼，来自 §一条目 1/6/8 的鲸鱼娘素材）。
> 自绘生成器 `tools/gen_personas.py` 已加闸门：不带 `--legacy` 直接拒绝运行，防止误跑刷回旧样式。

- 来源：**本仓库自产**。由 `tools/gen_personas.py`（Python 3 + Pillow，确定性整数格绘制，固定色板）逐像素生成，脚本即源文件，可重复运行 `C:/ProgramData/anaconda3/python tools/gen_personas.py` 完整复现同一批图。
- 许可：**程序自绘原创，无第三方素材依赖，不含任何网络下载件、不临摹任何官方或社区成图** → 这部分美术随项目非商业发布时**无需额外署名链，也不受 CC BY-NC-SA 约束**。若日后本项目要商用，本节条目本身可用（仍属原创），但见下方「整体限制」。
- 排查依据（为什么走自绘而不是找现成像素素材）：检索过 OpenAI / Google / DeepSeek 等各家品牌资产页与 itch.io / Kenney 等 CC0 像素包 —— **没有任何一家发布过可商用的官方拟人像素素材**；CC0 包里只有通用像素件、不含 AI 角色；社区的「AI 娘」图全部是二创（CC BY-NC-SA 或授权不明），萌娘百科的说明文字本身也是 BY-NC-SA。故只能自绘原创演绎。
- 规避措施：角色设定参考 `Evidence/r16/llm-persona-research.md` 的**文字**性格与口癖（自绘仅取材于公开文字描述，不参照任何成图构图）。尤其主动避开 §一/§六 的女仆鲸鱼娘签名式组合——本节的「大肥鱼」用的是**深蓝短发 + 大白围裙 + 饭碗 + 鲸尾**，**没有**女仆头饰、头鳍耳、蝴蝶结、蓝渐变长发、深蓝白女仆裙、玛丽珍鞋，不与条目 1/6/8 的构图同构。
- 落盘与一致性：同名同字节双份存放（`Assets/Art/Personas/` 为源件，`Assets/Resources/Personas/` 为 Unity 运行时读取），清单 `Assets/Art/Personas/personas_meta.json`；逐字节一致由 `Evidence/r17/persona_selfcheck.py`（sha256）校验。
- 规格：立绘帧 64×80、头像 32×32，七角色严格同尺寸；统一使用项目 16 色板（同 `Assets/Scripts/UI/Theme.cs` / `tools/gen_art.py` 的 PALETTE），1px 描边 `#38515B`，alpha 仅 0/255，无抗锯齿，**画面内不含任何文字**（中文由位图字体渲染）。
- 运行时入口：`Assets/Scripts/UI/Personas.cs`（`Personas.Face/Frame/ActionsFor/Has/KeyForDisplayName/Keys`），加载失败只 `Debug.LogWarning` 一次并返回 null，不抛异常。
- **整体限制（重要，指回既有条目）**：本节条目自身是原创、可自由使用；但**本仓库整体**仍含 §一~§七 的第三方 CC BY-NC-SA 素材（`Assets/Resources/Hero/` 的四件衍生件即来自条目 1/6/8），所以项目整体口径不变——**在任何商业化前，仍须按 §六.4 与 §头部「2026-11-03 补充（商用口径）」换掉鲸鱼娘形象或取得书面授权**；页脚署名（`App.cs` 的 `BuildFooter()`）与「各 AI 形象均为同人虚构」免责声明继续保留。自绘形象与第三方形象同框时，页脚署名义务以第三方条目为准。

---

## 九、画布动态背景：免费像素美术资产（2026-11-04 追加，r18 #146）

> 背景：玩家反馈 r17 程序化生成的背景条「都是毛刺，丑丑的」，并明确要求**不要再自己画，去搜集现成素材**。本节即背景美术的搜集结果与美术工作流记录。

### 美术工作流（本轮定稿，后续素材照此走）
1. **搜集渠道**（按本机可达性实测）：opengameart.org（OGA，可达，许可证标注齐全，首选）→ raw.githubusercontent.com（可达，需逐仓库核授权）→ 萌百 storage.moegirl.org.cn（可达，多为 BY-NC-SA）。**不可达/不用**：itch.io、kenney.nl、pixiv.net（本机被墙）；任何「免费」但授权含糊的站点不碰。
2. **筛选口径**：只要真像素图（低色数、无抗锯齿、整数格绘制）；许可证只收 CC0 / OGA-BY / CC-BY（署名即可，含商用权）；BY-NC-SA 素材沿用 §头部商用红线；**禁止 AI 生成图与自绘**（玩家明令）。
3. **登记**：下载原件先落 `ArtInbox/`（不入库），在本表登记来源页 URL、附件直链、作者、许可证、下载日期、是否修改、像素实测数据。
4. **落盘**：处理脚本（本轮 `tools/gen_backdrop.py`）只做拷贝/裁切/缩放/量化，不重绘；写入 `Assets/Resources/` 后由 `ImportSettings.Apply()` 统一点采样、不压缩、无 mipmap。
5. **署名**：需要署名的许可证（OGA-BY / CC-BY）在游戏内页脚 `App.cs BuildFooter()` 落一行；NC 素材同时受 §头部商用口径约束。

### 16. `oga_underwater-world-pixel-bg/1..5.png` ★在用（画布背景五层）
- 来源页：https://opengameart.org/content/underwater-world-pixel-background （OGA 提交者 CraftPix.net，2026-11-04 curl 实测 HTTP 200）
- 附件直链：https://opengameart.org/sites/default/files/underwater_world_pixel_background.zip
- 许可：**OGA-BY 3.0**（署名 CraftPix.net，允许商用与再分发，https://opengameart.org/oga-by-30-faq）
- 下载日期：2026-11-04；修改：**未修改**（`tools/gen_backdrop.py` 原字节拷成 `Assets/Resources/Backdrop/uw_{water,fish,rocks,sand,coral}.png`，md5 与源一致）
- 实测：五张均 576×324 RGBA 真像素。1.png 全幅不透明水体渐变（4 色）；2.png 鱼群（alpha 包围盒 (51,126)-(517,235)，29 色）；3.png 礁石剪影（y130..324，5 色）；4.png 沙底（y243..324，5 色）；5.png 珊瑚前景（y169..324，14 色）。运行时按镜像瓦片平铺、五档横视差 .04/.08/.12/.20/.30、章节乘色，属显示变换，不改素材字节。
- 署名落点：`App.cs BuildFooter()` 增「背景美术 CraftPix.net（OGA-BY 3.0）」一行。
- 包内 `COUPON.pdf/png` 是 CraftPix 折扣券广告，**未拷入 Assets/**。

### 17. `oga_warped-ocean-view/`（备用，未入 Assets）
- 来源页：https://opengameart.org/content/warped-ocean-view ；作者 Luis Zuno（@ansimuz）
- 许可：包内 `Ocean View Files/public-license.txt` 明示「personal or commercial projects / modify / re-distribute / credit not required」（OGA 页标 CC0）
- 下载日期：2026-11-04；状态：**备用未使用**。水面海景风格与「算法深海」主题不合，留作日后章节扩充候选。

### 18. `oga_underwater-world-parallax/`（备用，未入 Assets）
- 来源页：https://opengameart.org/content/underwater-world-parallax-backgrounds （CraftPix.net）
- 附件直链：https://opengameart.org/sites/default/files/Underwater-World-Parallax-Game-Backgrounds-PNG.zip
- 许可：OGA-BY 3.0（同条目 16）
- 下载日期：2026-11-04；状态：**备用未使用**。四套 1920×1080 分层大图的尺寸与色数都超出需要，条目 16 的 576×324 五层像素颗粒与卡片图标更同档。

### 排查记录（不可用来源）
- OGA `underwater-fantasy-pixel-art-environment`：页面无附件，原文件外链 itch.io（本机被墙）→ 弃用。
- 自绘路线：r17 `tools/gen_art.py` 生成的程序化背景条（`Assets/Resources/Props/bg_*.png`）被玩家否决，文件暂留但 `Backdrop.cs` 已不再读取；后续大版本可连同 gen_art.py 的 bg 函数一起清掉。

---

## 十、各家 AI 拟人立绘：第三方同人成图（2026-11-04 追加，r18 #145）

> 背景：玩家看到 r17 自绘的六只像素娘后明令「太丑，以后不要再自己画了，必须去网上找别人画好的素材，
> 这几个娘都很热门，应该很容易搜到」。本节即该指令的落实结果，并取代 §八 条目 15 的六家自绘件。

### 搜集口径（沿用 §九 工作流，角色美术这一支）
1. 渠道：pixiv / bilibili 原站本机被墙，改走**已把这些成图原字节收录并逐项登记许可的 GitHub 仓库**
   `shuaizenge/ai-girls`（raw.githubusercontent.com 可达）。仓库 `NOTICE.md` 核验日期 2026-10-01。
2. 只取**画师本人公布的成图**，不做任何自绘、不做 AI 生成、不改画角色本身；
   转换脚本只做抠白底 + 裁切 + 降采样（与 §九 第 4 步同口径：变换不改设计，重绘才算）。
3. 大文件（5–12 MB）从 raw.githubusercontent 直连常被掐断，用 `curl -C -` 断点续传补全，
   并以 PIL 能整图无截断解码 + 与 GitHub API 报告的 size 逐字节相符为验收条件。

### 19. `zipzippipe_*.{jpg,png}`（六家 AI 拟人立绘 + 五张另一姿势截帧）★在用
- 画师：**ZipZipPipe** — Pixiv https://www.pixiv.net/users/18604994 / Bilibili https://space.bilibili.com/4168597 （UID 4168597）
- 原始作品页：pixiv《AI娘化》 https://www.pixiv.net/artworks/148186519 （高清立绘，每角色一页）；
  bilibili《大AI和小AI们》 https://www.bilibili.com/video/BV1tE9XBbErS/ （同一套设计的另一姿势，视频截帧）；
  豆包（Seed）那张来自 https://www.bilibili.com/opus/1236803362705375305 。
- 收录仓库文件（本地 `ArtInbox/personas/`，未入库）与 sha256：

  | 本地文件 | 仓库内路径 | 尺寸 | sha256 |
  | --- | --- | --- | --- |
  | `zipzippipe_dragon.jpg` | `characters/gpt/adult/zipzippipe-pixiv-148186519-p03.jpg` | 3072×5504 | `c47948cd25efcf7ad4a5179288c8452445d57d6dfc379fb27ab6e809a6970157` |
  | `zipzippipe_claude.jpg` | `characters/claude/adult/zipzippipe-pixiv-148186519-p02.jpg` | 3072×5504 | `6fb3d6568ccd23cea1437226947276ab052bcdd47d6b7d55d8b5ca148c779782` |
  | `zipzippipe_gemini.jpg` | `characters/gemini/adult/zipzippipe-pixiv-148186519-p04.jpg` | 3072×5504 | `1353cf80c62b1841f0b59671a693aef945a297c586c9ad32f1a36efc5e3e15ab` |
  | `zipzippipe_qianwen.jpg` | `characters/qwen/adult/zipzippipe-pixiv-148186519-p12.jpg` | 3072×5504 | `3dfe6fafc0531d1bf3fd0a72556c91d53253b377ac52355bb127e54020d55114` |
  | `zipzippipe_grok.jpg` | `characters/grok/adult/zipzippipe-pixiv-148186519-p07.jpg` | 3072×5504 | `a5a4b4c95f905034b64bf80237f1b859ade0e6e31cadf4db40f9642124e171bb` |
  | `zipzippipe_doubao.jpg` | `characters/seed/adult/zipzippipe-bilibili-1236803362705375305-p00.jpg` | 3072×5504 | `81b76f71e19ed9f926e32c70f1710b7551e60efdfa858b46a4ee099d16e47847` |
  | `zipzippipe_gpt_chibi.png` | `characters/gpt/chibi/zipzippipe-BV1tE9XBbErS-23s.png` | 720×1280 | `90606f78f5c8ee76c3547c0df2999ba145cdb50bef25e7fc6a8a6c3ecc4ee4d5` |
  | `zipzippipe_claude_chibi.png` | `characters/claude/chibi/zipzippipe-BV1tE9XBbErS-17s.png` | 720×1280 | `331eb459d81c0a2f908fccaf8365f137a77098f81f1e145cca5413a1def07c55` |
  | `zipzippipe_gemini_chibi.png` | `characters/gemini/chibi/zipzippipe-BV1tE9XBbErS-11s.png` | 720×1280 | `d496c4edf07b964e18cce99b007c913237c9d44d1c8088fb84ea83e1972d1364` |
  | `zipzippipe_qwen_chibi.png` | `characters/qwen/chibi/zipzippipe-BV1tE9XBbErS-53s.png` | 720×1280 | `4f59aee4d8d4fa341a40b68f33d9593708f7eecfcb19c96ad8aa44d9bb844cb9` |
  | `zipzippipe_grok_chibi.png` | `characters/grok/chibi/zipzippipe-BV1tE9XBbErS-29s.png` | 720×1280 | `2f311320ca878e084ae031b65fbb697d3eb296f4091050a71c33797b78f30235` |

  注：`_chibi` 是仓库对「视频截帧」的目录命名，实测就是同设计的正常比例全身图，不是 Q 版。
- 许可（照仓库 `NOTICE.md` 原文口径，不擅自升级）：作者在《"亿万"身家的她们》简介说明**图片开放分享、允许用于再创作**；
  仓库**不统一授予 CC0/MIT 等标准许可证**，其中 DeepSeek 二次设计明确 **CC BY-NC-SA 4.0（不可商用）**。
  → 本项目为非商业学生作品，可用；**商用红线沿用 §头部**：商用前须换掉这些形象或取得书面授权。
- 转换：`tools/gen_personas_from_photo.py`（numpy + scipy 向量化）。四步：裁掉传输截断产生的纯灰行 →
  从四条边泛洪判近白为背景（**只删与边界连通的白**，白裙白袜保住）→ 最大连通域定前景包围盒 →
  按包围盒高的固定比例裁头区（0.30）与半身框（0.62，宽 = 高 × 64/80），半身框水平位置在「不把头挤出框」的
  约束内取装下最多前景像素的一档（长柄武器、张开的龙翼不被裁掉）→ LANCZOS 等比降采样 + 硬 alpha(0/255)。
  比例口径让同一角色的两种姿势在 64×80 里头部像素高度一致，主菜单每 3 秒换姿势不会忽大忽小。
- 帧分配：`idle` = pixiv 高清立绘；`wave`/`talk` = 视频截帧的另一姿势（同字节）。
  **豆包只有公开的一张**，三帧同图，舞台上只保留 ±2px 像素呼吸，不换姿势。
- 落盘：`Assets/Art/Personas/<key>_{face,idle,wave,talk}.png` 与 `Assets/Resources/Personas/` 同名同字节，
  六角色 × 4 帧 = 24 张，加 `dayu_*` 5 张共 29 张；逐字节一致与缺帧由 `Assets/Editor/PersonaAssetTests.cs` 判红。
- 署名落点：`App.cs CreditLines` 第三行「各家 AI 拟人立绘为第三方同人作品：ZipZipPipe《AI 娘化》系列 · 署名 ZipZipPipe · 非商业」，
  进主菜单落款与说明书「素材来源」页。
- 未采用：`characters/deepseek/**`（与 §一条目 1/6/8 的鲸鱼娘同源，避免同一角色两套设计）、
  `characters/kimi|glm|mistral|llama|minimax|hunyuan/**`（出场名单里没有这几家）、
  `characters/doubao/official/doubao-avatar.png`（豆包官方头像，权利归字节跳动，非同人开放素材）。



