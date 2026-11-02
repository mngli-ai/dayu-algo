using System; using System.Collections.Generic; using System.Globalization; using System.Text;
namespace Dayu.Core {
 public static class Vm {
  sealed class St {public StructDef Def;public List<Val> Items=new List<Val>();public Val Last;}
  public static RunResult Run(Program p,string input,int maxSteps=20000){
   var z=new RunResult{Ok=true};var env=new Dictionary<string,Val>();var arrs=new Dictionary<string,List<Val>>();var sts=new Dictionary<string,St>();var vals=new Dictionary<string,Val>();var tok=(input??"").Trim();var ts=tok.Length==0?new string[0]:tok.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);int ti=0,depth=0;var cache=new Dictionary<string,Val>();var error=false;
   Action<GraphNode,string,string,string> ev=(n,k,text,outv)=>{var e=new TraceEvent{I=z.Events.Count,Kind=k,Uid=n==null?null:n.Uid,Text=text,Depth=depth,Output=outv};foreach(var x in env)e.Vars[x.Key]=DisplayObj(x.Value);foreach(var x in arrs)e.Vars[x.Key]=ArrayText(x.Value);foreach(var x in arrs)e.Arrays[x.Key]=ToObjList(x.Value);foreach(var x in sts)e.Structs.Add(new StructSnap{Id=x.Key,Kind=x.Value.Def.Kind,Name=x.Value.Def.Name,Items=x.Value.Items.ConvertAll(DisplayObj)});z.Events.Add(e);};
   Action<GraphNode,string> fail=(n,m)=>{error=true;z.Error=new VmError{NodeUid=n==null?null:n.Uid,Message=m,Seq=z.Events.Count};ev(n,"error",m,null);};

   // 表达式求值：标量取自 env，数组取自 arrs，容器实例取自 sts，三者一起交给 ExprLang；结果再按目标类型转回 Val。
   Func<GraphNode,string,object> evalText=null;
   evalText=(n,src)=>{
    if(string.IsNullOrEmpty(src))return null;
    var parsed=ExprLang.Parse(src);
    if(!parsed.Ok){fail(n,parsed.Error);return null;}
    var used=new HashSet<string>();CollectVars(parsed.Root,used);
    foreach(var nm in used){var vt=GraphUtil.VarType(p.Graph,nm);if(vt!=null&&ExprLang.IsArrayType(vt)&&!arrs.ContainsKey(nm)){fail(n,"数组变量「"+nm+"」尚未创建，请先执行「创建变量」。");return null;}
     var sd=p.Structs!=null?p.Structs.Find(x=>x!=null&&x.Id==nm):null;if(sd!=null&&!sts.ContainsKey(nm)){fail(n,(sd.Kind=="queue"?"队列":"栈")+"「"+nm+"」还没创建，请先执行「创建"+(sd.Kind=="queue"?"队列":"栈")+"」。");return null;}}
    var scalars=new Dictionary<string,object>();foreach(var kv in env)scalars[kv.Key]=ToObj(kv.Value);
    var arrays=new Dictionary<string,List<object>>();foreach(var kv in arrs)arrays[kv.Key]=ToObjList(kv.Value);
    var conts=new Dictionary<string,ExprContainer>();foreach(var kv in sts){var d=kv.Value.Def;if(d==null)continue;conts[kv.Key]=new ExprContainer(Members.TypeOf(d.Kind,d.Elem),ToObjList(kv.Value.Items));}
    var res=ExprLang.Eval(parsed.Root,scalars,arrays,conts);
    if(!res.Ok){fail(n,res.Error);return null;}
    return res.Value;
   };
   Func<GraphNode,string,object> evalObj=(n,key)=>evalText(n,GraphUtil.Get(n,key));
   Func<GraphNode,string,string,Val> evalField=(n,key,prefer)=>{var o=evalObj(n,key);if(o==null)return error?Val.Int(0):Val.Null();return ToVal(o,prefer);};

