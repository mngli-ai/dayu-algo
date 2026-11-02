from __future__ import annotations
import sys, json, math, re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SPR = ROOT/'Assets/Art/Sprites'; FONTDIR=ROOT/'Assets/Art/Font'
PALETTE=['#FFFDF7','#F2EAD8','#FFFFFF','#EAF7F8','#B9E8DD','#76C9D5','#4A9EAD','#2E6B77','#38515B','#F6B45C','#D98E3F','#B99AD9','#8E6FB8','#F4D878','#F29B8F','#8BD6A8']
P=[tuple(bytes.fromhex(x[1:]))+(255,) for x in PALETTE]

class Canvas:
 def __init__(self,w=32,h=32): self.w=w; self.h=h; self.a=[[-1]*w for _ in range(h)]
 def rect(self,x,y,w,h,c):
  for yy in range(max(0,y),min(self.h,y+h)):
   for xx in range(max(0,x),min(self.w,x+w)): self.a[yy][xx]=c
 def outline(self):
  out=[r[:] for r in self.a]
  for y in range(self.h):
   for x in range(self.w):
    if self.a[y][x]<0 and any(0<=nx<self.w and 0<=ny<self.h and self.a[ny][nx]>=0 for nx,ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1))): out[y][x]=8
  self.a=out
 def image(self):
  im=Image.new('RGBA',(self.w,self.h),(0,0,0,0)); px=im.load()
  for y,row in enumerate(self.a):
   for x,c in enumerate(row):
    if c>=0: px[x,y]=P[c]
  return im

def base(pose='idle'):
 c=Canvas(); R=c.rect
 R(6,3,20,15,6); R(9,6,14,8,0); R(6,3,20,3,5); R(15,5,2,2,0); R(8,3,12,1,3)
 R(6,6,3,10,5); R(23,6,3,10,5); R(6,16,2,1,6); R(24,16,2,1,6)
 R(4,9,2,3,5); R(26,9,2,3,5)
 R(11,8,2,2,7); R(11,8,1,1,2); R(19,8,2,2,7); R(19,8,1,1,2); R(9,10,2,1,14); R(21,10,2,1,14); R(15,12,2,1,7)
 R(14,13,4,1,1); R(9,14,2,2,7); R(21,14,2,2,7); R(10,14,12,7,7); R(12,15,8,6,2); R(12,14,1,1,2); R(19,14,1,1,2); R(14,17,4,2,3)
 R(7,15,2,4,2); R(7,19,2,1,0); R(23,15,2,4,2); R(23,19,2,1,0); R(9,20,14,3,7); R(8,23,16,2,7); R(12,25,2,2,1); R(18,25,2,2,1); R(11,27,3,1,7); R(18,27,3,1,7); R(21,19,3,2,6); R(24,18,4,4,5); R(27,21,2,1,6)
 R(18,1,1,2,5); R(19,1,2,1,5)
 if pose=='carry':
  R(7,15,2,4,-1); R(23,15,2,4,-1); R(12,17,2,3,2); R(18,17,2,3,2); R(14,19,4,4,13)
 elif pose=='happy' or pose=='nap':
  R(10,8,4,1,7); R(18,8,4,1,7); R(14,12,4,1,7); R(13,11,1,1,7); R(18,11,1,1,7)
 elif pose=='sad':
  R(10,8,4,2,0); R(18,8,4,2,0); R(10,9,4,1,7); R(18,9,4,1,7); R(12,11,1,1,5); R(19,3,3,1,5); R(18,1,1,2,-1); R(19,1,2,1,-1)
 if pose=='nap': R(23,4,3,1,8); R(24,5,1,1,8); R(23,6,3,1,8)
 c.outline(); return c

def gen_heroes():
 SPR.mkdir(parents=True,exist_ok=True); meta={'frameW':32,'frameH':32,'anims':{}}
 counts={'idle':6,'swim':6,'carry':6,'happy':6,'sad':6,'nap':6}
 for pose,n in counts.items():
  strip=Image.new('RGBA',(32*n,32),(0,0,0,0))
  for i in range(n):
   im=base(pose).image(); dx=round(math.sin(i*2)*1) if pose=='idle' else 0
   if pose=='happy': dy=round(math.sin(i*6))*2
   elif pose in ('sad','nap'): dy=1
   else: dy=round(math.sin(i*2.4)) if pose=='swim' else 0
   if pose=='nap': dx=-1
   strip.alpha_composite(im,(i*32+dx,dy))
  strip.save(SPR/f'hero_{pose}.png'); meta['anims'][pose]={'frames':n,'fps':6}
 (SPR/'hero_meta.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')

