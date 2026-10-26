using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Builds an original neighbourhood from the existing Downtown kit, without replacing game systems.
public static class CityNeighborhoodSetup
{
    const string ScenePath="Assets/Scenes/TestePersonagem.unity";
    const string RootName="Bairro Horizonte - Praca Central";
    const string Generated="Assets/DowntownCity/Neighborhood";
    const string Review="../tools/neighborhood-review";
    const float Ground=-.15f;
    static Dictionary<string,Material> materials;
    static Transform pavement,buildings,park,furniture,limits;
    static readonly List<string> report=new List<string>();
    static readonly List<Light> streetLights=new List<Light>();
    static int buildingCount,treeCount,benchCount;

    struct Footprint
    {
        public string asset;public float x,z,yaw;
        public Footprint(string name,float x,float z,float yaw){asset=name;this.x=x;this.z=z;this.yaw=yaw;}
    }

    [MenuItem("Horizonte/Montar bairro com praca central")]
    public static void Create()
    {
        Directory.CreateDirectory(Review);Directory.CreateDirectory(Generated);
        string backup=Path.Combine(Review,"scene-before-neighborhood-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity");
        File.Copy(ScenePath,backup,false);
        materials=CityStreetSetup.PrepareNeighborhoodAssets();
        foreach(var material in materials.Values)material.enableInstancing=true;
        materials["Park_Grass"]=PlainMaterial("Park_Grass",new Color(.16f,.29f,.12f));
        materials["Park_Wood"]=PlainMaterial("Park_Wood",new Color(.32f,.16f,.075f));
        materials["Park_Trunk"]=PlainMaterial("Park_Trunk",new Color(.22f,.13f,.075f));
        materials["Park_Water"]=PlainMaterial("Park_Water",new Color(.075f,.31f,.36f),.72f);
        materials["Road_Paint"]=PlainMaterial("Road_Paint",new Color(.95f,.78f,.28f));
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var player=Object.FindAnyObjectByType<ThirdPersonPlayer>();
        if(!player||!Camera.main)throw new Exception("Personagem ou camera existente ausente.");
        foreach(var old in scene.GetRootGameObjects())
            if(old.name==RootName||old.name=="Centro - Rua de teste")Object.DestroyImmediate(old);
        var city=new GameObject(RootName);
        var map=city.AddComponent<NeighborhoodMap>();map.halfExtent=60;map.groundLevel=Ground;
        pavement=Group(city.transform,"Ruas e calcadas");buildings=Group(city.transform,"Oito lotes do bairro");
        park=Group(city.transform,"Praca Central");furniture=Group(city.transform,"Mobiliario urbano");limits=Group(city.transform,"Limites");
        report.Clear();streetLights.Clear();buildingCount=treeCount=benchCount=0;
        report.Add("Backup da cena anterior="+Path.GetFullPath(backup));
        BuildPavement();BuildBlocks();BuildSquare();BuildStreetFurniture();BuildLimits();
        CityStreetSetup.PrepareNeighborhoodLighting(city);
        var cycle=city.GetComponent<CityDayNight>();cycle.streetLights=streetLights.ToArray();cycle.startAtNight=true;cycle.SetNight(true);
        ConfigureNightLights(cycle);
        PlacePlayerAndCamera(player);
        QualitySettings.shadowDistance=38;QualitySettings.shadowResolution=ShadowResolution.Medium;
        QualitySettings.shadowCascades=2;QualitySettings.antiAliasing=2;
        QualitySettings.anisotropicFiltering=AnisotropicFiltering.Enable;
        RenderSettings.fogStartDistance=80;RenderSettings.fogEndDistance=165;
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
        PrefabUtility.SaveAsPrefabAsset(player.gameObject,"Assets/Prefabs/Jogador.prefab");AssetDatabase.SaveAssets();
        report.Add("Mapa=120 x 120 m; limites fisicos=+/-59.5 m; asfalto=6 m; calcadas=3 m; ruas em X/Z +/-22 m");
        report.Add("Praca central=32 x 32 m, com caminhos largos e origem livre; fonte em (0,0,10)");
        report.Add("Edificios="+buildingCount+" (8 pequenos, 6 medios, 2 grandes), escala 1; todos fechados por BoxCollider estatico");
        report.Add("Arvores low poly="+treeCount+"; bancos="+benchCount+"; luzes reais="+streetLights.Count+", todas sem sombras");
        report.Add("LOD afeta somente renderers; colliders permanecem ativos. Materiais compartilhados e instancing habilitado.");
        report.Add("Spawn=(0,-0.1,0); personagem e componentes preservados; base invisivel continua em Y=-0.15.");
        File.WriteAllLines(Path.Combine(Review,"setup.txt"),report);
        Debug.Log("NEIGHBORHOOD_SETUP_OK\n"+string.Join("\n",report));
    }

