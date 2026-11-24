using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.Editor
{
    // Polygonize a scalar field once in the editor. Normals come from the surface gradient,
    // so neighboring cells shade continuously even when their triangles don't share vertices.
    internal static class Sculpt
    {
        static readonly int[,] Tets = { {0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6} };
        static readonly Vector3Int[] Corners = { new(0,0,0),new(1,0,0),new(1,1,0),new(0,1,0),new(0,0,1),new(1,0,1),new(1,1,1),new(0,1,1) };
        public static float Oval(Vector3 p, Vector3 c, Vector3 r)
        {
            p -= c;
            return (new Vector3(p.x/r.x,p.y/r.y,p.z/r.z).magnitude-1)*Mathf.Min(r.x,Mathf.Min(r.y,r.z));
        }
        public static float Merge(float a, float b, float radius = .12f)
        {
            float h = Mathf.Max(radius-Mathf.Abs(a-b),0)/radius;
            return Mathf.Min(a,b)-h*h*radius*.25f;
        }
        public static Mesh Surface(string key, Vector3 min, Vector3 max, float spacing, Func<Vector3,float> field, Func<Vector3,Color> paint)
        {
            string path=StudioProject.Asset(key+".asset");
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing) return existing;
            var count=Vector3Int.CeilToInt((max-min)/spacing);
            var values=new float[count.x+1,count.y+1,count.z+1];
            for(int x=0;x<=count.x;x++) for(int y=0;y<=count.y;y++) for(int z=0;z<=count.z;z++)
                values[x,y,z]=field(min+new Vector3(x,y,z)*spacing);
            Vector3 Grad(Vector3 p)
            {
                const float h=.002f;
                return new Vector3(field(p+Vector3.right*h)-field(p-Vector3.right*h),field(p+Vector3.up*h)-field(p-Vector3.up*h),field(p+Vector3.forward*h)-field(p-Vector3.forward*h)).normalized;
            }
            var pos=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var tris=new List<int>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                var n=Grad(a);
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),n)<0) (b,c)=(c,b);
                tris.Add(pos.Count);tris.Add(pos.Count+1);tris.Add(pos.Count+2);
                pos.Add(a);pos.Add(b);pos.Add(c);normals.Add(n);normals.Add(Grad(b));normals.Add(Grad(c));colors.Add(paint(a));colors.Add(paint(b));colors.Add(paint(c));
            }
            var points=new Vector3[8];var val=new float[8];var inside=new int[4];var outside=new int[4];
            Vector3 Edge(int a,int b) => Vector3.LerpUnclamped(points[a],points[b],val[a]/(val[a]-val[b]));
            for(int x=0;x<count.x;x++) for(int y=0;y<count.y;y++) for(int z=0;z<count.z;z++)
            {
                int filled=0;
                for(int i=0;i<8;i++) {var c=new Vector3Int(x,y,z)+Corners[i];val[i]=values[c.x,c.y,c.z];if(val[i]<0)filled++;points[i]=min+(Vector3)c*spacing;}
                if(filled==0||filled==8) continue;
                for(int t=0;t<6;t++)
                {
                    int ni=0,no=0;
                    for(int j=0;j<4;j++) {int v=Tets[t,j];if(val[v]<0)inside[ni++]=v;else outside[no++]=v;}
                    if(ni==1) Triangle(Edge(inside[0],outside[0]),Edge(inside[0],outside[1]),Edge(inside[0],outside[2]));
                    if(ni==3) Triangle(Edge(outside[0],inside[0]),Edge(outside[0],inside[1]),Edge(outside[0],inside[2]));
                    if(ni==2)
                    {
                        var a=Edge(inside[0],outside[0]);var b=Edge(inside[0],outside[1]);var c=Edge(inside[1],outside[0]);var d=Edge(inside[1],outside[1]);
                        Triangle(a,b,c);Triangle(c,b,d);
                    }
                }
            }
            var mesh=new Mesh {name=key,indexFormat=IndexFormat.UInt32};mesh.SetVertices(pos);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(tris,0);mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
    }
}