def gen_tokens_icons():
 cols={'int':9,'char':11,'bool':13,'str':11}
 for k,c in cols.items():
  im=Image.new('RGBA',(16,16),(0,0,0,0)); d=ImageDraw.Draw(im); d.rectangle((1,1,14,14),fill=P[c],outline=P[8]); im.save(SPR/f'token_{k}.png')
 names=['play','pause','step','reset','bug','track','fit','submit','plus','close','chevron','stack','queue','vec','branch','loop','var','io','math']
 for name in names:
  im=Image.new('RGBA',(12,12),(0,0,0,0)); d=ImageDraw.Draw(im); q=P[7];
  if name=='play': d.polygon([(3,2),(10,6),(3,10)],fill=q)
  elif name=='pause': d.rectangle((2,2,4,10),fill=q); d.rectangle((7,2,9,10),fill=q)
  elif name=='plus': d.rectangle((5,2,6,9),fill=q); d.rectangle((2,5,9,6),fill=q)
  elif name=='close': d.line((2,2,9,9),fill=q,width=2); d.line((9,2,2,9),fill=q,width=2)
  elif name=='chevron': d.line((3,3,6,6),fill=q,width=2); d.line((6,6,3,9),fill=q,width=2)
  elif name in ('stack','queue'): 
   for y in (2,5,8): d.rectangle((2,y,9,y+1),fill=q)
  elif name=='branch': d.line((3,2,3,10),fill=q,width=2); d.line((3,5,9,5),fill=q,width=2); d.line((9,5,9,9),fill=q,width=2)
  elif name=='loop': d.arc((2,2,10,10),20,320,fill=q,width=2); d.polygon([(8,2),(10,2),(10,4)],fill=q)
  elif name=='bug': d.ellipse((3,3,8,9),fill=q); d.line((2,4,0,3),fill=q); d.line((2,8,0,9),fill=q); d.line((9,4,11,3),fill=q); d.line((9,8,11,9),fill=q)
  elif name=='track': d.ellipse((3,3,8,8),outline=q); d.line((6,1,6,10),fill=q); d.line((1,6,10,6),fill=q)
  elif name=='fit': d.rectangle((2,2,9,9),outline=q); d.line((2,5,5,5),fill=q); d.line((6,6,9,6),fill=q)
  elif name=='submit': d.line((2,6,9,6),fill=q,width=2); d.polygon([(7,3),(10,6),(7,9)],fill=q)
  elif name=='reset': d.arc((2,2,10,10),40,320,fill=q,width=2); d.polygon([(2,2),(5,2),(3,5)],fill=q)
  elif name=='step': d.polygon([(2,3),(7,6),(2,9)],fill=q); d.rectangle((8,3,9,9),fill=q)
  elif name=='vec': d.rectangle((2,3,9,8),outline=q); d.line((4,3,4,8),fill=q); d.line((6,3,6,8),fill=q)
  elif name=='var': d.rectangle((3,2,8,9),outline=q); d.line((2,10,10,10),fill=q)
  elif name=='io': d.rectangle((2,3,9,8),outline=q); d.line((5,1,5,10),fill=q)
  elif name=='math': d.line((2,6,10,6),fill=q,width=2); d.line((6,2,6,10),fill=q)
  im.save(SPR/f'icon_{name}.png')

# ---------------- 模块图标：每个模块一个实体（《美术调研方案》三、视觉语言总表）----------------
# 16x16，1 像素深海灰描边（P[8]），只允许用 16 色板里的颜色。
ICO=ROOT/'Assets/Art/ModuleIcons'; INK=8; CREAM=0; FOAM=4; WHALE=5; DEEP=7; HONEY=9; AMBER=10; GRAPE=12; YELLOW=13; CORAL=14; MINT=15; SAND=1

def _icon():
 im=Image.new('RGBA',(16,16),(0,0,0,0)); return im,ImageDraw.Draw(im)

