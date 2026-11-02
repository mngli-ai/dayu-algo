using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 卡片外形的几何生成器。
// 这套 UI 全部是运行时用 1x1 白 Sprite + Image 拼出来的，没有卡片底板贴图，所以非矩形外形只能「扫描线横条」来画：
// 按 Pitch 一行一切，每行一条 Image，行宽由该行的形状方程定。像素风的斜边本来就是台阶状的，
// 这种「一行一台阶」的画法就是正确的抖动边缘，不需要抗锯齿，也不需要任何新素材，
// 和 ModuleViz.SilhouetteBlocks 往框外凸的轮廓台阶是同一套语言。
// 坐标约定和卡片一致：x 向右、y 从卡片上缘向下（Y 向下为正），单位像素，父节点用左上角锚点 + pivot(0,1)。
// Bars 是纯函数，所有单测只测它和 FitsContent / ForType，不建场景对象。
public enum CardShape { Rect, RoundRect, Hexagon, Diamond, Circle, TriangleDown, Trapezoid, Pill }

public static class CardShapes
{
    public const float Pitch = 2f;          // 台阶高度：2px 一行，和描边同量级，再大就开始有明显的锯齿
    const float EdgePx = 2f;                // 每行两端各让出的描边宽度
    const float Chamfer = 0.25f;            // 六边形斜边吃掉的高度比例（上下各一段，中间留出一条等宽的腰）
    const float MinVisible = 0.5f;          // 窄过半个像素的行直接丢掉，不产出 0 宽的 Image

    // 纯函数：把形状切成横条。返回 (x, y, 条宽, 条高)，卡片内部坐标、左上角为原点、Y 向下。
    // 行心取样（不取行首/行尾），台阶才左右对称、截图稳定；宽度再夹紧到卡片宽，
    // 所以任何一条都必然落在框内：条的左缘 >= 0、右缘 <= 卡片宽、上缘 >= 0、下缘 <= 卡片高。
    // 只产出宽度 > 0 的条，条数 <= ceil(卡片高 / Pitch)；矩形一定是整 ceil(卡片高 / Pitch) 条。
    public static List<Vector4> Bars(CardShape shape, float w, float h)
    {
        var L = new List<Vector4>();
        if (w <= 0f || h <= 0f) return L;
        int n = Mathf.CeilToInt(h / Pitch);
        for (int i = 0; i < n; i++)
        {
            float y = i * Pitch;
            float rh = Mathf.Min(Pitch, h - y);
            float bw = Mathf.Clamp(Span(shape, w, h, y + rh * 0.5f), 0f, w);
            if (bw < MinVisible) continue;
            L.Add(new Vector4((w - bw) * 0.5f, y, bw, rh));
        }
        return L;
    }