    [MenuItem("Horizonte/Iluminar noite do bairro")]
    public static void ApplyNightLighting()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var city=GameObject.Find(RootName);
        if(!city)throw new Exception("Bairro existente ausente; nao foi recriado para ajustar a luz.");
        var cycle=city.GetComponent<CityDayNight>();
        if(!cycle)throw new Exception("Ciclo de dia/noite ausente.");
        ConfigureNightLights(cycle);
        cycle.startAtNight=true;cycle.SetNight(true);
        foreach(var renderer in city.GetComponentsInChildren<Renderer>(true))
            foreach(var material in renderer.sharedMaterials)
                if(material&&(material.name.StartsWith("MI_FakeInterior_")||material.name.StartsWith("City_Lamp")))
                    EditorUtility.SetDirty(material);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("NEIGHBORHOOD_NIGHT_LIGHTING_OK: ceu noturno, luz ambiente de preenchimento, 8 postes sem sombras locais.");
    }

    static void ConfigureNightLights(CityDayNight cycle)
    {
        // Alternate the working fixtures on opposite sides, covering all four approaches.
        var positions=new[]{new Vector3(-49,4.64f,-17.6f),new Vector3(10,4.64f,-17.6f),
            new Vector3(-10,4.64f,17.6f),new Vector3(49,4.64f,17.6f),
            new Vector3(-17.6f,4.64f,-49),new Vector3(-17.6f,4.64f,10),
            new Vector3(17.6f,4.64f,-10),new Vector3(17.6f,4.64f,49)};
        var lamps=cycle.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Luminaria").ToArray();
        if(cycle.streetLights.Length!=positions.Length)throw new Exception("O bairro deve manter exatamente oito luzes locais.");
        for(int i=0;i<positions.Length;i++)
        {
            var light=cycle.streetLights[i];if(!light)throw new Exception("Luz de poste ausente.");
            var position=positions[i];
            var fixture=lamps.OrderBy(t=>(t.position-position).sqrMagnitude).First();
            if(Vector2.Distance(new Vector2(fixture.position.x,fixture.position.z),new Vector2(position.x,position.z))>.1f)
                throw new Exception("Luminaria existente nao encontrada em "+position);
            light.transform.SetParent(fixture.parent,true);light.transform.position=position;
            var inward=new Vector3(-position.x,0,-position.z).normalized;
            // A slight tilt reaches the central garden while retaining light on the road.
            light.transform.rotation=Quaternion.LookRotation(Vector3.down+(Mathf.Max(Mathf.Abs(position.x),Mathf.Abs(position.z))<20?inward*.25f:Vector3.zero));
            light.type=LightType.Spot;light.range=26;light.spotAngle=145;light.innerSpotAngle=105;
            light.intensity=4.2f;light.color=new Color(1,.83f,.6f);light.shadows=LightShadows.None;
            light.renderMode=LightRenderMode.Auto;
            EditorUtility.SetDirty(light);
        }
        cycle.SetNight(true);EditorUtility.SetDirty(cycle);
    }

    static Transform Group(Transform parent,string name)
    {var obj=new GameObject(name);obj.transform.SetParent(parent,false);return obj.transform;}

    static Material PlainMaterial(string name,Color color,float smoothness=.12f)
    {
        string path=Generated+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
        material.color=color;material.SetFloat("_Glossiness",smoothness);material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
    }

    static GameObject Box(string name,Transform parent,Vector3 center,Vector3 size,Material material,bool collision=true)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);
        obj.transform.position=center;obj.transform.localScale=size;obj.isStatic=true;obj.layer=0;
        if(material)obj.GetComponent<Renderer>().sharedMaterial=material;else Object.DestroyImmediate(obj.GetComponent<Renderer>());
        if(!collision)Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
    }

    static GameObject Primitive(string name,PrimitiveType type,Transform parent,Vector3 center,Vector3 scale,Material material)
    {
        var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(parent,false);
        obj.transform.position=center;obj.transform.localScale=scale;obj.isStatic=true;obj.layer=0;
        obj.GetComponent<Renderer>().sharedMaterial=material;var collider=obj.GetComponent<Collider>();if(collider)Object.DestroyImmediate(collider);return obj;
    }

    static GameObject Model(string name,Transform parent,Vector3 position,float yaw=0)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DowntownCity/Models/"+name+".fbx");
        if(!asset)throw new Exception("Modelo Downtown ausente: "+name);
        var obj=(GameObject)PrefabUtility.InstantiatePrefab(asset);obj.name=name;obj.transform.SetParent(parent,false);
        obj.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        foreach(var transform in obj.GetComponentsInChildren<Transform>()){transform.gameObject.layer=0;transform.gameObject.isStatic=true;}
        foreach(var renderer in obj.GetComponentsInChildren<MeshRenderer>())
            renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
            {
                string key=source.name.Replace(" (Instance)","");
                if(!materials.TryGetValue(key,out var material))throw new Exception("Material Downtown sem mapeamento: "+key);
                return material;
            }).ToArray();
        return obj;
    }

    static Bounds BoundsOf(GameObject obj)
    {
        bool first=true;var bounds=new Bounds();
        foreach(var filter in obj.GetComponentsInChildren<MeshFilter>())
            foreach(var vertex in filter.sharedMesh.vertices)
            {var point=filter.transform.TransformPoint(vertex);if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
        return bounds;
    }

    static void AddLod(GameObject obj,float cutoff)
    {
        var renderers=obj.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
        var lod=obj.GetComponent<LODGroup>();
        if(!lod)lod=obj.AddComponent<LODGroup>();
        lod.SetLODs(new[]{new LOD(cutoff,renderers)});lod.RecalculateBounds();lod.fadeMode=LODFadeMode.None;
    }

    static void BuildPavement()
    {
        Box("Base solida do bairro",pavement,new Vector3(0,-.45f,0),new Vector3(120,.6f,120),materials["MI_Asphalt"]).GetComponent<Renderer>().enabled=false;
        var roads=new List<Rect>();
        foreach(float axis in new[]{-22f,22f})
        {
            roads.Add(Rect.MinMaxRect(axis-3,-60,axis+3,60));
            foreach(var segment in new[]{new Vector2(-60,-25),new Vector2(-19,19),new Vector2(25,60)})
                roads.Add(Rect.MinMaxRect(segment.x,axis-3,segment.y,axis+3));
        }
        Surface("Asfalto - malha unica sem sobreposicao",pavement,roads,Ground+.002f,materials["MI_Asphalt"],"Roads");
        var ranges=new[]{new Vector2(-59.5f,-25),new Vector2(-19,19),new Vector2(25,59.5f)};
        for(int x=0;x<3;x++)for(int z=0;z<3;z++)
        {
            if(x==1&&z==1)continue;
            var xr=ranges[x];var zr=ranges[z];
            Box("Calcada e base do lote "+x+"-"+z,pavement,new Vector3((xr.x+xr.y)/2,-.15f,(zr.x+zr.y)/2),new Vector3(xr.y-xr.x,.3f,zr.y-zr.x),null);
            Surface("Piso do lote "+x+"-"+z,pavement,new[]{Rect.MinMaxRect(xr.x,zr.x,xr.y,zr.y)},.002f,materials["City_Ground"],"Lot-"+x+"-"+z);
        }
        Surface("Piso da praca",park,new[]{Rect.MinMaxRect(-19,-19,19,19)},Ground+.002f,materials["City_Ground"],"Square");
        foreach(float side in new[]{-1f,1f})foreach(var segment in new[]{new Vector2(-19,-2.5f),new Vector2(2.5f,19)})
        {
            Box("Passeio da praca N-S",pavement,new Vector3((segment.x+segment.y)/2,-.15f,side*17.5f),new Vector3(segment.y-segment.x,.3f,3),materials["City_Ground"]);
        }
        foreach(float side in new[]{-1f,1f})foreach(var segment in new[]{new Vector2(-16,-2.5f),new Vector2(2.5f,16)})
            Box("Passeio da praca L-O",pavement,new Vector3(side*17.5f,-.15f,(segment.x+segment.y)/2),new Vector3(3,.3f,segment.y-segment.x),materials["City_Ground"]);
        var markings=new List<Rect>();
        foreach(float road in new[]{-22f,22f})for(float distance=-56;distance<=56;distance+=4)
        {
            if(Mathf.Abs(Mathf.Abs(distance)-22)<7||Mathf.Abs(distance)<4)continue;
            markings.Add(Rect.MinMaxRect(distance-.8f,road-.045f,distance+.8f,road+.045f));
            markings.Add(Rect.MinMaxRect(road-.045f,distance-.8f,road+.045f,distance+.8f));
        }
        Surface("Pintura das pistas",pavement,markings,Ground+.008f,materials["Road_Paint"],"Markings");
        foreach(float side in new[]{-1f,1f}){Crosswalk(0,side*22,0);Crosswalk(side*22,0,90);}
        foreach(float x in new[]{-22f,22f})foreach(float z in new[]{-22f,22f})
        {Crosswalk(x-8,z,0);Crosswalk(x+8,z,0);Crosswalk(x,z-8,90);Crosswalk(x,z+8,90);}
    }

    static void Surface(string name,Transform parent,IEnumerable<Rect> rectangles,float y,Material material,string file)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(var rect in rectangles)
        {
            int start=vertices.Count;
            var points=new[]{new Vector3(rect.xMin,y,rect.yMin),new Vector3(rect.xMin,y,rect.yMax),new Vector3(rect.xMax,y,rect.yMax),new Vector3(rect.xMax,y,rect.yMin)};
            vertices.AddRange(points);uv.AddRange(points.Select(v=>new Vector2(v.x/6,v.z/6)));
            triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        var mesh=new Mesh {name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Generated+"/"+file+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
        var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(parent,false);obj.isStatic=true;obj.layer=0;
        obj.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    static void Crosswalk(float x,float z,float yaw)
    {
        var obj=Model("Decal_Crosswalk",pavement,Vector3.zero,yaw);var bounds=BoundsOf(obj);
        obj.transform.position+=new Vector3(x-bounds.center.x,Ground+.008f-bounds.min.y,z-bounds.center.z);
        obj.name="Faixa de pedestres";AddLod(obj,.018f);
    }

    static void BuildBlocks()
    {
        var placements=new[]{
            new Footprint("Building_Small_1",-50.5f,36,180),new Footprint("Building_Medium_2_001",-36.2f,35.3f,180),
            new Footprint("Building_Medium_2_001",36.2f,35.3f,180),new Footprint("Building_Small_1",50.5f,36,180),
            new Footprint("Building_Small_1",-50.5f,-36,0),new Footprint("Building_Medium_2_001",-36.2f,-35.3f,0),
            new Footprint("Building_Medium_2_001",36.2f,-35.3f,0),new Footprint("Building_Small_1",50.5f,-36,0),
            new Footprint("Building_Small_1",-36,-8,90),new Footprint("Building_Medium_2_001",-35.3f,6.3f,90),
            new Footprint("Building_Small_1",36,-8,270),new Footprint("Building_Medium_2_001",35.3f,6.3f,270),
            new Footprint("Building_Small_1",-8,35.8f,180),new Footprint("Building_Large_2",7.1f,40,90),
            new Footprint("Building_Large_2",-7.1f,-40,270),new Footprint("Building_Small_1",8,-35.8f,0)};
        foreach(var placement in placements)
        {
            var obj=Model(placement.asset,buildings,Vector3.zero,placement.yaw);var bounds=BoundsOf(obj);
            obj.transform.position+=new Vector3(placement.x-bounds.center.x,-bounds.min.y,placement.z-bounds.center.z);bounds=BoundsOf(obj);
            obj.name="Edificio "+(++buildingCount).ToString("00")+" - "+placement.asset;
            var collider=new GameObject("Volume solido do edificio",typeof(BoxCollider));collider.transform.SetParent(obj.transform,true);
            collider.transform.position=bounds.center;collider.isStatic=true;collider.GetComponent<BoxCollider>().size=bounds.size;
            AddLod(obj,.105f);
            report.Add(obj.name+" centro="+bounds.center.ToString("F2")+" tamanho="+bounds.size.ToString("F2"));
        }
    }

    static void BuildSquare()
    {
        // Four planted beds leave a five-metre cross of unobstructed paths through the origin.
        foreach(float x in new[]{-10f,10f})foreach(float z in new[]{-10f,10f})
        {
            Box("Canteiro da praca",park,new Vector3(x,-.02f,z),new Vector3(9,.20f,9),materials["MI_Concrete"]);
            Box("Gramado da praca",park,new Vector3(x,.085f,z),new Vector3(8.55f,.025f,8.55f),materials["Park_Grass"],false);
            Tree(x-2,z-1.7f,park);Tree(x+2,z+1.7f,park);
        }
        Fountain();
        foreach(float side in new[]{-1f,1f})foreach(float offset in new[]{-10f,10f})
        {Bench(new Vector3(side*4.2f,Ground,offset),side>0?90:270);Bench(new Vector3(offset,Ground,side*4.2f),side>0?180:0);}
        foreach(var point in new[]{new Vector2(-4.1f,-14),new Vector2(4.1f,14),new Vector2(-14,4.1f),new Vector2(14,-4.1f)})TrashBin(point.x,point.y);
        foreach(float side in new[]{-1f,1f})
        {
            Model("Prop_Planter_Single",park,new Vector3(side*14.8f,Ground,side*3.75f));
            var planter=park.GetChild(park.childCount-1).gameObject;var pb=BoundsOf(planter);
            Box("Colisao da floreira",planter.transform,pb.center,pb.size,null);AddLod(planter,.025f);
        }
    }

    static void Fountain()
    {
        var group=Group(park,"Fonte da Praca Central");Vector3 center=new Vector3(0,Ground,10);
        Primitive("Base da fonte",PrimitiveType.Cylinder,group,center+Vector3.up*.15f,new Vector3(3.7f,.15f,3.7f),materials["MI_Concrete"]);
        Primitive("Agua da fonte",PrimitiveType.Cylinder,group,center+Vector3.up*.31f,new Vector3(3.1f,.015f,3.1f),materials["Park_Water"]);
        Primitive("Coluna da fonte",PrimitiveType.Cylinder,group,center+Vector3.up*.68f,new Vector3(.45f,.42f,.45f),materials["MI_Concrete"]);
        Primitive("Taca da fonte",PrimitiveType.Cylinder,group,center+Vector3.up*1.12f,new Vector3(1.2f,.10f,1.2f),materials["MI_Concrete"]);
        Box("Colisao da fonte",group,center+Vector3.up*.6f,new Vector3(3.7f,1.2f,3.7f),null);AddLod(group.gameObject,.035f);
    }

    static void Bench(Vector3 position,float yaw)
    {
        var group=Group(park,"Banco da praca "+(++benchCount));group.position=position;
        Box("Assento de madeira",group,position+Vector3.up*.47f,new Vector3(2.2f,.13f,.58f),materials["Park_Wood"],false);
        Box("Encosto de madeira",group,position+new Vector3(0,.83f,.25f),new Vector3(2.2f,.42f,.12f),materials["Park_Wood"],false);
        foreach(float side in new[]{-1f,1f})Box("Suporte metalico",group,position+new Vector3(side*.82f,.22f,0),new Vector3(.12f,.44f,.43f),materials["City_Metal"],false);
        Box("Colisao do banco",group,position+Vector3.up*.5f,new Vector3(2.2f,1,.65f),null);
        group.rotation=Quaternion.Euler(0,yaw,0);AddLod(group.gameObject,.025f);
    }

    static void TrashBin(float x,float z)
    {
        var group=Group(park,"Lixeira da praca");
        Primitive("Corpo da lixeira",PrimitiveType.Cylinder,group,new Vector3(x,.32f,z),new Vector3(.48f,.47f,.48f),materials["City_Metal"]);
        Primitive("Tampa da lixeira",PrimitiveType.Cylinder,group,new Vector3(x,.82f,z),new Vector3(.55f,.04f,.55f),materials["Park_Wood"]);
        Box("Colisao da lixeira",group,new Vector3(x,.32f,z),new Vector3(.5f,.94f,.5f),null);AddLod(group.gameObject,.025f);
    }

    static void Tree(float x,float z,Transform parent)
    {
        var group=Group(parent,"Arvore low poly "+(++treeCount));
        Primitive("Tronco",PrimitiveType.Cylinder,group,new Vector3(x,1.15f,z),new Vector3(.23f,1.15f,.23f),materials["Park_Trunk"]);
        var canopy=new GameObject("Copa de oito lados",typeof(MeshFilter),typeof(MeshRenderer));canopy.transform.SetParent(group,false);
        canopy.transform.position=new Vector3(x,3.1f,z);canopy.transform.localScale=new Vector3(1.7f,1.7f,1.7f);canopy.isStatic=true;
        canopy.GetComponent<MeshFilter>().sharedMesh=CanopyMesh();canopy.GetComponent<Renderer>().sharedMaterial=materials["City_Leaves"];
        Box("Colisao do tronco",group,new Vector3(x,1.15f,z),new Vector3(.32f,2.3f,.32f),null);AddLod(group.gameObject,.04f);
    }

    static Mesh CanopyMesh()
    {
        string path=Generated+"/LowPolyCanopy.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;int start=vertices.Count;
            var left=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var right=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
            vertices.AddRange(new[]{Vector3.up*1.1f,right,left,Vector3.down*.65f,left,right});
            triangles.AddRange(new[]{start,start+1,start+2,start+3,start+4,start+5});
        }
        mesh=new Mesh {name="Eight-sided tree canopy"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }

    static void BuildStreetFurniture()
    {
        int lampIndex=0;
        foreach(float z in new[]{-17.6f,17.6f})foreach(float x in new[]{-49f,-10f,10f,49f})StreetLamp(x,z,lampIndex++%2==0);
        foreach(float x in new[]{-17.6f,17.6f})foreach(float z in new[]{-49f,-10f,10f,49f})StreetLamp(x,z,lampIndex++%2==0);
        foreach(float side in new[]{-1f,1f})
        {
            foreach(float coordinate in new[]{-50f,-34f,34f,50f})Tree(coordinate,side*54,furniture);
            foreach(float coordinate in new[]{-49f,-8f,8f,49f})Tree(side*54,coordinate,furniture);
        }
        foreach(float x in new[]{-22f,22f})foreach(float z in new[]{-9f,9f})
            Model("Prop_ManholeCover",furniture,new Vector3(x+1.4f,Ground+.009f,z));
        foreach(float z in new[]{-22f,22f})foreach(float x in new[]{-42f,42f})
            Model("Prop_Drain",furniture,new Vector3(x,Ground+.003f,z+2.55f));
    }

    static void StreetLamp(float x,float z,bool realLight)
    {
        var group=Group(furniture,"Poste do bairro");
        Primitive("Poste",PrimitiveType.Cylinder,group,new Vector3(x,2.45f,z),new Vector3(.12f,2.45f,.12f),materials["City_Metal"]);
        Box("Braco do poste",group,new Vector3(x,4.82f,z),new Vector3(.9f,.10f,.12f),materials["City_Metal"],false);
        Box("Luminaria",group,new Vector3(x,4.76f,z),new Vector3(.65f,.10f,.35f),materials["City_Lamp"],false);
        Box("Colisao do poste",group,new Vector3(x,2.45f,z),new Vector3(.2f,4.9f,.2f),null);
        if(realLight&&streetLights.Count<8)
        {
            var light=new GameObject("Luz do poste",typeof(Light)).GetComponent<Light>();light.transform.SetParent(group,false);
            light.transform.SetPositionAndRotation(new Vector3(x,4.64f,z),Quaternion.Euler(90,0,0));
            light.type=LightType.Spot;light.range=14;light.spotAngle=125;light.innerSpotAngle=82;light.intensity=2.1f;
            light.color=new Color(1,.82f,.57f);light.shadows=LightShadows.None;streetLights.Add(light);
        }
        AddLod(group.gameObject,.025f);
    }

    static void BuildLimits()
    {
        foreach(float side in new[]{-1f,1f})
        {
            Box("Muro do bairro norte-sul",limits,new Vector3(0,1,side*59.5f),new Vector3(119,2,.7f),materials["City_BrickBoundary"]);
            Box("Muro do bairro leste-oeste",limits,new Vector3(side*59.5f,1,0),new Vector3(.7f,2,119),materials["City_BrickBoundary"]);
        }
    }

    static void PlacePlayerAndCamera(ThirdPersonPlayer player)
    {
        var controller=player.GetComponent<CharacterController>();controller.enabled=false;
        player.spawnPosition=new Vector3(0,-.1f,0);player.spawnYaw=0;player.transform.SetPositionAndRotation(player.spawnPosition,Quaternion.identity);
        controller.height=1.8f;controller.center=new Vector3(0,.91f,0);controller.radius=.26f;controller.stepOffset=.25f;controller.enabled=true;
        var camera=Camera.main;camera.nearClipPlane=.15f;camera.farClipPlane=185;camera.clearFlags=CameraClearFlags.Skybox;
        var orbit=camera.GetComponent<ThirdPersonCamera>();if(orbit){orbit.yaw=0;orbit.pitch=16;orbit.distance=4.2f;}
        camera.transform.SetPositionAndRotation(new Vector3(0,2.2f,-4.05f),Quaternion.Euler(16,0,0));
    }

    public static void RenderPreview()
    {
        EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory(Review);
        var camera=Camera.main;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        try
        {
            Render(camera,"overview",new Vector3(86,112,-92),new Vector3(0,0,0));
            Render(camera,"square",new Vector3(-14,8,-17),new Vector3(0,1,8));
            Render(camera,"street",new Vector3(-22,2.2f,-12),new Vector3(-22,2,22));
        }
        finally{camera.transform.SetPositionAndRotation(oldPosition,oldRotation);}
    }

    static void Render(Camera camera,string name,Vector3 position,Vector3 target)
    {
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        var previous=RenderTexture.active;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);var rt=new RenderTexture(1280,720,24);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Review,name+".png"),texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
    }
}