def _box(d,x,y,c=HONEY):
 '数字木箱：整数=蜜糖橙，比较/布尔=鲸鱼蓝，字符=葡萄紫'
 d.rectangle((x,y,x+5,y+5),fill=P[c],outline=P[INK]); d.point((x+1,y+1),fill=P[CREAM])

def _cab(d):
 '变量储物柜：柜体 + 抽屉缝 + 把手'
 d.rectangle((2,1,13,14),fill=P[SAND],outline=P[INK])
 for y in (5,9): d.line((3,y,12,y),fill=P[INK])
 d.rectangle((7,2,8,4),fill=P[DEEP]); d.rectangle((7,6,8,8),fill=P[DEEP]); d.rectangle((7,10,12,13),fill=P[FOAM])

def _tube(d,c=GRAPE):
 '栈=竖直弹簧筒'
 d.rectangle((4,1,11,14),fill=P[3],outline=P[INK]); d.line((4,4,11,4),fill=P[c]); d.line((4,11,11,11),fill=P[c])

def _quay(d,c=WHALE):
 '队列=横向通道，右边进左边出'
 d.rectangle((1,6,14,13),fill=P[3],outline=P[INK]); d.line((2,8,13,8),fill=P[c])

def _car(d,x,c=FOAM):
 '通道里的一节车厢'
 d.rectangle((x,9,x+4,12),fill=P[c],outline=P[INK])

def _belt(d):
 '传送带：两条辊子 + 带面'
 d.line((1,11,14,11),fill=P[INK]); d.ellipse((2,12,4,14),fill=P[DEEP]); d.ellipse((11,12,13,14),fill=P[DEEP])

def _gear(d,x,y,r=3,c=AMBER):
 d.ellipse((x-r,y-r,x+r,y+r),fill=P[c],outline=P[INK])
 for dx,dy in ((0,-r-1),(0,r+1),(-r-1,0),(r+1,0)): d.point((x+dx,y+dy),fill=P[INK])