   Func<GraphNode,string,Val> val=null;val=(n,port)=>{var key=n.Uid+":"+port;if(cache.ContainsKey(key))return cache[key];var l=GraphUtil.LinkInto(p.Graph,n.Uid,port);if(l==null){if(n.Type=="binop"&&(port=="a"||port=="b")){var vn=GraphUtil.Get(n,port=="a"?"varA":"varB");if(!string.IsNullOrEmpty(vn)){Val vv=env.ContainsKey(vn)?env[vn]:Val.Null();cache[key]=vv;return vv;}}fail(n,"输入「"+port+"」未连接。");return Val.Int(0);}var s=GraphUtil.FindNode(p.Graph,l.FromNode);Val v;switch(s.Type){case "const":v=Parse(GraphUtil.Get(s,"value"),GraphUtil.Get(s,"vtype"));break;case "varget":case "varset":case "vardecl":{var nm=GraphUtil.Get(s,"name");Val vv;if(string.IsNullOrEmpty(nm))v=Val.Null();else if(env.TryGetValue(nm,out vv))v=vv;else if(arrs.ContainsKey(nm)){fail(s,"数组变量「"+nm+"」不能直接当标量使用，请用 "+nm+"[i] 取元素。");v=Val.Null();}else v=Val.Null();break;}case "input":v=vals.ContainsKey(s.Uid)?vals[s.Uid]:Val.Null();break;case "binop":{var ex=GraphUtil.Get(s,"expr");if(!string.IsNullOrEmpty(ex)){var ws=ExprFormat.Parse(ex);v=ExprFormat.Eval(ws,name=>env.ContainsKey(name)?env[name]:Val.Int(0));}else{Val a=val(s,"a"); Val b=val(s,"b");v=Op(GraphUtil.Get(s,"op"),a,b);}break;}case "compare":{Val a=val(s,"a"); Val b=val(s,"b");v=Cmp(GraphUtil.Get(s,"op"),a,b);break;}case "logic":{var a=val(s,"a");v=GraphUtil.Get(s,"op")=="not"?Val.Bool(!a.AsBool()):Val.Bool(GraphUtil.Get(s,"op")=="and"?a.AsBool()&&val(s,"b").AsBool():a.AsBool()||val(s,"b").AsBool());break;}case "stack_pop":case "queue_pop":v=sts.ContainsKey(GraphUtil.Get(s,"structId"))?sts[GraphUtil.Get(s,"structId")].Last:Val.Null();break;case "stack_top":{var q=sts[GraphUtil.Get(s,"structId")];if(q.Items.Count==0){fail(s,"栈为空，无法查看顶端。");return Val.Int(0);}v=q.Items[q.Items.Count-1];break;}case "stack_empty":v=Val.Bool(!sts.ContainsKey(GraphUtil.Get(s,"structId"))||sts[GraphUtil.Get(s,"structId")].Items.Count==0);break;case "stack_size":v=Val.Int(sts.ContainsKey(GraphUtil.Get(s,"structId"))?sts[GraphUtil.Get(s,"structId")].Items.Count:0);break;case "queue_front":{var q=sts[GraphUtil.Get(s,"structId")];if(q.Items.Count==0){fail(s,"队列为空，无法查看队头。");return Val.Int(0);}v=q.Items[0];break;}case "queue_empty":v=Val.Bool(!sts.ContainsKey(GraphUtil.Get(s,"structId"))||sts[GraphUtil.Get(s,"structId")].Items.Count==0);break;case "queue_size":v=Val.Int(sts.ContainsKey(GraphUtil.Get(s,"structId"))?sts[GraphUtil.Get(s,"structId")].Items.Count:0);break;case "text_index":v=Val.Char(val(s,"s").AsString()[(int)val(s,"i").AsLong()]);break;case "len_of":v=Val.Int(val(s,"v").AsString().Length);break;case "forloop":v=env.ContainsKey("__index")?env["__index"]:Val.Int(0);break;default:v=Val.Int(0);break;}cache[key]=v;return v;};

