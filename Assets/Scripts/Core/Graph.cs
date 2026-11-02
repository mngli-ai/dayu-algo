using System;
using System.Collections.Generic;

namespace Dayu.Core
{
    public enum LinkKind { Exec, Data }
    public sealed class GraphNode { public string Uid; public string Type; public float X, Y; public Dictionary<string, string> Params = new Dictionary<string, string>(); }
    public sealed class GraphLink { public string FromNode, FromPort, ToNode, ToPort; public LinkKind Kind; }
    public sealed class StructDef { public string Id, Kind, Elem, Name; }
    public sealed class Graph { public string LevelId; public bool ExprMode; public List<StructDef> Structs = new List<StructDef>(); public List<GraphNode> Nodes = new List<GraphNode>(); public List<GraphLink> Links = new List<GraphLink>(); }

    public static class GraphUtil
    {
        public static GraphNode FindNode(Graph g, string uid) { if (g == null) return null; return g.Nodes.Find(n => n.Uid == uid); }
        public static GraphLink LinkInto(Graph g, string uid, string port) { if (g == null) return null; return g.Links.Find(l => l.Kind == LinkKind.Data && l.ToNode == uid && l.ToPort == port); }
        public static GraphLink ExecOutLink(Graph g, string uid, string port) { if (g == null) return null; return g.Links.Find(l => l.Kind == LinkKind.Exec && l.FromNode == uid && l.FromPort == port); }
        public static string OutType(GraphNode node, string port, Graph g, HashSet<string> seen = null)
        {
            if (node == null) return null; if (seen == null) seen = new HashSet<string>(); if (!seen.Add(node.Uid)) return null;
            var t = NodeTypes.Get(node.Type); if (t == null) return null; var p = t.Out.Find(x => x.Id == port); if (p == null) return null; if (p.Type != "any") return p.Type;
            if (node.Type == "const" || node.Type == "input") return Get(node, "vtype");
            if (node.Type == "sample_input") return "string";
            if (node.Type == "output") { var input = LinkInto(g, node.Uid, "value"); return input == null ? null : OutType(FindNode(g, input.FromNode), input.FromPort, g, new HashSet<string>(seen)); }
            if (node.Type == "vardecl" || node.Type == "varget" || node.Type == "varset") { var d = g.Nodes.Find(n => n.Type == "vardecl" && Get(n, "name") == Get(node, "name")); return d == null ? null : Get(d, "vtype"); }
            if (node.Type.StartsWith("stack_") || node.Type.StartsWith("queue_")) { var s = g.Structs.Find(x => x.Id == Get(node, "structId")); return s == null ? null : s.Elem; }
            if (node.Type == "binop") { var a = LinkInto(g, node.Uid, "a"); var b = LinkInto(g, node.Uid, "b"); var ta = a == null ? VarType(g, Get(node, "varA")) : OutType(FindNode(g, a.FromNode), a.FromPort, g, new HashSet<string>(seen)); var tb = b == null ? VarType(g, Get(node, "varB")) : OutType(FindNode(g, b.FromNode), b.FromPort, g, new HashSet<string>(seen)); foreach (var x in new[] { "int", "ll", "double" }) if (x == ta) return x; return tb ?? ta ?? "int"; }
            return null;
        }
        // 变量名读取统一去首尾空格：多打一个空格是「明明创建了却报没创建」最常见的来源，
        // 在唯一的读取出口修掉比每个调用点各 trim 一遍可靠。
        public static string Get(GraphNode n, string key)
        {
            string v;
            if (n == null || !n.Params.TryGetValue(key, out v)) return null;
            return key == "name" ? v.Trim() : v;
        }

        public static string VarType(Graph g, string name)
        {
            if (g == null || string.IsNullOrEmpty(name)) return null;
            var d = g.Nodes.Find(n => n != null && n.Type == "vardecl" && Get(n, "name") == name);
            return d == null ? null : Get(d, "vtype");
        }
    }
}
