using System;
using UnityEditor;
using UnityEngine;
using static XReality.Editor.Sculpt;

namespace XReality.Editor
{
    internal static partial class StudioArt
    {
        static Mesh ContactMesh()
        {
            string path=StudioProject.Asset("Contact.asset");
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh) return mesh;
            mesh = new Mesh {name="Contact plane",vertices=new[]{V(-.72f,.04f,-.57f),V(-.72f,.04f,.64f),V(.72f,.04f,.64f),V(.72f,.04f,-.57f)},uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)},triangles=new[]{0,1,2,0,2,3}};
            mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static readonly int[] Sides = {-1,1};
        static Material Clay()
        {
            var path=StudioProject.Asset("Storybook Clay.mat");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat) return mat;
            var shader=Shader.Find("XReality/Storybook Clay");
            if(!shader) throw new InvalidOperationException("Storybook Clay shader was not imported.");
            mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);return mat;
        }
        static Color Pigment(string hex) => QualitySettings.activeColorSpace==ColorSpace.Linear? C(hex).linear:C(hex);
        static Transform Skin(string name, Transform parent, Mesh mesh)
        {
            var node=Joint(name,parent,Vector3.zero);node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Clay();return node;
        }
        static void Stretch(string name,Transform parent,Vector3 a,Vector3 b,float thickness,string color)
        {
            var capsule=Part(name,parent,(a+b)*.5f,V(thickness,(b-a).magnitude+thickness,thickness),color);
            capsule.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
            UnityEngine.Object.DestroyImmediate(capsule.GetComponent<Collider>());
        }
        static void StorybookCat(PetRoom pet, Transform cat)
        {
            var body=Joint("Body",cat,Vector3.zero);
            float BodyField(Vector3 p)
            {
                float d=Merge(Oval(p,V(0,.5f,.12f),V(.39f,.43f,.63f)),Oval(p,V(0,.63f,-.29f),V(.32f,.39f,.33f)),.23f);
                foreach(int s in Sides)
                {
                    d=Merge(d,Oval(p,V(s*.24f,.31f,.34f),V(.20f,.32f,.3f)),.14f);
                    d=Merge(d,Oval(p,V(s*.205f,.25f,-.39f),V(.102f,.25f,.113f)),.12f);
                    d=Merge(d,Oval(p,V(s*.212f,.105f,-.44f),V(.125f,.105f,.177f)),.085f);
                    d=Merge(d,Oval(p,V(s*.252f,.11f,.34f),V(.126f,.11f,.19f)),.095f);
                }
                return d;
            }
            Color Coat(Vector3 p)
            {
                float cream=Mathf.Max(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.13f,.20f,p.y)),Mathf.SmoothStep(0,1,Mathf.InverseLerp(.49f,.51f,-p.z))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.19f,.26f,Mathf.Abs(p.x)))));
                float wave=1-Mathf.SmoothStep(.08f,.20f,Mathf.Abs(Mathf.Sin((p.z+.23f*p.y*p.y)*17.4f)));
                var ginger=Color.Lerp(Pigment("EB9958"),Pigment("BE5D38"),wave*.74f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.48f,.88f,p.y)));
                return Color.Lerp(ginger,Pigment("FFE9C9"),cream);
            }
            Skin("Sculpted body and paws",body,Surface("Cat-Coat",V(-.59f,-.08f,-.73f),V(.59f,1.12f,.83f),.033f,BodyField,Coat));
            var collider=body.gameObject.AddComponent<BoxCollider>();collider.center=V(0,.46f,.1f);collider.size=V(.82f,.91f,1.21f);
            var head=Joint("Head",body,V(0,.93f,-.38f));
            if (pet) pet.head = head;
            float HeadField(Vector3 p)
            {
                float d=Merge(Oval(p,V(0,.025f,.018f),V(.446f,.36f,.362f)),Oval(p,V(0,-.115f,-.02f),V(.433f,.255f,.314f)),.15f);
                foreach(int s in Sides)d=Merge(d,Oval(p,V(.09f*s,-.107f,-.305f),V(.159f,.109f,.137f)),.105f);
                return Merge(d,Oval(p,V(0,-.208f,-.244f),V(.188f,.127f,.15f)),.13f);
            }
            Color Face(Vector3 p)
            {
                float cream=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.11f,-.045f,p.y+.64f*Mathf.Abs(p.x))))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.16f,.27f,-p.z));
                float forehead=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.13f,.27f,p.y));
                float stripe=(1-Mathf.SmoothStep(.012f,.05f,Mathf.Abs(Mathf.Abs(p.x)-(.03f+.54f*(p.y-.23f)))))*forehead;
                float cheek=(1-Mathf.SmoothStep(.012f,.038f,Mathf.Abs(p.y+.04f+.20f*(p.z+.19f))))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.3f,.4f,Mathf.Abs(p.x)));
                return Color.Lerp(Color.Lerp(Pigment("F1A15D"),Pigment("BF603A"),Mathf.Max(stripe,cheek)*.87f),Pigment("FFF0D6"),cream);
            }
            Skin("Sculpted cheeks, muzzle and chin",head,Surface("Cat-Face",V(-.55f,-.43f,-.51f),V(.55f,.47f,.46f),.023f,HeadField,Face));
            var hc=head.gameObject.AddComponent<SphereCollider>();hc.radius=.40f;
            foreach(int s in Sides)
            {
                var ear=Joint(s==1?"Ear.L":"Ear.R",head,V(s*.29f,.247f,-.017f));
                float EarField(Vector3 p)
                {
                    float dy=Mathf.Clamp01((p.y+.1f)/.48f);
                    return Oval(V((p.x-s*p.y*.37f)/(1-dy*.66f),p.y,p.z),V(0,.127f,.018f),V(.216f,.249f,.097f));
                }
                Color EarPaint(Vector3 p)=>Color.Lerp(Pigment("D8854D"),Pigment("E8A099"),Mathf.SmoothStep(0,1,Mathf.InverseLerp(.041f,.068f,-p.z))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.12f,p.y)));
                Skin("Rounded ear",ear,Surface(s==1?"Cat-EarL":"Cat-EarR",V(-.37f,-.15f,-.13f),V(.37f,.43f,.16f),.024f,EarField,EarPaint));
                var eye=Joint(s==1?"Eye.L":"Eye.R",head,V(s*.193f,.043f,-.296f));eye.localRotation=Quaternion.Euler(0,s*-23,s*-8);
                Part("Dark eyelid",eye,V(0,0,0),V(.176f,.177f,.117f),"442F30");
                Part("Forest iris",eye,V(0,0,-.045f),V(.141f,.145f,.052f),"456A51");
                Part("Pupil",eye,V(0,0,-.065f),V(.061f,.123f,.022f),"241E26");
                Part("Eye glint",eye,V(-.032f,.038f,-.078f),V(.042f,.039f,.018f),"FFFAE8");
                for(int i=0;i<2;i++) Stretch("Whisker",head,V(s*.194f,-.132f-i*.034f,-.406f),V(s*(.513f-i*.045f),-.081f-i*.088f,-.35f),.012f,"FFF0D6");
            }
            Part("Nose",head,V(0,-.077f,-.443f),V(.103f,.064f,.037f),"9E5650");
            Stretch("Philtrum",head,V(0,-.099f,-.449f),V(0,-.165f,-.437f),.018f,"834D42");
            var tail=Joint("Tail",body,V(0,.36f,.54f));
            var chain=new[]{V(0,0,0),V(.085f,.075f,.188f),V(.22f,.22f,.285f),V(.39f,.39f,.26f),V(.53f,.48f,.14f),V(.56f,.49f,.01f)};
            float TailField(Vector3 p)
            {
                float d=1;
                for(int i=1;i<chain.Length;i++){var a=chain[i-1];var b=chain[i];var ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/ab.sqrMagnitude);d=Merge(d,(p-(a+ab*t)).magnitude-Mathf.Lerp(.114f,.055f,(i-1+t)/(chain.Length-1)),.048f);}
                return d;
            }
            Skin("Tapered curled tail",tail,Surface("Cat-Tail",V(-.17f,-.16f,-.16f),V(.7f,.64f,.44f),.024f,TailField,p=>Color.Lerp(Pigment("E58A4E"),Pigment("AA4D37"),Mathf.SmoothStep(.15f,.65f,Mathf.Sin((p.x+p.y)*26))*.66f)));
        }
    }
}