def mod_icons():
 defs={}
 # 开始：起点旗
 im,d=_icon(); d.line((3,1,3,14),fill=P[INK]); d.polygon([(4,2),(11,4),(4,6)],fill=P[WHALE]); defs['entry']=im
 # 读取输入：传送带把箱子送进来
 im,d=_icon(); _belt(d); _box(d,5,4); d.line((13,6,13,9),fill=P[INK]); d.polygon([(11,9),(13,9),(12,11)],fill=P[INK]); defs['input']=im
 # 创建变量：柜子 + 加号
 im,d=_icon(); _cab(d); d.rectangle((1,1,4,1),fill=P[MINT]); d.rectangle((2,0,3,2),fill=P[MINT]); defs['vardecl']=im
 # 创建数组：铁轨上并排三节车厢（和画布里的数组列车同一套视觉语言）+ 加号
 im,d=_icon(); d.rectangle((1,1,4,1),fill=P[MINT]); d.rectangle((2,0,3,2),fill=P[MINT])
 for x in (0,5,10):
  d.rectangle((x,5,x+3,10),fill=P[WHALE],outline=P[INK]); d.point((x+1,6),fill=P[CREAM])
  d.point((x+1,11),fill=P[INK]); d.point((x+2,11),fill=P[INK])
 d.line((4,7,4,8),fill=P[INK]); d.line((9,7,9,8),fill=P[INK])
 d.line((0,12,15,12),fill=P[INK]); d.line((0,13,15,13),fill=P[DEEP]); defs['array_new']=im
 # 赋值：箱子推进抽屉
 im,d=_icon(); _cab(d); _box(d,9,10,WHALE); d.line((6,12,8,12),fill=P[INK]); defs['varset']=im
 # 读取变量：抽屉滑出、箱子浮起
 im,d=_icon(); d.rectangle((2,1,13,14),fill=P[SAND],outline=P[INK]); d.line((3,9,12,9),fill=P[INK]); d.rectangle((4,10,14,13),fill=P[FOAM],outline=P[INK]); _box(d,5,3,WHALE); defs['varget']=im
 # 常量：一只木箱
 im,d=_icon(); _box(d,5,5); d.line((6,7,9,7),fill=P[INK]); d.line((7,6,7,9),fill=P[INK]); defs['const']=im
 # 运算：两台箱子进机器，压出新箱子
 im,d=_icon(); d.rectangle((1,4,9,11),fill=P[3],outline=P[INK]); _gear(d,5,7); _box(d,10,1); _box(d,10,10,AMBER); defs['binop']=im
 # 比较：天平
 im,d=_icon(); d.line((7,2,7,12),fill=P[INK]); d.line((2,4,13,4),fill=P[INK]); d.rectangle((1,5,4,7),fill=P[WHALE],outline=P[INK]); d.rectangle((11,5,14,7),fill=P[HONEY],outline=P[INK]); d.rectangle((5,12,10,13),fill=P[INK]); defs['compare']=im
 # 逻辑：紧急拉杆 + 与门块
 im,d=_icon(); d.rectangle((2,8,10,13),fill=P[3],outline=P[INK]); d.line((4,10,8,10),fill=P[DEEP]); d.line((10,12,14,3),fill=P[INK]); d.ellipse((13,2,15,4),fill=P[CORAL]); defs['logic']=im
 # 条件分支：铁路道岔
 im,d=_icon(); d.line((1,12,15,12),fill=P[INK]); d.line((1,10,15,10),fill=P[INK]); d.line((6,10,12,3),fill=P[INK]); d.line((6,12,12,5),fill=P[INK]); d.ellipse((2,2,5,5),fill=P[WHALE]); defs['branch']=im
 # 计数循环：工厂 + 计数轮 + 烟囱
 im,d=_icon(); d.rectangle((1,7,13,14),fill=P[SAND],outline=P[INK]); d.polygon([(1,7),(4,4),(7,7),(10,4),(13,7)],fill=P[DEEP]); d.rectangle((10,1,12,5),fill=P[AMBER],outline=P[INK]); d.ellipse((3,9,7,13),fill=P[WHALE],outline=P[INK]); d.line((5,9,5,13),fill=P[INK]); defs['forloop']=im
 # 条件循环：水车
 im,d=_icon(); d.ellipse((2,1,13,12),fill=P[3],outline=P[INK]); [d.line((8,1,8,12),fill=P[INK]),d.line((2,7,13,7),fill=P[INK]),d.line((3,2,12,11),fill=P[DEEP]),d.line((12,2,3,11),fill=P[DEEP])]; d.line((1,14,14,14),fill=P[WHALE]); defs['whileloop']=im
 # 输出：秤台，数字盘跳数
 im,d=_icon(); d.rectangle((1,10,14,14),fill=P[SAND],outline=P[INK]); d.ellipse((3,1,12,10),fill=P[FOAM],outline=P[INK]); d.line((7,6,10,3),fill=P[CORAL]); d.point((7,6),fill=P[INK]); _box(d,9,11,MINT); defs['output']=im
 # 样例输入板：质检台上待检箱，右侧箭头把数据送进来
 im,d=_icon(); d.rectangle((1,9,14,13),fill=P[3],outline=P[INK]); _box(d,2,3,HONEY); _box(d,8,3,HONEY); d.line((1,1,6,1),fill=P[INK]); d.polygon([(5,0),(5,3),(8,1)],fill=P[INK]); defs['sample_input']=im
 # 样例对比板：实际（蓝）与期望（橙）并排，一致亮薄荷绿勾
 im,d=_icon(); d.rectangle((1,9,14,13),fill=P[3],outline=P[INK]); _box(d,2,4,WHALE); _box(d,8,4,AMBER); d.line((1,2,3,4),fill=P[MINT]); d.line((3,4,7,1),fill=P[MINT]); defs['sample_compare']=im
 # 创建结构：筒里放一块葡萄紫的构件 + 加号
 im,d=_icon(); _tube(d); d.rectangle((5,11,10,13),fill=P[GRAPE],outline=P[INK]); d.rectangle((12,1,15,1),fill=P[MINT]); d.rectangle((13,0,14,3),fill=P[MINT]); defs['structnew']=im
 # 栈·压入：筒里两格 + 一只珊瑚箱正落进筒口
 im,d=_icon(); _tube(d); d.rectangle((5,11,10,13),fill=P[FOAM],outline=P[INK]); d.rectangle((5,8,10,10),fill=P[HONEY],outline=P[INK]); _box(d,5,2,CORAL); d.line((13,1,13,5),fill=P[INK]); d.polygon([(11,4),(15,4),(13,7)],fill=P[INK]); defs['stack_push']=im
 # 栈·弹出：筒里两格 + 顶格黄箱已浮到筒口外
 im,d=_icon(); _tube(d); d.rectangle((5,11,10,13),fill=P[FOAM],outline=P[INK]); _box(d,5,1,YELLOW); d.line((13,7,13,3),fill=P[INK]); d.polygon([(11,4),(15,4),(13,1)],fill=P[INK]); defs['stack_pop']=im
 # 栈·查看顶：筒里两格，顶格涂黄并被侧边箭头指住
 im,d=_icon(); _tube(d); d.rectangle((5,11,10,13),fill=P[FOAM],outline=P[INK]); d.rectangle((5,8,10,10),fill=P[YELLOW],outline=P[INK]); d.line((12,9,14,9),fill=P[INK]); d.polygon([(14,7),(14,11),(12,9)],fill=P[INK]); defs['stack_top']=im
 # 栈·是否空：空筒（只剩底格）+ 问号
 im,d=_icon(); _tube(d); d.line((6,4,9,4),fill=P[DEEP]); d.line((7,6,8,8),fill=P[DEEP]); d.rectangle((7,10,8,11),fill=P[DEEP]); defs['stack_empty']=im
 # 栈·数量：筒里三格 + 右侧刻度条
 im,d=_icon(); _tube(d); d.rectangle((5,12,10,13),fill=P[FOAM],outline=P[INK]); d.rectangle((5,9,10,11),fill=P[HONEY],outline=P[INK]); d.rectangle((5,6,10,8),fill=P[MINT],outline=P[INK]); [d.line((12,y,14,y),fill=P[INK]) for y in (3,7,11)]; defs['stack_size']=im
 # 创建队列：横道里一节薄荷车厢 + 加号
 im,d=_icon(); _quay(d); _car(d,5,MINT); d.rectangle((11,1,14,1),fill=P[MINT]); d.rectangle((12,0,13,3),fill=P[MINT]); defs['queue_new']=im
 # 队列·入队：道里两节 + 一只珊瑚箱从右边挤进来
 im,d=_icon(); _quay(d); _car(d,3,FOAM); _car(d,8,HONEY); _box(d,10,1,CORAL); d.line((14,6,14,8),fill=P[INK]); d.polygon([(12,7),(16,7),(14,9)],fill=P[INK]); defs['queue_push']=im
 # 队列·出队：顶到左口那节黄箱已经飘出道外
 im,d=_icon(); _quay(d); _car(d,7,FOAM); _box(d,0,1,YELLOW); d.line((1,6,3,6),fill=P[INK]); d.polygon([(3,4),(3,8),(0,6)],fill=P[INK]); defs['queue_pop']=im
 # 队列·看队头：最左那节涂黄，右侧箭头指住它
 im,d=_icon(); _quay(d); _car(d,2,YELLOW); _car(d,7,FOAM); _car(d,11,HONEY); d.line((1,4,4,4),fill=P[INK]); d.polygon([(4,2),(4,6),(2,4)],fill=P[INK]); defs['queue_front']=im
 # 队列·是否空：空道 + 问号
 im,d=_icon(); _quay(d); d.line((6,1,9,1),fill=P[DEEP]); d.line((7,2,8,3),fill=P[DEEP]); d.rectangle((7,4,8,5),fill=P[DEEP]); defs['queue_empty']=im
 # 队列·长度：道里三节 + 下方刻度
 im,d=_icon(); _quay(d); _car(d,2,FOAM); _car(d,6,HONEY); _car(d,10,MINT); [d.line((x,14,x,15),fill=P[INK]) for x in (2,6,10,13)]; defs['queue_size']=im
 # 取字符：卷轴 + 一块铅字
 im,d=_icon(); d.rectangle((1,4,13,9),fill=P[CREAM],outline=P[INK]); d.rectangle((1,4,2,9),fill=P[AMBER]); d.rectangle((12,4,13,9),fill=P[AMBER]); d.rectangle((9,10,13,14),fill=P[GRAPE],outline=P[INK]); defs['text_index']=im
 # 长度：尺子
 im,d=_icon(); d.rectangle((1,5,14,11),fill=P[YELLOW],outline=P[INK]); [d.line((x,5,x,8),fill=P[INK]) for x in (4,7,10)]; defs['len_of']=im
 return defs

