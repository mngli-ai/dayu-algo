using System; using System.Collections.Generic;
namespace Dayu.Core {
 public sealed class CompileError { public string NodeUid,Message; public CompileError(string u,string m){NodeUid=u;Message=m;} }
 public sealed class Program { public Graph Graph; public Dictionary<string,string> VarTypes=new Dictionary<string,string>(); public string EntryUid; public List<StructDef> Structs=new List<StructDef>(); }
 public sealed class CompileResult { public bool Ok; public Program Program; public List<CompileError> Errors=new List<CompileError>(); }
 public static class Compiler {
  static bool Compat(string a,string b){if(string.IsNullOrEmpty(a)||string.IsNullOrEmpty(b)||a=="any"||b=="any"||a==b)return true;if(a=="char"&&b=="string")return true;int ra=Rank(a),rb=Rank(b);return ra>=0&&rb>=0&&ra<=rb;} static int Rank(string x){return x=="int"?0:x=="ll"?1:x=="double"?2:-1;}

  // 表达式语言只认 int/double 两种数值类型，图里的 ll 在这里按 int 处理；数组类型保留 [] 后缀。
  static string ExprType(string t){if(string.IsNullOrEmpty(t))return "int";if(t.EndsWith("[]",StringComparison.Ordinal))return ExprType(t.Substring(0,t.Length-2))+"[]";return t=="ll"?"int":t;}

  // 数组类型：int[] / double[] / string[] / char[]，元素类型就是去掉 [] 的部分。
  static bool IsArrayType(string t){return ExprLang.IsArrayType(t);}
  static string ElemType(string t){return ExprLang.ElementType(t);}

  // ExprLang.Check 会拒绝「表达式结果本身就是数组」，但创建变量 / 赋值恰恰要写数组，
  // 所以这里识别这一条特定错误，采用它给出的数组类型继续判断。
  static bool ArrayResult(ExprCheckResult chk){return chk!=null&&!chk.Ok&&!string.IsNullOrEmpty(chk.Error)&&chk.Error.Contains("输出结果不能是数组")&&IsArrayType(chk.Type);}

  // 全量类型表：走不到的孤立节点用它兜底，免得老图一改就满屏报错。
  // 栈 / 队列实例也登记进来（按 stack<元素> / queue<元素>），否则玩家写完 st.empty 会被当成「未声明」。
  static Dictionary<string,string> AllTypes(Program p,Graph g){var d=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var kv in p.VarTypes)d[kv.Key]=ExprType(kv.Value);foreach(var s in g.Structs)if(s!=null&&!string.IsNullOrEmpty(s.Id))d[s.Id]=Members.TypeOf(s.Kind,s.Elem);foreach(var n in g.Nodes)if(n!=null&&(n.Type=="forloop"||n.Type=="whileloop"))d[VarScope.IndexName(n)]="int";return d;}

  // 这个节点写表达式时能用哪些变量：可达节点按执行顺序实时算，循环变量只在循环体里算进来。
  static Dictionary<string,string> ScopeAt(Program p,Graph g,Dictionary<string,Dictionary<string,string>> scopes,GraphNode n)
  {
    var full=AllTypes(p,g);
    Dictionary<string,string> visible;
    if(n==null||scopes==null||!scopes.TryGetValue(n.Uid,out visible))return full;
    var d=new Dictionary<string,string>(StringComparer.Ordinal);
    foreach(var kv in visible)d[kv.Key]=ExprType(kv.Value);
    return d;
  }

  static bool IsNumericType(string t){return t=="int"||t=="double";}

  // 报错时把「这一步究竟看得见哪些变量」一起说出来，玩家不用猜程序认为有什么。
  static string Vis(Dictionary<string,string> scope)
  {
    if(scope==null||scope.Count==0)return "（此刻还没有任何可用变量）";
    var names=new List<string>(scope.Keys);names.Sort(StringComparer.Ordinal);
    return "（此刻可用的变量："+string.Join("、",names)+"）";
  }

  // 「未定义的变量」里那些其实已经声明、只是位置在后面的，换成可操作的提示。
  static string VisibleError(Dictionary<string,string> scope,Dictionary<string,string> full,string message)
  {
    if(message==null||message.IndexOf("未定义的变量")<0)return null;
    foreach(var name in full.Keys)if(!scope.ContainsKey(name)&&message.Contains("「"+name+"」"))return "变量「"+name+"」在这里还没创建：先把它上面的模块用执行线连进来，再在它下面写表达式。"+Vis(scope);
    return null;
  }

