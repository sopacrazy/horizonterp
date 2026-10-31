using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Original metre-scale architecture for the separate city example. No recovered model is reused.
public static class CityExampleBuildings
{
    const string Root = "Assets/CityExample/Architecture";
    static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
    static Mesh cube;
    static Mesh pitchedRoof;
    static readonly Color[] WallColors = {
        new Color(.88f,.80f,.63f), new Color(.72f,.84f,.81f), new Color(.89f,.68f,.56f),
        new Color(.81f,.84f,.88f), new Color(.88f,.85f,.75f), new Color(.68f,.76f,.87f)
    };

    sealed class Assembly
    {
        public GameObject root;
        public readonly Dictionary<Material,List<CombineInstance>> parts = new Dictionary<Material,List<CombineInstance>>();
        public readonly List<Renderer> renderers = new List<Renderer>();
        public void Box(string name, Vector3 position, Vector3 size, Material material, bool collision = false)
        {
            Add(Cube(), position, Quaternion.identity, size, material);
            if (!collision) return;
            var support = new GameObject(name); support.transform.SetParent(root.transform, false); support.isStatic = true;
            var collider = support.AddComponent<BoxCollider>(); collider.center = position; collider.size = size;
        }
        public void Add(Mesh mesh,Vector3 position,Quaternion rotation,Vector3 scale,Material material)
        {
            if (!parts.TryGetValue(material,out var list)) { list = new List<CombineInstance>(); parts.Add(material,list); }
            list.Add(new CombineInstance { mesh=mesh, transform=Matrix4x4.TRS(position,rotation,scale) });
        }
        public void Bake(string key)
        {
            int number = 0;
            foreach (var group in parts)
            {
                string path = Root+"/Meshes/"+key+"_"+number+".asset";
                var combined = new Mesh { name=key+"_"+group.Key.name, indexFormat=IndexFormat.UInt32 };
                combined.CombineMeshes(group.Value.ToArray(),true,true,false); combined.RecalculateBounds();
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) { EditorUtility.CopySerialized(combined,existing); Object.DestroyImmediate(combined); combined=existing; }
                else AssetDatabase.CreateAsset(combined,path);
                var child = new GameObject(group.Key.name,typeof(MeshFilter),typeof(MeshRenderer));
                child.transform.SetParent(root.transform,false); child.isStatic=true;
                child.GetComponent<MeshFilter>().sharedMesh=combined;
                var renderer=child.GetComponent<MeshRenderer>(); renderer.sharedMaterial=group.Key;
                renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
                renderers.Add(renderer); number++;
            }
        }
    }

    // Front is local -Z. The supplied footprint includes the roof and every attached detail.
    public static GameObject Create(Transform parent,Vector3 center,Vector2 footprint,float height,string kind,float yaw,int variant,bool accessibleInterior=false)
    {
        if (footprint.x<7 || footprint.y<7 || height<3.5f)
            throw new ArgumentException("Os modulos precisam de pelo menos 7 x 7 m e 3,5 m de altura.");
        kind=(kind??"house").ToLowerInvariant(); variant=((variant%6)+6)%6;
        string description=kind+"_"+footprint.x.ToString("F3",CultureInfo.InvariantCulture)+"_"+
            footprint.y.ToString("F3",CultureInfo.InvariantCulture)+"_"+height.ToString("F3",CultureInfo.InvariantCulture)+"_"+variant+"_"+accessibleInterior;
        string key=kind+"_"+Hash(description).ToString("x8");
        if (!Templates.TryGetValue(key,out var template) || !template)
        {
            PrepareFolders();
            template=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+key+".prefab");
            if (!template)
            {
                var assembly=Build(footprint,height,kind,variant,accessibleInterior);
                assembly.Bake(key);
                var lod=assembly.root.AddComponent<LODGroup>();
                lod.SetLODs(new[]{new LOD(height>12?.004f:.008f,assembly.renderers.ToArray())});
                lod.fadeMode=LODFadeMode.None; lod.RecalculateBounds();
                template=PrefabUtility.SaveAsPrefabAsset(assembly.root,Root+"/Prefabs/"+key+".prefab");
                Object.DestroyImmediate(assembly.root);
            }
            Templates[key]=template;
        }
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(template);
        instance.name=Label(kind)+(accessibleInterior?" - interior acessivel":"")+" "+variant;
        instance.transform.SetParent(parent,false);
        instance.transform.SetPositionAndRotation(center,Quaternion.Euler(0,yaw,0));
        return instance;
    }

    static Assembly Build(Vector2 footprint,float h,string kind,int variant,bool interior)
    {
        float w=footprint.x,d=footprint.y;
        var a=new Assembly {root=new GameObject("Modulo "+kind)}; a.root.isStatic=true;
        bool house=kind=="house"||kind=="community"||kind=="farm";
        bool shed=kind=="warehouse"||kind=="airport";
        Color tint=WallColors[variant];
        if (kind=="hospital") tint=new Color(.91f,.93f,.91f);
        if (kind=="police") tint=new Color(.66f,.73f,.80f);
        if (kind=="cityhall") tint=new Color(.89f,.84f,.72f);
        if (shed) tint=new Color(.66f,.70f,.71f);
        var wall=Material("Parede_"+kind+"_"+variant,tint,shed?"T_MetalConcrete_BaseColor.png":"T_Concrete_BaseColor.png",3.5f);
        var trim=Material("Acabamento",new Color(.86f,.85f,.80f),"T_Concrete_BaseColor.png",2);
        var dark=Material("Metal_escuro",new Color(.15f,.19f,.22f),null,2,.45f,.35f);
        var glass=Material("Vidro_azul",new Color(.18f,.34f,.42f),null,2,.7f,.15f);
        var roof=Material(house?"Telha_ceramica":"Cobertura",house?new Color(.56f,.27f,.16f):new Color(.35f,.39f,.40f),"T_RoofSlate_BaseColor.png",2.5f);
        var floor=Material("Piso_interno",new Color(.78f,.77f,.70f),"T_MarbleFloor_BaseColor.png",3);
        var wood=Material("Madeira",new Color(.40f,.24f,.12f),null,2);
        var brick=Material("Tijolo",new Color(.82f,.65f,.52f),"T_RedBrick_BaseColor.png",3);
        var accent=Material("Identidade_"+kind,Accent(kind),null,2,.15f);
        float bodyW=w-.7f,bodyD=d-.7f,bodyFront=-bodyD*.5f,bodyBack=bodyD*.5f;
        float thickness=.25f,doorWidth=Mathf.Min(3.2f,Mathf.Max(2.8f,bodyW*.35f)),doorHeight=3.05f;

        if (interior)
        {
            // A single accessible level: continuous centre aisle, real door opening and no blocking glass.
            a.Box("Piso acessivel",new Vector3(0,-.06f,0),new Vector3(bodyW,.20f,bodyD),floor,true);
            a.Box("Parede lateral esquerda",new Vector3(-bodyW*.5f+thickness*.5f,h*.5f,0),new Vector3(thickness,h,bodyD),wall,true);
            a.Box("Parede lateral direita",new Vector3(bodyW*.5f-thickness*.5f,h*.5f,0),new Vector3(thickness,h,bodyD),wall,true);
            a.Box("Parede de fundos",new Vector3(0,h*.5f,bodyBack-thickness*.5f),new Vector3(bodyW,h,thickness),wall,true);
            float side=(bodyW-doorWidth)*.5f;
            a.Box("Fachada esquerda",new Vector3(-(doorWidth+side)*.5f,h*.5f,bodyFront+thickness*.5f),new Vector3(side,h,thickness),wall,true);
            a.Box("Fachada direita",new Vector3((doorWidth+side)*.5f,h*.5f,bodyFront+thickness*.5f),new Vector3(side,h,thickness),wall,true);
            a.Box("Verga da entrada",new Vector3(0,doorHeight+(h-doorHeight)*.5f,bodyFront+thickness*.5f),new Vector3(doorWidth,h-doorHeight,thickness),wall,true);
            // Floor reaches the edge of the declared lot. The 4 cm threshold is walkable.
            a.Box("Soleira nivelada",new Vector3(0,-.06f,-d*.5f+.175f),new Vector3(doorWidth,.2f,.35f),floor,true);
            a.Box("Portico esquerdo",new Vector3(-doorWidth*.5f-.08f,1.5f,bodyFront-.04f),new Vector3(.13f,3,.13f),accent);
            a.Box("Portico direito",new Vector3(doorWidth*.5f+.08f,1.5f,bodyFront-.04f),new Vector3(.13f,3,.13f),accent);
            // Furnishings intentionally stay outside a 3.2 m wide central corridor.
            float sideX=Mathf.Max(2.3f,bodyW*.31f),deskW=Mathf.Min(2.4f,bodyW*.21f);
            if (bodyW>9)
            {
                Furniture(a,new Vector3(-sideX,0,-bodyD*.1f),deskW,wood,dark);
                Furniture(a,new Vector3(sideX,0,-bodyD*.25f),deskW,wood,dark);
                a.Box("Banco de espera",new Vector3(-sideX,.3f,bodyD*.28f),new Vector3(deskW,.55f,.65f),accent,true);
            }
            if (kind=="hospital")
            {
                a.Box("Maca de atendimento",new Vector3(sideX,.55f,bodyD*.31f),new Vector3(1.2f,.55f,2.1f),trim,true);
                a.Box("Colchao",new Vector3(sideX,.88f,bodyD*.31f),new Vector3(1.2f,.12f,2.1f),accent);
            }
            if (h>5)
            {
                a.Box("Forro do pavimento acessivel",new Vector3(0,3.6f,0),new Vector3(bodyW,.18f,bodyD),trim,true);
                a.Box("Faixa de pavimento",new Vector3(0,3.76f,bodyFront-.03f),new Vector3(bodyW,.22f,.10f),accent);
            }
        }
        else
        {
            a.Box("Volume fechado",new Vector3(0,h*.5f,0),new Vector3(bodyW,h,bodyD),kind=="community"&&variant%2==0?brick:wall,true);
            a.Box("Porta cenografica fechada",new Vector3(0,1.2f,bodyFront-.025f),new Vector3(Mathf.Min(1.3f,bodyW*.18f),2.4f,.04f),wood);
        }

        // Foundation and roof are aligned to the same wall rectangle, without unsupported steps.
        a.Box("Rodape frontal esquerdo",new Vector3(-(bodyW+doorWidth)*.25f,.18f,bodyFront-.03f),new Vector3((bodyW-doorWidth)*.5f,.35f,.10f),trim);
        a.Box("Rodape frontal direito",new Vector3((bodyW+doorWidth)*.25f,.18f,bodyFront-.03f),new Vector3((bodyW-doorWidth)*.5f,.35f,.10f),trim);
        a.Box("Acabamento lateral",new Vector3(-bodyW*.5f-.035f,.18f,0),new Vector3(.10f,.35f,bodyD),trim);
        a.Box("Acabamento lateral",new Vector3(bodyW*.5f+.035f,.18f,0),new Vector3(.10f,.35f,bodyD),trim);
        if (house)
        {
            a.Add(Roof(),new Vector3(0,h,0),Quaternion.identity,new Vector3(w,Mathf.Min(2,w*.17f),d),roof);
            a.Box("Laje de cobertura",new Vector3(0,h-.08f,0),new Vector3(bodyW,.16f,bodyD),trim,interior);
        }
        else
        {
            a.Box("Laje de cobertura",new Vector3(0,h+.07f,0),new Vector3(w,.24f,d),roof,interior);
            a.Box("Platibanda frontal",new Vector3(0,h+.42f,-d*.5f+.12f),new Vector3(w,.55f,.24f),trim);
            a.Box("Platibanda fundos",new Vector3(0,h+.42f,d*.5f-.12f),new Vector3(w,.55f,.24f),trim);
            a.Box("Platibanda lateral",new Vector3(-w*.5f+.12f,h+.42f,0),new Vector3(.24f,.55f,d),trim);
            a.Box("Platibanda lateral",new Vector3(w*.5f-.12f,h+.42f,0),new Vector3(.24f,.55f,d),trim);
        }

        if (!shed)
        {
            int rows=Mathf.Max(1,Mathf.FloorToInt((h-.3f)/3.15f));
            int columns=Mathf.Clamp(Mathf.FloorToInt((bodyW-1)/3.2f),2,8);
            float windowW=Mathf.Min(1.75f,(bodyW-1)/columns*.57f);
            for(int row=0;row<rows;row++)
            {
                float y=1.85f+row*3.15f;
                if (y+.65f>h-.2f)continue;
                for(int col=0;col<columns;col++)
                {
                    float x=-bodyW*.5f+(col+.5f)*bodyW/columns;
                    if(row>0||Mathf.Abs(x)>doorWidth*.5f+windowW*.5f+.15f)
                        Window(a,new Vector3(x,y,bodyFront-.04f),new Vector2(windowW,1.35f),false,trim,glass,dark);
                    Window(a,new Vector3(x,y,bodyBack+.04f),new Vector2(windowW,1.35f),false,trim,glass,dark);
                }
                int sideColumns=Mathf.Clamp(Mathf.FloorToInt((bodyD-1)/3.5f),2,7);
                for(int col=0;col<sideColumns;col++)
                {
                    float z=-bodyD*.5f+(col+.5f)*bodyD/sideColumns;
                    Window(a,new Vector3(-bodyW*.5f-.04f,y,z),new Vector2(1.55f,1.35f),true,trim,glass,dark);
                    Window(a,new Vector3(bodyW*.5f+.04f,y,z),new Vector2(1.55f,1.35f),true,trim,glass,dark);
                }
            }
        }
        else
        {
            // Horizontal industrial clerestory, avoiding the entrance opening.
            float panelY=Mathf.Max(3.6f,h-.8f);
            a.Box("Janela alta industrial",new Vector3(0,panelY,bodyFront-.04f),new Vector3(bodyW*.72f,.55f,.055f),glass);
            for(float z=-bodyD*.4f;z<bodyD*.5f;z+=4)
            {
                a.Box("Montante de fachada",new Vector3(-bodyW*.5f-.04f,h*.5f,z),new Vector3(.10f,h,.16f),dark);
                a.Box("Montante de fachada",new Vector3(bodyW*.5f+.04f,h*.5f,z),new Vector3(.10f,h,.16f),dark);
            }
        }

        if (!house)
        {
            float signY=interior?3.38f:2.95f;
            string text=Label(kind).ToUpperInvariant();
            float signW=Mathf.Min(bodyW-.5f,Mathf.Max(5,text.Length*.38f));
            a.Box("Placa da fachada",new Vector3(0,signY,bodyFront-.08f),new Vector3(signW,.55f,.08f),accent);
            var label=new GameObject(text,typeof(TextMesh)); label.transform.SetParent(a.root.transform,false);
            label.transform.localPosition=new Vector3(0,signY,bodyFront-.127f);
            var tm=label.GetComponent<TextMesh>(); tm.text=text;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;
            tm.characterSize=Mathf.Min(.30f,signW/Mathf.Max(1,text.Length)*1.25f);tm.fontSize=48;tm.color=Color.white;
            var renderer=label.GetComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            a.renderers.Add(renderer);
        }
        return a;
    }

    static void Furniture(Assembly a,Vector3 p,float width,Material wood,Material metal)
    {
        a.Box("Balcao lateral",p+Vector3.up*.55f,new Vector3(width,1.1f,.85f),wood,true);
        a.Box("Tampo",p+Vector3.up*1.13f,new Vector3(width+.08f,.08f,.91f),metal);
    }

    static void Window(Assembly a,Vector3 p,Vector2 size,bool side,Material trim,Material glass,Material frame)
    {
        Vector3 dimensions=side?new Vector3(.075f,size.y,size.x):new Vector3(size.x,size.y,.075f);
        Vector3 border=side?new Vector3(.055f,size.y+.16f,size.x+.16f):new Vector3(size.x+.16f,size.y+.16f,.055f);
        a.Box("Moldura da janela",p,border,trim);
        a.Box("Vidro opaco",p,dimensions,glass);
        a.Box("Montante da janela",p,side?new Vector3(.09f,size.y,.055f):new Vector3(.055f,size.y,.09f),frame);
    }

    static Material Material(string name,Color color,string texture,float scale,float smoothness=.12f,float metallic=0)
    {
        if(Materials.TryGetValue(name,out var cached)&&cached)return cached;
        string path=Root+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=Shader.Find("Horizonte/Recovered City Surface");
        if (!shader) shader=Shader.Find("Standard");
        if(!m) {m=new Material(shader){name="Exemplo_"+name};AssetDatabase.CreateAsset(m,path);}
        m.shader=shader;m.color=color;m.enableInstancing=true;
        var tex=string.IsNullOrEmpty(texture)?null:AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/DowntownCity/Textures/"+texture);
        m.mainTexture=tex;
        if(m.HasProperty("_TopTex"))
        {
            m.SetTexture("_TopTex",tex);m.SetColor("_TopColor",color);m.SetFloat("_MetersPerTile",scale);m.SetFloat("_TopMetersPerTile",scale);
            m.SetFloat("_Facade",0);m.SetFloat("_Smoothness",smoothness);m.SetFloat("_Metallic",metallic);
        }
        else {m.SetFloat("_Glossiness",smoothness);m.SetFloat("_Metallic",metallic);}
        EditorUtility.SetDirty(m);Materials[name]=m;return m;
    }

    static Color Accent(string kind)
    {
        switch(kind)
        {
            case "hospital":return new Color(.12f,.43f,.38f);
            case "police":return new Color(.12f,.22f,.34f);
            case "cityhall":return new Color(.18f,.34f,.36f);
            case "warehouse":return new Color(.57f,.28f,.10f);
            case "airport":return new Color(.15f,.30f,.46f);
            default:return new Color(.32f,.36f,.27f);
        }
    }

    static string Label(string kind)
    {
        switch(kind)
        {
            case "house":return "Casa";case "apartment":return "Residencial";
            case "commercial":return "Comercio Horizonte";case "hospital":return "Hospital Horizonte";
            case "police":return "Delegacia";case "cityhall":return "Prefeitura";
            case "warehouse":return "Galpao logistica";case "farm":return "Sede rural";
            case "airport":return "Terminal regional";case "community":return "Moradia Vila Nova";
            default:return "Edificio";
        }
    }

    static Mesh Cube()
    {
        if(cube)return cube;
        var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary);return cube;
    }

    static Mesh Roof()
    {
        if(pitchedRoof)return pitchedRoof;
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        Vector3 a=new Vector3(-.5f,0,-.5f),b=new Vector3(.5f,0,-.5f),c=new Vector3(.5f,0,.5f),d=new Vector3(-.5f,0,.5f),
            e=new Vector3(0,1,-.5f),f=new Vector3(0,1,.5f);
        Action<Vector3[]> face=points=>
        {
            int start=vertices.Count;vertices.AddRange(points);
            for(int i=0;i<points.Length;i++)uv.Add(new Vector2(points[i].x+.5f,points[i].z+.5f));
            for(int i=1;i<points.Length-1;i++){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
        };
        face(new[]{a,e,b});face(new[]{d,c,f});face(new[]{a,d,f,e});face(new[]{b,e,f,c});face(new[]{a,b,c,d});
        pitchedRoof=new Mesh{name="Cobertura ceramica modular"};pitchedRoof.SetVertices(vertices);pitchedRoof.SetUVs(0,uv);pitchedRoof.SetTriangles(triangles,0);
        pitchedRoof.RecalculateNormals();pitchedRoof.RecalculateBounds();return pitchedRoof;
    }

    static void PrepareFolders()
    {
        Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Meshes");Directory.CreateDirectory(Root+"/Prefabs");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    static uint Hash(string text)
    {
        uint result=2166136261;unchecked{foreach(char c in text)result=(result^c)*16777619;}return result;
    }
}
