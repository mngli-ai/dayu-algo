using UnityEngine;
using UnityEngine.UI;
public sealed class GridGraphic : MaskableGraphic {
 // 画布底色就是 Foam，点阵得用半透明的鲸青才浮得出来
 protected override void OnPopulateMesh(VertexHelper vh){ vh.Clear(); var r=rectTransform.rect; var minor=Theme.A(Theme.Whale,.16f); var major=Theme.A(Theme.WhaleDeep,.26f); for(int y=24;y<r.height;y+=24) for(int x=24;x<r.width;x+=24) Add(vh,x,y,1,1,minor); for(int y=120;y<r.height;y+=120) for(int x=120;x<r.width;x+=120) Add(vh,x,y,2,2,major); }
 void Add(VertexHelper vh,float x,float y,float w,float h,Color c){int i=vh.currentVertCount;vh.AddVert(new Vector3(x,-y),c,Vector2.zero);vh.AddVert(new Vector3(x+w,-y),c,Vector2.zero);vh.AddVert(new Vector3(x+w,-y-h),c,Vector2.zero);vh.AddVert(new Vector3(x,-y-h),c,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i+2,i+3,i);}
}