   // 顺着数据线取数组：来源必须是数组变量的读取节点。
   Func<GraphNode,string,List<object>> arrayOf=(n,port)=>{
    var l=GraphUtil.LinkInto(p.Graph,n.Uid,port);if(l==null)return null;
    var s=GraphUtil.FindNode(p.Graph,l.FromNode);if(s==null)return null;
    if(s.Type!="varget"&&s.Type!="varset"&&s.Type!="vardecl")return null;
    var nm=GraphUtil.Get(s,"name");if(string.IsNullOrEmpty(nm))return null;
    List<Val> src;if(!arrs.TryGetValue(nm,out src))return null;
    return ToObjList(src);
   };

   Action<GraphLink> chain=null;Action<GraphNode,string> next=(n,port)=>{var l=GraphUtil.ExecOutLink(p.Graph,n.Uid,port);if(l!=null)chain(l);};
   chain=l=>{if(error)return;if(++z.Steps>maxSteps){fail(GraphUtil.FindNode(p.Graph,l.ToNode),"步数超限。");return;}cache.Clear();depth++;var n=GraphUtil.FindNode(p.Graph,l.ToNode);if(n==null)return;switch(n.Type){
    case "entry":next(n,"out");break;
    case "vardecl":{var vt=GraphUtil.Get(n,"vtype");var nm=GraphUtil.Get(n,"name");
     if(ExprLang.IsArrayType(vt)){arrs[nm]=new List<Val>();var elem=ExprLang.ElementType(vt);
      // 长度：先按默认值铺满 N 格，等价于 C++ 的 vector::resize；负数按 0 处理，两边都不会炸
      var sizeSrc=GraphUtil.Get(n,"size");
      if(!string.IsNullOrEmpty(sizeSrc)){var so=evalText(n,sizeSrc);if(error)break;long sn;
       if(!AsIndex(so,out sn)){fail(n,"数组「"+nm+"」的长度必须是整数。");break;}
       if(sn>1000000){fail(n,"数组「"+nm+"」的长度过大："+sn+"（最多 1000000）。");break;}
       for(long k=0;k<sn;k++)arrs[nm].Add(DefaultVal(elem));}
      if(!string.IsNullOrEmpty(GraphUtil.Get(n,"initExpr"))){arrs[nm].Clear();var o=evalObj(n,"initExpr");if(!error){var list=o as List<object>;if(list==null)fail(n,"变量「"+nm+"」的类型是数组「"+vt+"」，初始值表达式必须给出数组。");else foreach(var it in list)arrs[nm].Add(ToElemVal(it,elem));}}
      ev(n,"step","创建数组变量 "+nm+(arrs[nm].Count>0?"（长度 "+arrs[nm].Count+"）":""),null);}
     else{var v=!string.IsNullOrEmpty(GraphUtil.Get(n,"initExpr"))?evalField(n,"initExpr",vt):Parse(null,vt);env[nm]=v;ev(n,"step","创建变量 "+nm,null);}
     next(n,"out");break;}
    case "varset":{var src=GraphUtil.Get(n,"valueExpr");var asg=Assign.TryParse(src);
     var nm=asg!=null?asg.Name:GraphUtil.Get(n,"name");var vt=GraphUtil.VarType(p.Graph,nm);
     // a[i] = x：按下标写一个元素，下标超出当前长度时按 vector::resize 的语义补默认值，跟生成的 C++ 保持一致。
     if(asg!=null&&asg.IsIndexed){
      List<Val> arr;if(string.IsNullOrEmpty(nm)||!arrs.TryGetValue(nm,out arr)){fail(n,"数组变量「"+nm+"」尚未创建，请先执行「创建变量」。");break;}
      const long maxIndex=1000000;
      var io=evalText(n,asg.IndexExpr);if(error)break;long idx;
      if(!AsIndex(io,out idx)){fail(n,"数组下标必须是整数。");break;}
      if(idx<0){fail(n,"数组下标不能是负数："+idx+"。");break;}
      if(idx>maxIndex){fail(n,"数组下标过大："+idx+"（最多 "+maxIndex+"）。");break;}
      var vo=evalText(n,asg.Rhs);if(error)break;
      var elem=ExprLang.ElementType(vt??("int[]"));
      while(arr.Count<=idx)arr.Add(DefaultVal(elem));
      arr[(int)idx]=ToElemVal(vo,elem);
      ev(n,"varset",asg.LeftText+" = "+arr[(int)idx].ToDisplayString(),null);
      next(n,"out");break;}
     var rhs=asg!=null?asg.Rhs:src;
     if(ExprLang.IsArrayType(vt)){object o=!string.IsNullOrEmpty(rhs)?evalText(n,rhs):arrayOf(n,"value");
      if(!error){var list=o as List<object>;if(list==null)fail(n,"变量「"+nm+"」的类型是数组「"+vt+"」，赋值必须给出数组。");else{var elem=ExprLang.ElementType(vt);var nl=new List<Val>();foreach(var it in list)nl.Add(ToElemVal(it,elem));arrs[nm]=nl;}}
      ev(n,"varset",nm+" = "+ArrayText(arrs.ContainsKey(nm)?arrs[nm]:new List<Val>()),null);}
     else{Val v;if(!string.IsNullOrEmpty(rhs)){var o=evalText(n,rhs);if(error)break;v=ToVal(o,vt);}else v=val(n,"value");
      env[nm]=v;vals[n.Uid]=v;ev(n,"varset",(asg!=null?asg.LeftText:nm)+" = "+v.ToDisplayString(),null);}
     next(n,"out");break;}
    case "input":{if(ti>=ts.Length){fail(n,"输入数据不足。");break;}var v=Parse(ts[ti++],GraphUtil.Get(n,"vtype"));vals[n.Uid]=v;var nm=GraphUtil.Get(n,"name");string inName,inIdx;
     if(!string.IsNullOrEmpty(nm)&&Assign.TryParseTarget(nm,out inName,out inIdx)&&inIdx!=null){
      List<Val> iarr;if(!arrs.TryGetValue(inName,out iarr)){fail(n,"数组变量「"+inName+"」尚未创建，请先执行「创建变量」。");break;}
      var io=evalText(n,inIdx);if(error)break;long iidx;
      if(!AsIndex(io,out iidx)){fail(n,"数组下标必须是整数。");break;}
      if(iidx<0){fail(n,"数组下标不能是负数："+iidx+"。");break;}
      if(iidx>1000000){fail(n,"数组下标过大："+iidx+"（最多 1000000）。");break;}
      var ielem=ExprLang.ElementType(GraphUtil.VarType(p.Graph,inName)??("int[]"));
      while(iarr.Count<=iidx)iarr.Add(DefaultVal(ielem));
      // ToElemVal 收的是表达式运行时的 object，Val 要先过 ToObj，否则整个值会被当成认不出的类型丢掉
      iarr[(int)iidx]=ToElemVal(ToObj(v),ielem);
      ev(n,"input","输入 "+v.ToDisplayString()+" → "+inName+"["+iidx+"]",null);next(n,"out");break;}
     if(!string.IsNullOrEmpty(nm))env[nm]=v;ev(n,"input","输入 "+v.ToDisplayString(),null);next(n,"out");break;}
    case "output":{var v=!string.IsNullOrEmpty(GraphUtil.Get(n,"outputExpr"))?evalField(n,"outputExpr",null):val(n,"value");var line=v.ToDisplayString();z.Output=string.IsNullOrEmpty(z.Output)?line:z.Output+"\n"+line;ev(n,"output","输出 "+line,line);next(n,"out");break;}
    case "branch":{var b=!string.IsNullOrEmpty(GraphUtil.Get(n,"condExpr"))?evalField(n,"condExpr",null).AsBool():val(n,"cond").AsBool();ev(n,"branch",b?"条件成立 → 走「真」":"条件不成立 → 走「假」",null);next(n,b?"true":"false");break;}
    case "forloop":{long a=!string.IsNullOrEmpty(GraphUtil.Get(n,"fromExpr"))?evalField(n,"fromExpr","int").AsLong():val(n,"from").AsLong();long b=!string.IsNullOrEmpty(GraphUtil.Get(n,"toExpr"))?evalField(n,"toExpr","int").AsLong():val(n,"to").AsLong();long guard=0;
     var name=VarScope.IndexName(n);
     // 循环变量按节点上选的名字绑定，同时留一份旧的 __index 给数据端口；嵌套靠保存/恢复，内层结束外层还在。
     var hadI=env.ContainsKey(name);var prevI=hadI?env[name]:Val.Int(0);
     var hadIdx=env.ContainsKey("__index");var prevIdx=hadIdx?env["__index"]:Val.Int(0);
     bool desc=VarScope.IsDesc(n);
     // 轮数先算出来：起点跨过终点（顺序时 a>b、倒序时 a<b）以前是 0 轮静默跳过，玩家只看见「直接到下一步」，
     // 误以为是程序坏了；这里必须发一条看得见的事件，把起点、终点、方向、结论一次说全。
     long rounds=desc?(a>=b?a-b+1:0):(b>=a?b-a+1:0);
     // 循环体出口空着时轮数照样走完，但一件事都没做：只在第一条轮次事件里补一句，别 k 条重复刷屏。
     var bodyLinked=GraphUtil.ExecOutLink(p.Graph,n.Uid,"body")!=null;
     if(rounds==0)ev(n,"loopEmpty","计数循环：起点 "+a+"，终点 "+b+"，"+(desc?"倒序":"顺序")+" → 一圈都没转，循环体没执行。",null);
     long i=a;bool capped=false;
     for(;!error&&(desc?i>=b:i<=b);i+=desc?-1:1){if(z.Steps>=maxSteps){capped=true;break;}if(++guard>maxSteps){fail(n,"步数超限。");break;}env[name]=Val.Int(i);env["__index"]=Val.Int(i);cache.Clear();ev(n,"loopIter","第 "+(desc?a-i+1:i-a+1)+" 轮循环，"+name+" = "+i+(guard==1&&!bodyLinked?"（循环体是空的：没有模块接在「循环体」出口）":""),null);next(n,"body");}
     // 撞上限而中断不等于「循环正常跑完」：以前 for 一退出条件就静默收工，截断后的输出会被当成正常结果比对。
     if(capped&&!error)fail(n,"步数超限。");
     if(hadI)env[name]=prevI;else env.Remove(name);
     if(hadIdx)env["__index"]=prevIdx;else env.Remove("__index");
     next(n,"done");break;}
    case "whileloop":{int guard=0;bool capped=false;while(!error){if(z.Steps>=maxSteps){capped=true;break;}if(++guard>maxSteps){fail(n,"步数超限。");break;}cache.Clear();var c=!string.IsNullOrEmpty(GraphUtil.Get(n,"condExpr"))?evalField(n,"condExpr",null).AsBool():val(n,"cond").AsBool();if(error)break;if(!c)break;ev(n,"loopIter","第 "+guard+" 轮循环",null);next(n,"body");}if(capped&&!error)fail(n,"步数超限。");next(n,"done");break;}
    case "structnew":{var d=p.Structs.Find(x=>x.Id==GraphUtil.Get(n,"structId"));sts[GraphUtil.Get(n,"structId")]=new St{Def=d};ev(n,"create",d!=null&&d.Kind=="queue"?"创建队列":"创建栈",null);next(n,"out");break;}
    case "stack_push":{var q=sts[GraphUtil.Get(n,"structId")];var pv=!string.IsNullOrEmpty(GraphUtil.Get(n,"pushExpr"))?evalField(n,"pushExpr",null):val(n,"value");q.Items.Add(pv);ev(n,"push","压入 "+pv.ToDisplayString(),null);next(n,"out");break;}
    case "stack_pop":{var q=sts[GraphUtil.Get(n,"structId")];if(q.Items.Count==0){fail(n,"栈为空，无法弹出。");break;}q.Last=q.Items[q.Items.Count-1];q.Items.RemoveAt(q.Items.Count-1);vals[n.Uid]=q.Last;ev(n,"pop","弹出元素",null);next(n,"out");break;}
    case "queue_push":{var q=sts[GraphUtil.Get(n,"structId")];var pv=!string.IsNullOrEmpty(GraphUtil.Get(n,"pushExpr"))?evalField(n,"pushExpr",null):val(n,"value");q.Items.Add(pv);ev(n,"push","入队 "+pv.ToDisplayString(),null);next(n,"out");break;}
    case "queue_pop":{var q=sts[GraphUtil.Get(n,"structId")];if(q.Items.Count==0){fail(n,"队列为空，无法出队。");break;}q.Last=q.Items[0];q.Items.RemoveAt(0);vals[n.Uid]=q.Last;ev(n,"pop","出队元素",null);next(n,"out");break;}
    default:next(n,"out");break;}};

