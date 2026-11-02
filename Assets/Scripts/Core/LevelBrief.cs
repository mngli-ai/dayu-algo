using System.Collections.Generic;
namespace Dayu.Core {
 // 题栏版式的纯函数：把一关的样例切成「样板（可见）」与「抽查（隐藏）」两段，
 // 再算出判定一行话和按组分节的样例正文。只做数据加工、不碰渲染，Editor 自测可直接调用。
 // 约定：可见样例必须排在隐藏样例前面（Levels.cs 的关卡表按此填写，LevelContentTests 盯着）。
 // 一节的样例版式：一个小标题配一段正文（正文原样保留自带的换行）。
 public sealed class BriefSection {
  public string Heading; public string Body; public bool GroupEnd;
  public BriefSection(string heading,string body,bool groupEnd){Heading=heading;Body=body;GroupEnd=groupEnd;}
 }
 public static class LevelBrief {
  public static List<LevelTest> Visible(Level l){var r=new List<LevelTest>();if(l==null||l.Tests==null)return r;foreach(var t in l.Tests)if(t!=null&&t.Visible)r.Add(t);return r;}
  public static List<LevelTest> Hidden(Level l){var r=new List<LevelTest>();if(l==null||l.Tests==null)return r;foreach(var t in l.Tests)if(t!=null&&!t.Visible)r.Add(t);return r;}
  public static int HiddenCount(Level l){return Hidden(l).Count;}
  public static bool HasHidden(Level l){return HiddenCount(l)>0;}
  // 「样板 3 条 · 抽查 5 条」：只报条数，不暴露隐藏样例的任何内容。
  public static string JudgeLine(Level l){return "样板 "+Visible(l).Count+" 条 · 抽查 "+Hidden(l).Count+" 条";}
  // 洛谷那种「一组输入配一组输出」：每条可见样例切两节，输入节紧跟自己的输出节，隐藏样例一条不进列表。
  public static List<BriefSection> SampleSections(Level l){
   var r=new List<BriefSection>();var vis=Visible(l);
   // 只有一条样例时标题不带序号，否则玩家会以为一份数据里只截了一条。
   for(int k=0;k<vis.Count;k++){var tail=vis.Count>1?" "+(k+1):"";
    r.Add(new BriefSection("输入样例"+tail,vis[k].Input??"",false));
    r.Add(new BriefSection("输出样例"+tail,vis[k].Output??"",true));}
   return r;
  }
 }
}
