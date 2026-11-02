using System;
using System.Collections.Generic;
using Dayu.Core;

// 撤销/恢复：快照栈 + 指针。栈里存的是 GraphIO.Serialize 出来的文本而不是 Graph 对象，
// 因为 GraphView.LoadGraph 是引用赋值，历史里只要放对象，玩家下一次编辑就会把历史本身改掉。
// 纯 C#、不碰 UnityEngine，Editor 自测直接 new 出来跑。
public sealed class GraphHistory
{
    public const int MaxDepth = 60;   // 顶栏「撤销 60」封顶，再多对教学关卡没意义，还白占内存

    readonly List<string> snaps = new List<string>();
    int ptr = -1;                      // 指向「当前态」；ptr 之后的都是待恢复
    int batchDepth;
    string batched;                    // 批内最后一次改动（拖动/框选一笔会连调 N 次）

    public bool CanUndo { get { return ptr > 0; } }
    public bool CanRedo { get { return ptr >= 0 && ptr < snaps.Count - 1; } }
    public int UndoDepth { get { return ptr < 0 ? 0 : ptr; } }
    public int RedoDepth { get { return ptr < 0 ? 0 : snaps.Count - 1 - ptr; } }
    public int Depth { get { return snaps.Count; } }
    public bool InBatch { get { return batchDepth > 0; } }

    // 进关/读档/看答案时调用：历史整体作废，栈底就是这张新图
    public void Reset(Graph current)
    {
        snaps.Clear(); ptr = -1; batchDepth = 0; batched = null;
        Push(GraphIO.Serialize(current));
    }

    // 每笔「已提交」的改动调一次（新增/删除模块、连线、改参数、改完一次位置）。
    // 和栈顶等价就丢弃：拖了一下没挪动位置不该占一条历史，否则撤销要点好几下才退回去。
    public void Commit(Graph current)
    {
        var s = GraphIO.Serialize(current);
        if (batchDepth > 0) { batched = s; return; }
        PushIfChanged(s);
    }

    // 拖动、框选、连续输入这类「一笔改动会调 N 次 Commit」的操作包起来，只留一条历史
    public void BeginBatch() { batchDepth++; }

    public void EndBatch()
    {
        // 多余的 EndBatch（嵌套写歪了）直接忽略，绝不能让计数变负后再把后面的批全吞掉
        if (batchDepth <= 0) return;
        batchDepth--;
        if (batchDepth > 0) return;   // 外层还没结束，继续攒着
        var s = batched; batched = null;
        if (s != null) PushIfChanged(s);
    }

    // 返回要替换进去的新 Graph 对象（反序列化出来的独立副本），调用方直接拿去当活图用。
    public Graph Undo()
    {
        if (!CanUndo) return null;
        ptr--;
        return Restore();
    }

    public Graph Redo()
    {
        if (!CanRedo) return null;
        ptr++;
        return Restore();
    }

    // 当前态的副本，自动保存的 snapshot 委托可以直接给它
    public Graph Current() { return ptr < 0 ? null : Restore(); }

    Graph Restore()
    {
        string err;
        // 每次还原都是重新解析一份文本，所以 Undo() 返回的对象怎么改都不会动到栈里的历史
        var g = GraphIO.Deserialize(snaps[ptr], out err);
        return g;
    }

    void PushIfChanged(string s)
    {
        if (ptr >= 0 && Equivalent(snaps[ptr], s)) return;
        Push(s);
    }

    void Push(string s)
    {
        if (ptr < snaps.Count - 1) snaps.RemoveRange(ptr + 1, snaps.Count - 1 - ptr);   // 新改动作废待恢复的尾巴
        snaps.Add(s);
        ptr = snaps.Count - 1;
        // 只从最老那头丢，栈底那份永远留着：玩家可以一路撤销到进关时的样子
        while (snaps.Count > MaxDepth && ptr > 0) { snaps.RemoveAt(0); ptr--; }
    }

    // 文本相同就不用再解析了；拖动产生的 0.0001 级坐标差靠 GraphIO.Same 的容差吸收
    static bool Equivalent(string a, string b)
    {
        if (a == b) return true;
        string err;
        var x = GraphIO.Deserialize(a, out err);
        if (x == null) return false;
        var y = GraphIO.Deserialize(b, out err);
        return y != null && GraphIO.Same(x, y);
    }
}