   var en=GraphUtil.FindNode(p.Graph,p.EntryUid);if(en!=null){var l=GraphUtil.ExecOutLink(p.Graph,en.Uid,"out");if(l!=null)chain(l);}ev(en,"end","执行结束",null);z.Ok=!error;return z; }

  static object DisplayObj(Val v){if(v.Type==ValType.Int)return (int)v.I;if(v.Type==ValType.Long)return v.I;if(v.Type==ValType.Double)return v.D;if(v.Type==ValType.Bool)return v.AsBool();if(v.Type==ValType.Char)return (char)v.I;return v.S;}

  // Val -> 表达式运行时的 object（ExprLang 只认 long / double / bool / char / string）。
  static object ToObj(Val v){switch(v.Type){case ValType.Int:case ValType.Long:return v.I;case ValType.Double:return v.D;case ValType.Bool:return v.AsBool();case ValType.Char:return (char)v.I;case ValType.Str:return v.S??"";default:return null;}}

  // 表达式结果 -> Val，prefer 为变量/端口的声明类型，避免 int 变量被写成 double。
  static Val ToVal(object o,string prefer){
   if(o==null)return Val.Null();
   if(o is long){long x=(long)o;if(prefer=="double")return Val.Double(x);if(prefer=="ll")return Val.Long(x);if(prefer=="string")return Val.Str(x.ToString(CultureInfo.InvariantCulture));if(prefer=="char")return Val.Char((char)x);return Val.Int(x);}
   if(o is double){double d=(double)o;if(string.IsNullOrEmpty(prefer)||prefer=="double")return Val.Double(d);if(prefer=="int"||prefer=="ll")return prefer=="ll"?Val.Long((long)d):Val.Int((long)d);if(prefer=="string")return Val.Str(d.ToString("R",CultureInfo.InvariantCulture));return Val.Double(d);}
   if(o is bool){var b=(bool)o;if(prefer=="string")return Val.Str(b?"true":"false");return Val.Bool(b);}
   if(o is char){var c=(char)o;return prefer=="string"?Val.Str(c.ToString()):Val.Char(c);}
   if(o is string)return Val.Str((string)o);
   if(o is System.Collections.IList)return Val.Null();
   return Val.Str(Convert.ToString(o,CultureInfo.InvariantCulture));
  }

