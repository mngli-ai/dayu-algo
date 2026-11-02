using UnityEngine;

// 全部颜色都取自《美术调研方案》的 16 色板（tools/gen_art.py 的 PALETTE），UI 不再自造色值：
// 素材、模块图标、卡片、面板共用同一套颜色，画面才不会被一片淡黄糊住。
//
// 层次靠「冷暖」拉开，不靠深浅：
//   画布底 Foam(#EAF7F8 冷) → 面板 Paper(#FFFDF7 暖白) → 卡片 White(#FFFFFF) → 凹陷 Salt(#F2EAD8 沙)
// 模块类别色（Accent）只出现在卡片标题带、模块栏色条这类小面积上，大面积仍然是白与淡青。
public static class Theme {
 public const int TopBarH=44,PaletteW=176,InspectorW=330,TraceH=144,FooterH=28,TextSize=12,TitleSize=24;

 // 底色四层
 public static Color32 White=new(255,255,255,255);      // #FFFFFF 卡片底
 public static Color32 Cream=new(255,253,247,255);      // #FFFDF7
 public static Color32 Paper=new(255,253,247,255);      // #FFFDF7 面板底
 public static Color32 Salt=new(242,234,216,255);       // #F2EAD8 沙：分隔条、凹陷底
 public static Color32 Foam=new(234,247,248,255);       // #EAF7F8 画布底（冷）
 public static Color32 Bg=new(234,247,248,255);         // 画布区底色，等于 Foam

 // 主线蓝绿（鲸鱼）
 public static Color32 MintLight=new(185,232,221,255);  // #B9E8DD
 public static Color32 WhaleLight=new(118,201,213,255); // #76C9D5
 public static Color32 Whale=new(74,158,173,255);       // #4A9EAD
 public static Color32 WhaleDeep=new(46,107,119,255);   // #2E6B77
 public static Color32 Ink=new(56,81,91,255);           // #38515B 文字与 1px 描边（素材同色）
 public static Color32 Border=new(56,81,91,255);        // 描边＝Ink，和素材的深海灰描边同族

 // 强调色
 public static Color32 Honey=new(246,180,92,255);       // #F6B45C
 public static Color32 HoneyDeep=new(217,142,63,255);   // #D98E3F
 public static Color32 Yellow=new(244,216,120,255);     // #F4D878
 public static Color32 Coral=new(242,155,143,255);      // #F29B8F
 public static Color32 Mint=new(139,214,168,255);       // #8BD6A8
 public static Color32 Lavender=new(185,154,217,255);   // #B99AD9
 public static Color32 Grape=new(142,111,184,255);      // #8E6FB8
 public static Color32 GrapeDeep=new(142,111,184,255);

 /// <summary>
 /// 带透明度的主题色。Color32 的 .r/.g/.b 是 0-255 的字节，直接写 new Color(c.r, c.g, c.b, a)
 /// 会把 76 这种值当成浮点分量、渲染时被 clamp 成纯白（选中高亮、拖拽残影都因此变成一块白）。
 /// </summary>
 public static Color A(Color32 c, float alpha) { return new Color(c.r / 255f, c.g / 255f, c.b / 255f, alpha); }

 /// <summary>模块类别色：节点类型 / 模块栏条目上写的 ColorKey 在这里落地成真实颜色。</summary>
 public static Color32 Accent(string colorKey)
 {
  switch (colorKey)
  {
   case "cyan":     return WhaleLight;   // 输入输出
   case "whale":    return Whale;        // 流程控制
   case "pink":     return Coral;        // 变量
   case "yellow":   return Yellow;       // 常量、运算、字符串
   case "purple":   return Grape;        // 栈的写入
   case "lavender": return Lavender;     // 栈的读取
   case "honey":    return Honey;        // 样例输入板
   case "mint":     return Mint;         // 样例对比板
  }
  return Whale;
 }

 /// <summary>类别色的淡底：铺在白色卡片标题带上，alpha 低到不压住黑字。</summary>
 public static Color AccentSoft(string colorKey, float alpha) { return A(Accent(colorKey), alpha); }

 /// <summary>
 /// 两个板色的定量混合（t=0 全 a、t=1 全 b）。只允许在同一个色族里拉开层次用——
 /// UI 需要「比 Ink 浅一档的描边」「比 Honey 亮一档的悬停」这类中间调，但板外不许出现新色相，
 /// 所以一律以常量落地并在下一行注明混合自哪两个板色，方便和美术对色。
 /// </summary>
 public static Color32 Mix(Color32 a, Color32 b, float t)
 {
  return new Color32(
   (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
   (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
   (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
   255);
 }

 // —— 以下 6 个是 UI 质感层（双层描边 / 悬停 / 按下）用到的中间调，全部由上面的 Mix 从 16 色板量化混合 ——
 public static Color32 InkSoft = Mix(Ink, Foam, .55f);       // #9AACB1 = Mix(Ink,Foam,.55) 双层描边的内圈：外圈 Ink 才读得出两层
 public static Color32 InkPress = Mix(Ink, WhaleDeep, .5f);  // #335E69 = Mix(Ink,WhaleDeep,.5) 按下态描边：同色族、更冷更深
 public static Color32 SaltPress = Mix(Salt, Ink, .18f);     // #D1CEC2 = Mix(Salt,Ink,.18) 按下态底色：比 Salt 沉一档
 public static Color32 HoneyLift = Mix(Honey, Cream, .42f);  // #FAD39D = Mix(Honey,Cream,.42) 主按钮悬停：Honey 加高光
 public static Color32 HoneyPress = Mix(Honey, HoneyDeep, .55f); // #E69F4C = Mix(Honey,HoneyDeep,.55) 主按钮按下
 public static Color32 ShadeSoft = Mix(Salt, Ink, .28f);     // #BEBFB5 = Mix(Salt,Ink,.28) 面板下沿 1px 内暗边
}