  // 读取 / 赋值指向了一个不存在的变量名：把图里真正创建过的变量列出来，方便对照拼写。
  static string UndeclaredMsg(GraphNode n,Graph g)
  {
    var name=GraphUtil.Get(n,"name");
    return "变量「"+name+"」不存在，图里没有这个名字的「创建变量」。"+DeclaredNames(g);
  }

  // 「我到底创建过哪些变量」——报错时把名单摊开，玩家不用自己回忆拼写
  static string DeclaredNames(Graph g)
  {
    var has=new List<string>();
    if(g!=null&&g.Nodes!=null)foreach(var x in g.Nodes)if(x!=null&&x.Type=="vardecl"){var nm=(GraphUtil.Get(x,"name")??"").Trim();if(!string.IsNullOrEmpty(nm)&&!has.Contains(nm))has.Add(nm);}
    has.Sort(StringComparer.Ordinal);
    return has.Count==0?"（目前一个变量都没创建）":"（已创建的变量："+string.Join("、",has)+"）";
  }

  public static CompileResult Compile(Graph g){
   var r=new CompileResult{Program=new Program{Graph=g,Structs=g.Structs}};
   var ns=g.Nodes;
   var es=ns.FindAll(n=>n.Type=="entry");
   if(es.Count!=1)r.Errors.Add(new CompileError(es.Count>0?es[0].Uid:null,"必须存在唯一的「开始」节点。"));
   r.Program.EntryUid=es.Count>0?es[0].Uid:null;
   foreach(var n in ns)if(n.Type=="vardecl"){var name=GraphUtil.Get(n,"name")??"";if(string.IsNullOrEmpty(name)||!IsIdent(name))r.Errors.Add(new CompileError(n.Uid,"变量名不是合法标识符。"));if(r.Program.VarTypes.ContainsKey(name))r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」重名。"));r.Program.VarTypes[name]=GraphUtil.Get(n,"vtype");}

   // 表达式字段：非空时用 ExprLang 做语法 + 类型校验，为空仍走旧的数据线校验。
    // 表达式字段按各自的可见范围校验：可达节点用执行顺序算出来的作用域，孤立节点退回全量变量。
    var scopes=VarScope.Snapshot(g);
    var fullTypes=AllTypes(r.Program,g);
    foreach(var n in ns)CheckExpr(g,n,r,ScopeAt(r.Program,g,scopes,n),fullTypes);

   var visiting=new HashSet<string>();var checkedSet=new HashSet<string>();foreach(var n in ns){var t=NodeTypes.Get(n.Type);if(t==null)continue;foreach(var p in t.In)Visit(g,n,p.Id,visiting,checkedSet,r.Errors);}
   // 循环空转：「循环体」出口一根执行线都没接时，运行会把轮次事件发满却什么都不做，
   // 玩家在跟踪栏看见的只有「第 N 轮循环」，根本想不起来自己漏了模块，所以这里直接报错。
   foreach(var n in ns){if(n.Type!="forloop"&&n.Type!="whileloop")continue;if(GraphUtil.ExecOutLink(g,n.Uid,"body")==null)r.Errors.Add(new CompileError(n.Uid,"「"+(n.Type=="forloop"?"计数循环":"条件循环")+"」的「循环体」出口还没有接模块，循环会空转。"));}
   foreach(var l in g.Links){if(l.Kind==LinkKind.Exec&&l.ToPort!="in")r.Errors.Add(new CompileError(l.ToNode,"执行线必须连接到「in」端口。"));if(l.Kind==LinkKind.Exec&&g.Links.FindAll(x=>x.Kind==LinkKind.Exec&&x.FromNode==l.FromNode&&x.FromPort==l.FromPort).Count>1)r.Errors.Add(new CompileError(l.FromNode,"一个出口只走一条执行线：从同一个出口拉出的新线会顶掉旧线。如果两根线都在，说明它们在图数据里重复了，请删掉多余的那根。"));if(l.Kind==LinkKind.Data&&g.Links.FindAll(x=>x.Kind==LinkKind.Data&&x.ToNode==l.ToNode&&x.ToPort==l.ToPort).Count>1)r.Errors.Add(new CompileError(l.ToNode,"一个数据输入只能连接一条线。"));}
   foreach(var n in ns){var t=NodeTypes.Get(n.Type);if(t==null)continue;{var nm0=GraphUtil.Get(n,"name")??"";if((n.Type=="varget"||(n.Type=="varset"&&Assign.TryParse(GraphUtil.Get(n,"valueExpr"))==null))&&nm0.Length>0&&!r.Program.VarTypes.ContainsKey(nm0))r.Errors.Add(new CompileError(n.Uid,UndeclaredMsg(n,g)));}if(n.Type=="sample_compare"&&!IsSampleExprMode(n)&&GraphUtil.LinkInto(g,n.Uid,"value")==null)r.Errors.Add(new CompileError(n.Uid,"样例对比板需要连接一个输出值。"));if(n.Type=="binop"){var ex=GraphUtil.Get(n,"expr");if(!string.IsNullOrEmpty(ex)){var ws=ExprFormat.Parse(ex);foreach(var em in ExprFormat.Validate(ws,g))r.Errors.Add(new CompileError(n.Uid,em));}else{if(GraphUtil.LinkInto(g,n.Uid,"a")==null&&string.IsNullOrEmpty(GraphUtil.Get(n,"varA")))r.Errors.Add(new CompileError(n.Uid,"运算块缺少操作数 A，请打开编辑窗口选择变量。"));if(GraphUtil.LinkInto(g,n.Uid,"b")==null&&string.IsNullOrEmpty(GraphUtil.Get(n,"varB")))r.Errors.Add(new CompileError(n.Uid,"运算块缺少操作数 B，请打开编辑窗口选择变量。"));if(!string.IsNullOrEmpty(GraphUtil.Get(n,"varA"))&&GraphUtil.VarType(g,GraphUtil.Get(n,"varA"))==null)r.Errors.Add(new CompileError(n.Uid,"变量「"+GraphUtil.Get(n,"varA")+"」不存在。"));if(!string.IsNullOrEmpty(GraphUtil.Get(n,"varB"))&&GraphUtil.VarType(g,GraphUtil.Get(n,"varB"))==null)r.Errors.Add(new CompileError(n.Uid,"变量「"+GraphUtil.Get(n,"varB")+"」不存在。"));}}if(n.Type.StartsWith("stack")||n.Type.StartsWith("queue")||n.Type=="structnew"){var sd=g.Structs.Find(s=>s.Id==GraphUtil.Get(n,"structId"));if(sd==null)r.Errors.Add(new CompileError(n.Uid,"还没有这个结构实例，请先接一个「创建栈」或「创建队列」。"));else{bool wantQueue=n.Type.StartsWith("queue");if((sd.Kind=="queue")!=wantQueue&&n.Type!="structnew")r.Errors.Add(new CompileError(n.Uid,"「"+GraphUtil.Get(n,"structId")+"」是"+(sd.Kind=="queue"?"队列":"栈")+"，用不了"+(wantQueue?"队列":"栈")+"的模块。"));}}foreach(var p in t.In){var l=GraphUtil.LinkInto(g,n.Uid,p.Id);if(!p.Optional&&l==null&&!NodeTypes.HasExpr(n,p.Id)&&!(n.Type=="varset"&&string.IsNullOrEmpty(GraphUtil.Get(n,"name"))))r.Errors.Add(new CompileError(n.Uid,"这个模块还缺一个「"+(string.IsNullOrEmpty(p.Label)?p.Id:p.Label)+"」输入。"));if(l!=null){var a=GraphUtil.OutType(GraphUtil.FindNode(g,l.FromNode),l.FromPort,g);if(!Compat(a,p.Type))r.Errors.Add(new CompileError(n.Uid,"需要「"+p.Type+"」，但连过来的是「"+a+"」。"));else if(IsArrayType(a)&&!(n.Type=="varset"&&p.Id=="value"))r.Errors.Add(new CompileError(n.Uid,"数组「"+a+"」不能直接连到「"+(string.IsNullOrEmpty(p.Label)?p.Id:p.Label)+"」端口，请用 a[i] 取元素。"));}}}
   r.Ok=r.Errors.Count==0;return r;
  }

  // 样例对比板：新模式下预期值直接取运行输出，不再要求连 value 数据线。
  static bool IsSampleExprMode(GraphNode n){return !string.IsNullOrEmpty(GraphUtil.Get(n,"exprMode"))||!string.IsNullOrEmpty(GraphUtil.Get(n,"expr"));}

  // 表达式字段统一按「这个节点当前看得见哪些变量」校验。
  static ExprCheckResult Chk(Dictionary<string,string> scope,Dictionary<string,string> full,string src)
  {
    var chk=ExprLang.Check(src,scope);
    if(!chk.Ok){var hint=VisibleError(scope,full,chk.Error);if(hint!=null){chk.Error=hint;chk.Type="unknown";}}
    return chk;
  }

  static void CheckExpr(Graph g,GraphNode n,CompileResult r,Dictionary<string,string> scope,Dictionary<string,string> full){
   switch(n.Type){
    case "vardecl":{
     var src=GraphUtil.Get(n,"initExpr");
     var vt=GraphUtil.Get(n,"vtype");var name=GraphUtil.Get(n,"name");
     // 数组长度：只有数组能写，而且与初始值表达式二选一（两个都填就说不清到底铺几个元素）
     var sizeSrc=GraphUtil.Get(n,"size");
     if(!string.IsNullOrEmpty(sizeSrc)){
      if(!IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是「"+vt+"」，不是数组，写不了长度。"));return;}
      if(!string.IsNullOrEmpty(src)){r.Errors.Add(new CompileError(n.Uid,"数组「"+name+"」的「长度」和「初始值表达式」只能填一个：填长度就按默认值铺满，填初始值就以初始值为准。"));return;}
      var sz=Chk(scope,full,sizeSrc);
      if(!sz.Ok){r.Errors.Add(new CompileError(n.Uid,"数组「"+name+"」的长度表达式有误："+sz.Error));return;}
      if(sz.Type!="int"&&sz.Type!="ll")r.Errors.Add(new CompileError(n.Uid,"数组长度必须是整数，实际类型是「"+sz.Type+"」。"));
      return;}
     if(string.IsNullOrEmpty(src))return;
     var chk=Chk(scope,full,src);
     if(ArrayResult(chk)){
      if(!IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是「"+vt+"」，但初始值表达式得到的是数组「"+chk.Type+"」，请把变量类型改成数组。"));return;}
      if(ElemType(vt)!=ElemType(chk.Type))r.Errors.Add(new CompileError(n.Uid,"初始值表达式的元素类型是「"+ElemType(chk.Type)+"」，不能赋给「"+vt+"」类型的变量「"+name+"」。"));
      return;}
     if(!chk.Ok){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的初始值表达式有误："+chk.Error));return;}
     if(IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是数组「"+vt+"」，初始值表达式必须给出数组。"));return;}
     if(!Compat(chk.Type,ExprType(vt)))r.Errors.Add(new CompileError(n.Uid,"初始值表达式的类型是「"+chk.Type+"」，不能直接赋给「"+vt+"」类型的变量「"+name+"」。"));
     return;}
    case "varset":{
     // 两种写法都收：sum = 表达式（整段赋值）和 a[i] = 表达式（按下标写一个元素）。
     var src=GraphUtil.Get(n,"valueExpr");
     var asg=Assign.TryParse(src);
     var name=asg!=null?asg.Name:GraphUtil.Get(n,"name");
     var vt=string.IsNullOrEmpty(name)?null:GraphUtil.VarType(g,name);
     // 赋值模块自己带的 vtype 只是旧图留下的提示；写错了要报错，避免和创建变量声明的类型打架。
     var own=GraphUtil.Get(n,"vtype");
     if(!string.IsNullOrEmpty(own)&&vt!=null&&own!=vt)r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是「"+vt+"」，赋值模块里写的类型是「"+own+"」，两者要一致。"));
     // 只有一个填空的「赋值」面板可能整句没写，也可能只写了等号左边，这里明确说缺什么，
     // 别让 Vm 静默把值存进一个空名字的变量。名字为空时下面那条「缺 value 输入」不再重复报。
     // 等号写了但整句没解析成功（a = = x 这种）不能再说「还没写出要存给哪个变量」——
     // 名字明明写了，照抄玩家那句才指得出问题在哪。
     if(string.IsNullOrEmpty(name)){
      r.Errors.Add(new CompileError(n.Uid,Assign.TopLevelEqualsAt(src)==-1
        ?"「赋值」还没写出要存给哪个变量，格式是 名字 = 表达式。"
        :"「赋值」这句没写对：「"+src+"」，格式是 名字 = 表达式，例如 a = 0、a[i] = x、s += 1。"));return;}
     if(string.IsNullOrEmpty(src))return;
     if(asg==null){
      var chk=Chk(scope,full,src);
      if(ArrayResult(chk)){
       if(vt==null||!IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」不是数组类型，不能赋值为数组「"+chk.Type+"」。"));return;}
       if(ElemType(vt)!=ElemType(chk.Type))r.Errors.Add(new CompileError(n.Uid,"赋值表达式的元素类型是「"+ElemType(chk.Type)+"」，不能赋给「"+vt+"」类型的变量「"+name+"」。"));
       return;}
      if(!chk.Ok){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的赋值表达式有误："+chk.Error));return;}
      if(vt!=null&&IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是数组「"+vt+"」，赋值表达式必须给出数组。"));return;}
      if(vt!=null&&!Compat(chk.Type,ExprType(vt)))r.Errors.Add(new CompileError(n.Uid,"赋值表达式的类型是「"+chk.Type+"」，不能直接赋给「"+vt+"」类型的变量「"+name+"」。"));
      return;}
     if(vt==null){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」还没创建，不能给它赋值。先放一个「创建变量」。"+DeclaredNames(g)));return;}
     if(asg.IsIndexed){
      if(!IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是「"+vt+"」，不是数组，写不成 "+asg.LeftText+"。"));return;}
      var idx=Chk(scope,full,asg.IndexExpr);
      if(!idx.Ok)r.Errors.Add(new CompileError(n.Uid,"下标表达式有误："+idx.Error));
      else if(idx.Type!="int")r.Errors.Add(new CompileError(n.Uid,"下标必须是整数，实际类型是「"+idx.Type+"」。"));
      var rhs=Chk(scope,full,asg.Rhs);
      if(ArrayResult(rhs)){r.Errors.Add(new CompileError(n.Uid,asg.LeftText+" 的右边要给出一个元素，表达式得到的是数组「"+rhs.Type+"」，整段赋值请写成 "+name+" = …。"));return;}
      if(!rhs.Ok){r.Errors.Add(new CompileError(n.Uid,"写入值的表达式有误："+rhs.Error));return;}
      if(!Compat(rhs.Type,ExprType(ElemType(vt))))r.Errors.Add(new CompileError(n.Uid,"写入值的类型是「"+rhs.Type+"」，与数组「"+name+"」（"+vt+"）的元素类型不匹配。"));
      return;}
     var whole=Chk(scope,full,asg.Rhs);
     if(ArrayResult(whole)){
      if(!IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」不是数组类型，不能赋值为数组「"+whole.Type+"」。"));return;}
      if(ElemType(vt)!=ElemType(whole.Type))r.Errors.Add(new CompileError(n.Uid,"赋值表达式的元素类型是「"+ElemType(whole.Type)+"」，不能赋给「"+vt+"」类型的变量「"+name+"」。"));
      return;}
     if(!whole.Ok){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的赋值表达式有误："+whole.Error));return;}
     if(IsArrayType(vt)){r.Errors.Add(new CompileError(n.Uid,"变量「"+name+"」的类型是数组「"+vt+"」，赋值表达式必须给出数组。"));return;}
     if(!Compat(whole.Type,ExprType(vt)))r.Errors.Add(new CompileError(n.Uid,"赋值表达式的类型是「"+whole.Type+"」，不能直接赋给「"+vt+"」类型的变量「"+name+"」。"));
     return;}
    case "output":{
     var src=GraphUtil.Get(n,"outputExpr");if(string.IsNullOrEmpty(src))return;
     var chk=Chk(scope,full,src);
     if(!chk.Ok)r.Errors.Add(new CompileError(n.Uid,"输出表达式有误："+chk.Error));
     return;}
    case "branch":
    case "whileloop":{
     var src=GraphUtil.Get(n,"condExpr");if(string.IsNullOrEmpty(src))return;
     var chk=Chk(scope,full,src);
     if(!chk.Ok){r.Errors.Add(new CompileError(n.Uid,"条件表达式有误："+chk.Error));return;}
     if(chk.Type!="bool")r.Errors.Add(new CompileError(n.Uid,"条件表达式必须是 bool，实际类型是「"+chk.Type+"」。"));
     return;}
    case "forloop":{
     var from=GraphUtil.Get(n,"fromExpr");var to=GraphUtil.Get(n,"toExpr");
     var indexName=VarScope.IndexName(n);
     if(!IsIdent(indexName))r.Errors.Add(new CompileError(n.Uid,"循环变量名「"+indexName+"」不是合法标识符。"));
     var scope2=new Dictionary<string,string>(scope,StringComparer.Ordinal);scope2[indexName]="int";
     if(!string.IsNullOrEmpty(from)){var chk=Chk(scope2,full,from);if(!chk.Ok)r.Errors.Add(new CompileError(n.Uid,"起始表达式有误："+chk.Error));else if(!IsNumericType(chk.Type))r.Errors.Add(new CompileError(n.Uid,"起始表达式必须是整数，实际类型是「"+chk.Type+"」。"));}
     if(!string.IsNullOrEmpty(to)){var chk=Chk(scope2,full,to);if(!chk.Ok)r.Errors.Add(new CompileError(n.Uid,"结束表达式有误："+chk.Error));else if(!IsNumericType(chk.Type))r.Errors.Add(new CompileError(n.Uid,"结束表达式必须是整数，实际类型是「"+chk.Type+"」。"));}
     return;}
    case "input":{
     var name=GraphUtil.Get(n,"name");if(string.IsNullOrEmpty(name))return;
     string tn,tix;
     if(!Assign.TryParseTarget(name,out tn,out tix)){r.Errors.Add(new CompileError(n.Uid,"要写入的位置「"+name+"」认不出来：写变量名（如 x）或数组元素（如 a[i]）。"));return;}
     if(tix==null){
      if(!r.Program.VarTypes.ContainsKey(tn))r.Errors.Add(new CompileError(n.Uid,"要写入的变量「"+tn+"」尚未声明。"));
      else if(ExprLang.IsArrayType(r.Program.VarTypes[tn]))r.Errors.Add(new CompileError(n.Uid,"「"+tn+"」是数组，要写进元素请带下标，如 "+tn+"[i]。"));
      return;}
     string vt2;
     if(!r.Program.VarTypes.TryGetValue(tn,out vt2)){r.Errors.Add(new CompileError(n.Uid,"要写入的数组「"+tn+"」尚未创建。"));return;}
     if(!ExprLang.IsArrayType(vt2)){r.Errors.Add(new CompileError(n.Uid,"「"+tn+"」不是数组，不能带下标写入。"));return;}
     var chk2=Chk(scope,full,tix);
     if(!chk2.Ok){r.Errors.Add(new CompileError(n.Uid,"下标表达式有误："+chk2.Error));return;}
     if(!IsNumericType(chk2.Type)){r.Errors.Add(new CompileError(n.Uid,"下标必须是整数，实际类型是「"+chk2.Type+"」。"));return;}
     var elem2=ExprLang.ElementType(vt2);var want=GraphUtil.Get(n,"vtype");
     if(!string.IsNullOrEmpty(want)&&elem2!=want)r.Errors.Add(new CompileError(n.Uid,"数组「"+tn+"」装的是「"+elem2+"」，读取类型要选「"+elem2+"」而不是「"+want+"」。"));
     return;}
   }
  }

  static bool IsIdent(string x){if(x.Length==0||(!char.IsLetter(x[0])&&x[0]!='_'&&x[0]<'\u3400'))return false;for(int i=1;i<x.Length;i++)if(!char.IsLetterOrDigit(x[i])&&x[i]!='_'&&x[i]<'\u3400')return false;return true;}
  static void Visit(Graph g,GraphNode n,string p,HashSet<string> v,HashSet<string> c,List<CompileError> e){string k=n.Uid+":"+p;if(v.Contains(k)){e.Add(new CompileError(n.Uid,"数据连接出现了环，请检查这几根线。"));return;}if(c.Contains(k))return;var l=GraphUtil.LinkInto(g,n.Uid,p);if(l==null){c.Add(k);return;}v.Add(k);var s=GraphUtil.FindNode(g,l.FromNode);if(s!=null){var t=NodeTypes.Get(s.Type);if(t!=null)foreach(var q in t.In)Visit(g,s,q.Id,v,c,e);}v.Remove(k);c.Add(k);}
 }
}