  static Val Parse(string x,string t){if(t=="int")return Val.Int(long.Parse(x??"0",CultureInfo.InvariantCulture));if(t=="ll")return Val.Long(long.Parse(x??"0",CultureInfo.InvariantCulture));if(t=="double")return Val.Double(double.Parse(x??"0",CultureInfo.InvariantCulture));if(t=="bool")return Val.Bool(x=="true"||x=="1");if(t=="char")return Val.Char(string.IsNullOrEmpty(x)?' ':x[0]);return Val.Str(x??"");}

  // 数组写入按下标自增长时补的空位，取值与 C++ vector::resize 的值初始化一致。
  static Val DefaultVal(string elem){
   if(elem=="double")return Val.Double(0);
   if(elem=="bool")return Val.Bool(false);
   if(elem=="char")return Val.Char('\0');
   if(elem=="string")return Val.Str("");
   if(elem=="ll")return Val.Long(0);
   return Val.Int(0);
  }

  // 下标表达式给出的整数：long 直接用，double 只接受整数部分无损的值。
  static bool AsIndex(object o,out long idx){
   idx=0;
   if(o is long l){idx=l;return true;}
   if(o is double d){idx=(long)d;return d==(double)idx;}
   return false;
  }

  // 数组元素：表达式结果（object）按声明的元素类型落成 Val。
  static Val ToElemVal(object o,string elem){
   if(o==null)return Val.Null();
   if(o is long){long x=(long)o;if(elem=="double")return Val.Double(x);if(elem=="char")return Val.Char((char)x);if(elem=="string")return Val.Str(x.ToString(CultureInfo.InvariantCulture));return Val.Int(x);}
   if(o is double){double d=(double)o;if(elem=="double")return Val.Double(d);if(elem=="int"||elem=="ll")return Val.Int((long)d);if(elem=="string")return Val.Str(d.ToString("R",CultureInfo.InvariantCulture));return Val.Double(d);}
   if(o is bool b)return Val.Bool(b);
   if(o is char c)return elem=="string"?Val.Str(c.ToString()):Val.Char(c);
   if(o is string s)return elem=="char"?Val.Char(string.IsNullOrEmpty(s)?' ':s[0]):Val.Str(s);
   return Val.Null();
  }

