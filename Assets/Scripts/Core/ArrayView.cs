using System.Globalization;

namespace Dayu.Core
{
    // 数组可视化需要的纯计算，不碰 Unity，方便自测。
    public static class ArrayView
    {
        /// <summary>
        /// 认出「按下标写入」的跟踪文本，例如 a[2] = 5。
        /// 只读 a[i] 或比较 a[i] == 5 都不算写入，返回 false。
        /// </summary>
        public static bool TryParseIndexedWrite(string text, out string name, out int index)
        {
            name = null;
            index = -1;
            if (string.IsNullOrEmpty(text)) return false;

            int lb = text.IndexOf('[');
            if (lb <= 0) return false;
            int rb = text.IndexOf(']', lb + 1);
            if (rb <= lb + 1) return false;

            // 等号必须出现在 ] 之后，且不能是 == 比较
            int eq = text.IndexOf('=', rb + 1);
            if (eq < 0) return false;
            if (eq + 1 < text.Length && text[eq + 1] == '=') return false;
            if (text[eq] == '!' || text[eq] == '<' || text[eq] == '>') return false;

            name = text.Substring(0, lb).Trim();
            if (name.Length == 0) return false;

            int v;
            if (!int.TryParse(text.Substring(lb + 1, rb - lb - 1).Trim(),
                              NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
            if (v < 0) return false;
            index = v;
            return true;
        }

        /// <summary>车厢里显示的元素值：跟变量区同一套口径。</summary>
        public static string ElementText(object v)
        {
            if (v == null) return "null";
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double) return ((double)v).ToString("0.######", CultureInfo.InvariantCulture);
            if (v is char) return "'" + v + "'";
            return v.ToString();
        }
    }
}