    // 池化绘制：容器（名字就是 name）挂在 parent 下，横条全塞进容器里复用。
    // 每行两条：先一条整行宽的描边色，再一条两端各缩进 EdgePx 的填充色 —— 斜边两侧就都有了描边，
    // 顶边底边留给卡片自己的边框（填充条铺满整行高，所以行与行之间不会长出横纹）。
    // 重画只改位置和颜色，多余的条 enabled=false，不 Destroy 重建。
    // raycastTarget 一律关掉：命中区归卡片底板，形状只管画，改外形不碰交互。
    public static void Draw(RectTransform parent, string name, CardShape shape,
                            float w, float h, Color fill, Color edge)
    {
        if (parent == null) return;
        var rt = Holder(parent, name, w, h);
        var pool = rt.GetComponent<BarPool>();
        var bars = Bars(shape, w, h);
        int used = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            Place(Pick(pool, ref used, name + "Edge", rt, edge), b);
            float iw = b.z - EdgePx * 2f;
            if (iw >= MinVisible)
                Place(Pick(pool, ref used, name + "Fill", rt, fill),
                      new Vector4(b.x + EdgePx, b.y, iw, b.w));
        }
        for (int i = used; i < pool.Bars.Count; i++)
            if (pool.Bars[i].enabled) pool.Bars[i].enabled = false;
    }

    // 卡片销毁 / 换形状时用：整个容器连带里面的横条一起清掉
    public static void Clear(RectTransform parent, string name)
    {
        if (parent == null || string.IsNullOrEmpty(name)) return;
        var t = parent.Find(name);
        if (t != null) Object.Destroy(t.gameObject);
    }

    // 形状要跟着信息量走，不能只看类型 key 写死：
    // 可编辑表达式是卡面上最占地方的一行字，两格以上就必须回到装得下字的形状，尖角形状一律放弃。
    public static CardShape ForType(string nodeTypeKey, int exprFieldCount)
    {
        if (exprFieldCount >= 2) return CardShape.RoundRect;
        switch (nodeTypeKey)
        {
            case "entry": return CardShape.TriangleDown;
            case "input": return CardShape.Hexagon;
            case "output": return CardShape.Pill;

            case "branch":                                    // 菱形＝真/假两岔，教科书画法
            case "compare":
            case "logic": return CardShape.Diamond;

            case "forloop":
            case "whileloop": return CardShape.RoundRect;      // 要被循环体外框包住，尖角会顶穿外框

            case "vardecl":
            case "varset":
            case "varget":
            case "binop":
            case "text_index": return CardShape.Rect;          // 名字和公式都是要读的字

            case "const": return CardShape.Circle;             // 一个值加一个类型，信息最少

            case "structnew": return CardShape.RoundRect;      // 创建栈/队列：名字要写，还得挂住后面一整列

            case "stack_push":
            case "queue_push": return CardShape.Trapezoid;     // 上宽下窄的漏斗，东西从宽口掉进结构

            case "stack_pop":
            case "queue_pop":
            case "stack_top":
            case "queue_front": return CardShape.Hexagon;      // 和「输入」同族：都是往外吐一个值

            case "stack_empty":
            case "queue_empty": return CardShape.Diamond;      // 只回一句「是/否」的布尔判定

            case "stack_size":
            case "queue_size":
            case "len_of": return CardShape.Pill;              // 只回一个数的小胶囊

            case "sample_input":
            case "sample_compare": return CardShape.Rect;      // 样例板要排两行数字
        }
        return CardShape.Rect;
    }

    // 尖角形状会不会挤掉文字：看形状里能不能塞下一块 keepW x keepH 的居中矩形（卡片的内容区）。
    // 逐行取形状宽度，取内容带内最窄的一行来比 —— 最窄处过得去，整块文字就一定在形状里。
    public static bool FitsContent(CardShape shape, float w, float h, float keepW, float keepH)
    {
        if (keepW <= 0f || keepH <= 0f) return true;
        if (keepW > w || keepH > h) return false;
        if (shape == CardShape.Rect) return true;             // 矩形没有尖角，永远是最宽松的
        float top = (h - keepH) * 0.5f;
        float bot = top + keepH;
        const int Steps = 16;
        float narrow = float.MaxValue;
        for (int i = 0; i <= Steps; i++)
        {
            float y = top + (bot - top) * (i / (float)Steps);
            narrow = Mathf.Min(narrow, Span(shape, w, h, y));
        }
        return narrow + 0.01f >= keepW;
    }

    // 某一行（精确高度 y）上的形状宽度。每个形状都是「越靠两端越窄」的单峰曲线
    // （倒三角、梯形是峰在顶边的退化情形，圆角矩形和六边形腰上是一段平的），
    // 所以 Bars 用行心取样、FitsContent 取内容带里的最窄处，都只需要这一个方程。
    static float Span(CardShape shape, float w, float h, float y)
    {
        float mid = h * 0.5f;
        switch (shape)
        {
            case CardShape.Diamond:
                return w * (1f - Mathf.Abs(y - mid) / mid);

            case CardShape.Circle:
            {
                float t = (y - mid) / mid;
                return w * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            }
            case CardShape.TriangleDown:
                return w * (1f - y / h);

            case CardShape.Trapezoid:                          // 末行仍有半宽：梯形不是三角形
                return w * (1f - 0.5f * Mathf.Clamp01(y / h));

            case CardShape.Pill:
            {
                float r = Mathf.Min(mid, w * 0.5f);
                float dy = y - mid;
                return w - 2f * r + 2f * Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
            }
            case CardShape.Hexagon:
            {
                float band = Mathf.Min(Mathf.Max(h * Chamfer, Pitch * 2f), mid);
                float k = y < band ? (band - y) / band
                        : (y > h - band ? (y - (h - band)) / band : 0f);
                return w - w * 0.5f * k;                        // 顶边留半宽，腰上是整宽
            }
            case CardShape.RoundRect:
                return w - 2f * Corner(w, h, y);
        }
        return w;
    }

    // 圆角：半径取短边的四分之一，只在上下两个圆角带里往里收，中间一段是满宽的直边
    static float Corner(float w, float h, float y)
    {
        float r = Mathf.Min(w, h) * 0.25f;
        if (r <= 0f) return 0f;
        float d = y < r ? r - y : (y > h - r ? y - (h - r) : 0f);
        if (d <= 0f) return 0f;
        return r - Mathf.Sqrt(Mathf.Max(0f, r * r - d * d));
    }

    static RectTransform Holder(RectTransform parent, string name, float w, float h)
    {
        RectTransform rt;
        BarPool pool;
        var found = parent.Find(name);
        if (found == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            rt = (RectTransform)go.transform;
            pool = go.AddComponent<BarPool>();
        }
        else
        {
            rt = (RectTransform)found;
            pool = rt.GetComponent<BarPool>();
            if (pool == null) pool = rt.gameObject.AddComponent<BarPool>();
        }
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static Image Pick(BarPool pool, ref int used, string nm, RectTransform rt, Color c)
    {
        Image img;
        if (used < pool.Bars.Count) img = pool.Bars[used];
        else { img = GraphView.Tile(rt, nm, c, false); pool.Bars.Add(img); }
        used++;
        img.color = c;
        return img;
    }

    static void Place(Image img, Vector4 b)
    {
        var t = img.rectTransform;
        t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
        t.pivot = new Vector2(0f, 1f);
        t.anchoredPosition = new Vector2(b.x, -b.y);
        t.sizeDelta = new Vector2(b.z, b.w);
        if (!img.enabled) img.enabled = true;
    }

    // 横条池：挂在容器上，容器销毁池就跟着没了，不留悬挂引用
    sealed class BarPool : MonoBehaviour
    {
        public readonly List<Image> Bars = new List<Image>();
    }
}