  // 数组变量 → 表达式运行时需要的 List<object>。
  static List<object> ToObjList(List<Val> items){var l=new List<object>();foreach(var v in items)l.Add(ToObj(v));return l;}

  // 表达式里用到的变量名，用来判断数组变量是否已经创建。
  static void CollectVars(ExprAst n,HashSet<string> outp){if(n==null)return;if(n.Kind=="var")outp.Add(n.Text);if(n.Children==null)return;foreach(var c in n.Children)CollectVars(c,outp);}

  // 数组在运行日志里的显示文本。
  static string ArrayText(List<Val> items){var sb=new StringBuilder("[");for(int i=0;i<items.Count;i++){if(i>0)sb.Append(", ");sb.Append(items[i].ToDisplayString());}return sb.Append("]").ToString();}
  static Val Op(string o,Val a,Val b){if(a.Type==ValType.Str||b.Type==ValType.Str)return Val.Str(a.AsString()+b.AsString());double x=a.AsDouble(),y=b.AsDouble();if(a.Type==ValType.Double||b.Type==ValType.Double){if(o=="+")return Val.Double(x+y);if(o=="-")return Val.Double(x-y);if(o=="*")return Val.Double(x*y);return Val.Double(o=="/"?x/y:x%y);}long i=a.AsLong(),j=b.AsLong();return Val.Long(o=="+"?i+j:o=="-"?i-j:o=="*"?i*j:o=="/"?i/j:i%j);}
  static Val Cmp(string o,Val a,Val b){int c=string.Compare(a.AsString(),b.AsString(),StringComparison.Ordinal);if(a.Type!=ValType.Str&&b.Type!=ValType.Str)c=a.AsDouble().CompareTo(b.AsDouble());return Val.Bool(o=="<"?c<0:o=="<="?c<=0:o==">"?c>0:o==">="?c>=0:o=="=="?c==0:c!=0);}
 }
}
