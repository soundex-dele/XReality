using UnityEditor;
using UnityEngine;
using static XReality.Editor.Sculpt;

namespace XReality.Editor
{
    internal static partial class StudioArt
    {
        // Sculpted bust, with spatially fitted eyes and independent eyelid/jaw deformations.
        static void Presenter(LiveStudio live, Transform root, Vector3 origin)
        {
            var torso = Joint("Torso", root, origin);
            if (live) live.torso = torso;
            float Jacket(Vector3 p)
            {
                float trunk=Oval(p,V(0,.345f,.052f),V(.64f,.67f,.40f));
                foreach(int s in Sides)trunk=Merge(trunk,Oval(p,V(s*.51f,.45f,.033f),V(.25f,.42f,.305f)),.22f);
                return Mathf.Max(trunk,-p.y-.18f);
            }
            Skin("Tailored jacket",torso,Surface("Host-Jacket",V(-.85f,-.25f,-.48f),V(.85f,1.11f,.55f),.037f,Jacket,p=>Color.Lerp(Pigment("244758"),Pigment("417080"),Mathf.InverseLerp(-.1f,.9f,p.y))));
            Part("Shirt",torso,V(0,.86f,-.125f),V(.58f,.36f,.49f),"E9D2A8");
            Stretch("Collar left",torso,V(-.25f,.94f,-.316f),V(-.119f,.76f,-.383f),.068f,"48717B");
            Stretch("Collar right",torso,V(.25f,.94f,-.316f),V(.119f,.76f,-.383f),.068f,"48717B");
            Part("Neck",torso,V(0,1.03f,.01f),V(.40f,.58f,.42f),"C98966");
            var badge=Part("Studio pin",torso,V(-.39f,.67f,-.323f),V(.104f,.104f,.038f),"DCA454");
            badge.localRotation=Quaternion.Euler(0,14,0);
            var head=Joint("Neck pivot",torso,V(0,1.10f,0));
            if (live) live.head = head;
            float FaceField(Vector3 p)
            {
                float d=Merge(Oval(p,V(0,.63f,.04f),V(.532f,.60f,.464f)),Oval(p,V(0,.296f,-.093f),V(.376f,.302f,.385f)),.17f);
                d=Merge(d,Oval(p,V(0,.46f,-.411f),V(.106f,.15f,.118f)),.095f);
                foreach(int s in Sides)d=Merge(d,Oval(p,V(s*.515f,.52f,.012f),V(.114f,.154f,.10f)),.05f);
                return d;
            }
            Color SkinTone(Vector3 p)
            {
                float cheek=Mathf.Exp(-((Mathf.Abs(p.x)-.318f)*(Mathf.Abs(p.x)-.318f)*76+(p.y-.40f)*(p.y-.40f)*119))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.19f,.35f,-p.z));
                return Color.Lerp(Pigment("E2AE89"),Pigment("C77768"),cheek*.58f);
            }
            var face=Surface("Host-Face",V(-.69f,-.08f,-.61f),V(.69f,1.30f,.56f),.025f,FaceField,SkinTone);
            var faceSurface=Morph(head,"Sculpted face",face,"JawOpen",p=>V(0,-.065f*(1-Mathf.SmoothStep(.18f,.36f,p.y))*Mathf.SmoothStep(.19f,.39f,-p.z),0),Clay());
            float Front(float x,float y)
            {
                float outer=-.64f,inner=0;
                for(int i=0;i<12;i++){float z=(outer+inner)*.5f;if(FaceField(V(x,y,z))>0)outer=z;else inner=z;}
                return outer-.007f;
            }
            float HairField(Vector3 p)
            {
                float dome=Oval(p,V(0,.746f,.093f),V(.587f,.593f,.506f));
                var a=p-V(-.14f,.984f,-.248f);
                a=Quaternion.Euler(0,0,-22)*a;
                float sweep=Oval(a,Vector3.zero,V(.435f,.222f,.267f));
                float line=.36f+.49f*Mathf.SmoothStep(.05f,.35f,-p.z)+.10f*p.x;
                return Merge(Mathf.Max(dome,line-p.y),sweep,.14f);
            }
            Skin("Side-swept hair",head,Surface("Host-Bob",V(-.66f,.27f,-.57f),V(.66f,1.43f,.68f),.026f,HairField,p=>Color.Lerp(Pigment("332C38"),Pigment("694842"),Mathf.SmoothStep(.8f,1.32f,p.y))));
            if (live) live.faceSurface = faceSurface;
            foreach(int s in Sides)
            {
                float x=s*.206f,y=.619f;
                Transform eye=Joint(s==1?"Eye L":"Eye R",head,V(x,y,Front(x,y)-.006f));
                eye.localRotation=Quaternion.Euler(0,-s*18,0);
                Part("Eye border",eye,V(0,0,0),V(.199f,.143f,.044f),"4C3436");
                Part("Eye white",eye,V(0,-.004f,-.013f),V(.184f,.115f,.034f),"FAEBD9");
                Part("Hazel iris",eye,V(0,-.001f,-.029f),V(.098f,.107f,.018f),"78604D");
                Part("Pupil",eye,V(0,-.001f,-.038f),V(.056f,.076f,.012f),"34282F");
                Part("Glint",eye,V(-.021f,.030f,-.047f),V(.028f,.024f,.011f),"FFF5E1");
                // Keep the seam inside the face when open; emerge as the eyelids meet.
                Vector3 Seam(float u, float v, bool closed)
                {
                    float px = x + u * .104f;
                    float py = y - .007f + .026f * u * u + v * .009f;
                    return V(px, py, Front(px, py) + (closed ? -.017f : .09f));
                }
                var lid = FacePatch(head,s==1?"Host-Lid-L":"Host-Lid-R",x,y,.108f,.085f,
                    (u,v)=>Seam(u,v,false), (u,v)=>Seam(u,v,true),
                    (u,v)=>Color.Lerp(Pigment("513233"), Pigment("613E3B"), (v + 1) * .5f), "Blink");
                if (live)
                {
                    if (s == 1) { live.leftLid = lid; live.leftEye = eye; }
                    else { live.rightLid = lid; live.rightEye = eye; }
                }
                var brow=Part("Brow",head,V(x,y+.125f,Front(x,y+.125f)),V(.163f,.038f,.05f),"59413D",PrimitiveType.Sphere,s*-5);
                brow.localRotation*=Quaternion.Euler(0,-s*18,0);
            }
            var lip=FacePatch(head,"Host-Mouth",0,.248f,.145f,.12f,
                (u,v)=>V(u*.144f,.253f+.027f*u*u+v*.011f,Front(u*.144f,.254f)-.019f),
                (u,v)=>V(u*.137f,.208f+.04f*u*u+v*.071f,Front(u*.137f,.23f)-.024f),
                (u,v)=>v>.66f?Pigment("A95E5B"):v<-.62f?Pigment("B86961"):Pigment("703B43"),"JawOpen");
            if (live) live.lip = lip;
        }
        static SkinnedMeshRenderer FacePatch(Transform head,string key,float x,float y,float radiusX,float radiusY,
            System.Func<float,float,Vector3> rest,System.Func<float,float,Vector3> target,System.Func<float,float,Color> color,string shape)
        {
            var path=StudioProject.Asset(key+".asset");
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh)
            {
                const int steps=48; const int bands=6;
                var vertices=new Vector3[1+steps*bands];var deltas=new Vector3[vertices.Length];var normals=new Vector3[vertices.Length];var colors=new Color[vertices.Length];var tris=new System.Collections.Generic.List<int>();
                void Point(int i,float u,float v){vertices[i]=rest(u,v);normals[i]=Vector3.back;colors[i]=color(u,v);deltas[i]=target(u,v)-vertices[i];}
                Point(0,0,0);
                for(int ring=1;ring<=bands;ring++) for(int k=0;k<steps;k++)
                {
                    int i=1+(ring-1)*steps+k;int next=1+(ring-1)*steps+(k+1)%steps;float angle=k*2*Mathf.PI/steps;
                    Point(i,Mathf.Cos(angle)*ring/bands,Mathf.Sin(angle)*ring/bands);
                    if(ring==1){tris.Add(0);tris.Add(next);tris.Add(i);}
                    else {tris.Add(i-steps);tris.Add(next);tris.Add(i);tris.Add(i-steps);tris.Add(next-steps);tris.Add(next);}
                }
                mesh=new Mesh{name=key,vertices=vertices,normals=normals,colors=colors,triangles=tris.ToArray()};
                mesh.AddBlendShapeFrame(shape,100,deltas,null,null);
                mesh.bounds=new Bounds(V(x,y,-.46f),V(radiusX*2.5f,radiusY*4,.65f));
                AssetDatabase.CreateAsset(mesh,path);
            }
            return AttachMorph(head,key,mesh,Clay());
        }
        static SkinnedMeshRenderer Morph(Transform parent,string name,Mesh source,string shape,System.Func<Vector3,Vector3> deform,Material material)
        {
            var path=StudioProject.Asset("Host-FacialRig.asset");var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh)
            {
                mesh=UnityEngine.Object.Instantiate(source);mesh.name=name;
                var points=mesh.vertices;var delta=new Vector3[points.Length];for(int i=0;i<points.Length;i++)delta[i]=deform(points[i]);
                mesh.AddBlendShapeFrame(shape,100,delta,null,null);AssetDatabase.CreateAsset(mesh,path);
            }
            return AttachMorph(parent,name,mesh,material);
        }
        static SkinnedMeshRenderer AttachMorph(Transform parent,string name,Mesh mesh,Material mat)
        {
            var node=Joint(name,parent,Vector3.zero);var renderer=node.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh=mesh;renderer.sharedMaterial=mat;renderer.localBounds=mesh.bounds;renderer.updateWhenOffscreen=true;return renderer;
        }
    }
}
