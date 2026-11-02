using System;
using System.Collections.Generic;

namespace Dayu.Core
{
    /// <summary>
    /// 按执行顺序算「走到某个节点时已经可见的变量」。
    /// 从入口沿执行线（out / true / false / body / done）走一遍：创建变量、读入变量让变量对后续节点可见；
    /// 循环变量只在循环体里可见，走 done 出口时收回。
    /// 走不到的孤立节点不入表，调用方据此退回全量变量，避免误报。
    /// </summary>
    public static class VarScope
    {
        public const string DefaultIndexName = "i";

        public static string IndexName(GraphNode loop)
        {
            var n = GraphUtil.Get(loop, "indexName");
            return string.IsNullOrEmpty(n) ? DefaultIndexName : n;
        }

        // 计数循环的方向：默认正序（起点往上走到终点）。倒序只改比较方向，起点 / 终点的含义不变。
        public const string DirAsc = "asc", DirDesc = "desc";

        public static bool IsDesc(GraphNode loop)
        {
            return GraphUtil.Get(loop, "dir") == DirDesc;
        }

        // 参数值 / 中文标签互转：界面上只有「正序」「倒序」两个词，节点里存的是 asc / desc。
        public static string DirOf(GraphNode loop) { return IsDesc(loop) ? DirDesc : DirAsc; }

        public static string DirLabelCn(string dir) { return dir == DirDesc ? "倒序" : "正序"; }

        public static Dictionary<string, Dictionary<string, string>> Snapshot(Graph g)
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (g == null || g.Nodes == null) return map;
            GraphNode entry = null;
            foreach (var n in g.Nodes) if (n != null && n.Type == "entry") { entry = n; break; }
            if (entry == null) return map;

            // 单调数据流：同一个节点可能被多条路径走到（分支汇合、循环回边），
            // 把这些路径的可见变量并起来；集合只会变大，不再变大就停止，环不会让它失控。
            var done = new HashSet<string>(StringComparer.Ordinal);
            var work = new Queue<Job>();
            work.Enqueue(new Job { Node = entry, Scope = new Dictionary<string, string>(StringComparer.Ordinal) });
            while (work.Count > 0)
            {
                var job = work.Dequeue();
                var n = job.Node;
                Dictionary<string, string> recorded;
                if (!map.TryGetValue(n.Uid, out recorded))
                {
                    recorded = new Dictionary<string, string>(StringComparer.Ordinal);
                    map[n.Uid] = recorded;
                }
                bool grew = false;
                foreach (var kv in job.Scope)
                    if (!recorded.ContainsKey(kv.Key)) { recorded[kv.Key] = kv.Value; grew = true; }
                if (!grew && done.Contains(n.Uid)) continue;
                done.Add(n.Uid);

                var after = new Dictionary<string, string>(recorded, StringComparer.Ordinal);
                Declare(g, n, after);

                if (n.Type == "forloop" || n.Type == "whileloop")
                {
                    var body = new Dictionary<string, string>(after, StringComparer.Ordinal);
                    body[IndexName(n)] = "int";
                    Push(g, n, "body", body, work);
                    Push(g, n, "done", after, work);
                }
                else if (n.Type == "branch")
                {
                    Push(g, n, "true", after, work);
                    Push(g, n, "false", after, work);
                }
                else Push(g, n, "out", after, work);
            }
            return map;
        }

        sealed class Job { public GraphNode Node; public Dictionary<string, string> Scope; }

        static void Push(Graph g, GraphNode from, string port, Dictionary<string, string> scope, Queue<Job> work)
        {
            var link = GraphUtil.ExecOutLink(g, from.Uid, port);
            if (link == null) return;
            var next = GraphUtil.FindNode(g, link.ToNode);
            if (next != null) work.Enqueue(new Job { Node = next, Scope = scope });
        }

        public static Dictionary<string, string> Visible(Graph g, string uid)
        {
            Dictionary<string, string> v;
            return uid != null && Snapshot(g).TryGetValue(uid, out v) ? v : null;
        }

        static void Declare(Graph g, GraphNode n, Dictionary<string, string> scope)
        {
            // 「创建栈 / 创建队列」这张卡就是容器实例的建档处：和数组一样，摆过这张卡才在它下面写 st.empty，
            // 关卡预置的实例定义（g.Structs）本身不算看见，免得没连创建卡也不报错。
            if (n.Type == "structnew")
            {
                var id = GraphUtil.Get(n, "structId");
                if (string.IsNullOrEmpty(id) || g == null || g.Structs == null) return;
                var def = g.Structs.Find(s => s != null && s.Id == id);
                if (def != null) scope[id] = Members.TypeOf(def.Kind, def.Elem);
                return;
            }
            if (n.Type != "vardecl" && n.Type != "input") return;
            var name = GraphUtil.Get(n, "name");
            if (string.IsNullOrEmpty(name)) return;
            string type = n.Type == "vardecl" ? GraphUtil.Get(n, "vtype") : GraphUtil.VarType(g, name);
            if (string.IsNullOrEmpty(type)) type = GraphUtil.Get(n, "vtype");
            scope[name] = string.IsNullOrEmpty(type) ? "int" : type;
        }
    }
}