def gen_module_icons():
 ICO.mkdir(parents=True,exist_ok=True)
 for key,im in mod_icons().items(): im.save(ICO/f'mod_{key}_16x16.png')
 print('模块图标', len(mod_icons()), '个 ->', ICO)

def collect_chars():
 files=list((ROOT/'Assets/Scripts').rglob('*.cs')) if (ROOT/'Assets/Scripts').exists() else []
 web=Path(r"C:/Users/24585/OneDrive/MenG's File/大肥鱼の算法大冒险/web2/src")
 if web.exists(): files += list(web.rglob('*.js'))
 s=''.join(p.read_text(encoding='utf-8',errors='ignore') for p in files)
 return sorted(set(ch for ch in s if (ord(ch)>=128 or 0x20<=ord(ch)<=0x7e)))

# 像素字体 fusion-pixel-12px 里没有 ✓ ✗ 这一对勾叉，而表达式浮窗的提示要用它们：
# 空字形在界面上就是一个看不见的空格，所以在这里按 (列,行) 手画台阶点阵补进图集，
# 点阵第 0 行落在图集第 4 行，跟 12px 汉字的基线对齐；adv 留 2px 间隙免得和下一个字粘上。
HAND={
 '✓': {'off':(1,4),'adv':9,'dots':[(0,2),(1,3),(2,4),(3,3),(4,2),(5,1),(6,0)]},
 '✗': {'off':(2,4),'adv':8,'dots':[(0,0),(1,1),(2,2),(3,3),(4,4),(4,0),(3,1),(1,3),(0,4)]},
}

def gen_font():
 chars=collect_chars(); ttf=FONTDIR/'fusion-pixel-12px.ttf'; font=ImageFont.truetype(str(ttf),12); cols=32; rows=max(1,(len(chars)+cols-1)//cols)
 atlas=Image.new('RGBA',(cols*12,rows*14),(0,0,0,0)); glyphs={}; missing=0
 for i,ch in enumerate(chars):
  x=(i%cols)*12; y=(i//cols)*14
  tile=Image.new('L',(12,14),0); dr=ImageDraw.Draw(tile); dr.text((0,11),ch,font=font,fill=255,anchor='ls',stroke_width=0)
  tile=tile.point(lambda a:255 if a>=128 else 0); adv=round(font.getlength(ch))
  if tile.getbbox() is None and ch in HAND:
   ox,oy=HAND[ch]['off']; dr=ImageDraw.Draw(tile)
   for c,r in HAND[ch]['dots']: dr.point((ox+c,oy+r),fill=255)
   adv=HAND[ch]['adv']
  atlas.paste(Image.merge('RGBA',(tile,tile,tile,tile)),(x,y),tile)
  glyphs[ch]={'x':i%cols,'y':i//cols,'adv':adv}; missing += int(tile.getbbox() is None and ch!=' ')
 atlas.save(FONTDIR/'atlas.png'); (FONTDIR/'atlas.json').write_text(json.dumps({'cellW':12,'cellH':14,'cols':cols,'rows':rows,'glyphs':glyphs},ensure_ascii=False,indent=2),encoding='utf-8')
 prev=atlas.resize((atlas.width*3,atlas.height*3),Image.Resampling.NEAREST); d=ImageDraw.Draw(prev)
 for x in range(0,prev.width,36): d.line((x,0,x,prev.height),fill=(120,120,120,255))
 for y in range(0,prev.height,42): d.line((0,y,prev.width,y),fill=(120,120,120,255))
 prev.save(Path(__import__('tempfile').gettempdir())/'font_preview.png'); print(f'字形总数: {len(chars)}, 缺字数: {missing}, atlas: {atlas.width}x{atlas.height}')
 if missing>10: raise RuntimeError('缺字超过 10 个')

def main():
 cmd=sys.argv[1] if len(sys.argv)>1 else 'all'
 if cmd in ('all','sprites'): gen_heroes(); gen_tokens_icons()
 if cmd in ('all','icons'): gen_module_icons()
 if cmd in ('all','font'): gen_font()
if __name__=='__main__': main()
