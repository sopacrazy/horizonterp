using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// An independently authored, reproducible city example. Never opens/saves the official scene.
public static class CityExampleSetup
{
    const string AssetsRoot = "Assets/CityExample";
    const string SceneFolder = "Assets/Scenes/CityExample";
    const string ScenePath = SceneFolder + "/CidadeHorizonteExemplo.unity";
    const string Review = "../tools/city-example-review";
    const int Cell = 128;
    const float Edge = 704;
    static readonly float[] XRoads = { -576, -384, -192, -56, 56, 192, 384, 576 };
    static readonly float[] ZRoads = { -576, -384, -192, 0, 192, 384, 576 };
    static readonly List<Street> streets = new List<Street>();
    static readonly List<Rect> paved = new List<Rect>();
    static readonly List<Rect> specialAsphalt = new List<Rect>();
    static readonly List<Bounds> footprints = new List<Bounds>();
    static readonly List<CityExamplePreview.Route> routes = new List<CityExamplePreview.Route>();
    static readonly List<CityExamplePreview.Destination> destinations = new List<CityExamplePreview.Destination>();
    static readonly Dictionary<Vector2Int, GameObject> cells = new Dictionary<Vector2Int, GameObject>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, QuadMesh> surfaces = new Dictionary<string, QuadMesh>();
    static Transform city;
    static int buildingCount, treeCount;

    sealed class Street
    {
        public Vector3 a, b;
        public float width;
        public Rect asphalt, reserve;
    }

    sealed class QuadMesh
    {
        public Transform parent;
        public Material material;
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
        public void Add(float x0, float z0, float x1, float z1, float y)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { new Vector3(x0,y,z0),new Vector3(x1,y,z0),new Vector3(x1,y,z1),new Vector3(x0,y,z1) });
            uv.AddRange(new[] { new Vector2(x0,z0)/6,new Vector2(x1,z0)/6,new Vector2(x1,z1)/6,new Vector2(x0,z1)/6 });
            triangles.AddRange(new[] { i,i+2,i+1,i,i+3,i+2 });
        }
    }

    [MenuItem("Horizonte/Exemplo independente/Criar cidade completa de teste")]
    public static void Create()
    {
        Directory.CreateDirectory(AssetsRoot + "/Meshes");
        Directory.CreateDirectory(AssetsRoot + "/Materials");
        Directory.CreateDirectory(SceneFolder); Directory.CreateDirectory(Review);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var original = ProtectedHashes();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        cells.Clear(); materials.Clear(); surfaces.Clear(); streets.Clear(); paved.Clear(); specialAsphalt.Clear();
        routes.Clear(); footprints.Clear(); destinations.Clear(); buildingCount = treeCount = 0;
        city = new GameObject("Cidade Horizonte - EXEMPLO INDEPENDENTE").transform;
        PrepareMaterials(); BuildStreetPlan(); BuildDistricts(); BuildBridges();
        BuildRiverfront(); BuildAirport(); BuildRural(); BuildCoast(); BuildUrbanFurniture();
        BuildBoundaries(); BuildTerrain(); FlushSurfaces();
        Physics.SyncTransforms();
        string validation = CityExampleValidation.Validate(city.gameObject, routes.ToArray(), footprints.ToArray(),
            streets.Select(s => RectBounds(s.reserve)).ToArray(), Path.GetFullPath(Review));
        foreach (var entry in cells.OrderBy(p => p.Key.x).ThenBy(p => p.Key.y)) CombineScenery(entry.Key, entry.Value);
        var map = city.gameObject.AddComponent<NeighborhoodMap>();
        map.halfExtent = 705; map.minY = -8; map.maxY = 160; map.groundLevel = 0;
        map.spawnPosition = new Vector3(96,.08f,0); map.isOfficialCity = false;
        map.mapCameraHeight = 220; map.displayName = "Cidade Horizonte · Exemplo"; map.worldCode = "cidade_exemplo_local";
        var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"));
        playerObject.name = "Jogador - Exemplo separado";
        var player = playerObject.GetComponent<ThirdPersonPlayer>();
        var controller = playerObject.GetComponent<CharacterController>();
        controller.enabled = false; player.spawnPosition = map.spawnPosition; player.spawnYaw = 90;
        playerObject.transform.SetPositionAndRotation(map.spawnPosition, Quaternion.Euler(0,90,0)); controller.enabled = true;
        var cam = new GameObject("Main Camera", typeof(Camera),typeof(AudioListener),typeof(ThirdPersonCamera)).GetComponent<Camera>();
        cam.tag = "MainCamera"; cam.nearClipPlane = .12f; cam.farClipPlane = 700; cam.fieldOfView = 58;
        cam.clearFlags = CameraClearFlags.Skybox;
        var orbit = cam.GetComponent<ThirdPersonCamera>(); orbit.target = player.transform; orbit.yaw = 90; orbit.pitch = 16;
        player.cameraTransform = cam.transform;
        ConfigureLighting();
        var preview = city.gameObject.AddComponent<CityExamplePreview>();
        preview.player = player; preview.destinations = destinations.ToArray(); preview.routes = routes.ToArray();
        preview.buildingFootprints = footprints.ToArray(); preview.reviewDirectory = Path.GetFullPath(Review);
        preview.reviewCameras = ReviewCameras();
        // Kept in the main scene because a serialized collider reference cannot target an unloaded sector.
        var obstacle = Box(city, "Barreira de teste - fora da via", new Vector3(112,.65f,-17),new Vector3(3,1.3f,.6f),materials["concrete"]);
        preview.collisionObstacle = obstacle.GetComponent<Collider>();
        var stream = city.gameObject.AddComponent<CitySectorStreaming>(); stream.target = player.transform;
        stream.mobileLoadDistance = 150; stream.desktopLoadDistance = 230;
        BuildHorizon(stream);
        var definitions = new List<CitySectorStreaming.Sector>(); var paths = new List<string>();
        EditorSceneManager.SaveScene(scene,ScenePath);
        foreach (var pair in cells.OrderBy(p => p.Key.x).ThenBy(p => p.Key.y))
        {
            string name = "CitySector_Example_" + pair.Key.x + "_" + pair.Key.y;
            string path = SceneFolder + "/" + name + ".unity";
            var sector = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            pair.Value.transform.SetParent(null,true); SceneManager.MoveGameObjectToScene(pair.Value,sector);
            Bounds bounds = new Bounds(new Vector3((pair.Key.x+.5f)*Cell,0,(pair.Key.y+.5f)*Cell),new Vector3(Cell,1,Cell));
            foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            definitions.Add(new CitySectorStreaming.Sector { scene = name, bounds = bounds });
            EditorSceneManager.SaveScene(sector,path); EditorSceneManager.CloseScene(sector,true); paths.Add(path);
        }
        SceneManager.SetActiveScene(scene); stream.sectors = definitions.ToArray();
        preview.streaming = stream; preview.expectedSectorCount = definitions.Count;
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        foreach (var pair in original)
            if (Hash(pair.Key) != pair.Value) throw new Exception("Arquivo oficial alterado indevidamente: " + pair.Key);
        File.WriteAllText(Review + "/setup.json",JsonUtility.ToJson(new SetupReport {
            scene = ScenePath, standalone = "Builds/CidadeExemplo/CidadeHorizonteExemplo.exe", sectors = definitions.Count,
            buildings = buildingCount, trees = treeCount, accessibleDestinations = destinations.Count, roadSegments = streets.Count,
            routeKilometres = routes.Sum(r => Length(r.points))/1000f, extentMetres = 1408, cellMetres = Cell,
            validation = validation, officialFilesUnchanged = true, sectorScenes = paths.ToArray(),
            notes = "Cidade de exemplo original. Interior acessivel em edificios selecionados; sem trafego/NPCs/simulacao aeroportuaria. Desempenho Android ainda precisa de aparelho real."
        },true));
        Debug.Log("CITY_EXAMPLE_SETUP_OK sectors="+definitions.Count+" buildings="+buildingCount+" destinations="+destinations.Count);
    }

    [Serializable] sealed class SetupReport
    {
        public string scene,standalone,validation,notes; public int sectors,buildings,trees,accessibleDestinations,roadSegments,extentMetres,cellMetres;
        public float routeKilometres; public bool officialFilesUnchanged; public string[] sectorScenes;
    }

    public static void CreateAndBuildWindows()
    {
        Create(); BuildWindows();
    }
    public static void BuildWindows()
    {
        var paths = new[] { ScenePath }.Concat(Directory.GetFiles(SceneFolder,"CitySector_Example_*.unity").OrderBy(p=>p).Select(p=>p.Replace('\\','/'))).ToArray();
        string output = "Builds/CidadeExemplo/CidadeHorizonteExemplo.exe"; Directory.CreateDirectory(Path.GetDirectoryName(output));
        // Explicit scene list; official EditorBuildSettings and official executable are preserved.
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=paths,locationPathName=output,
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.None });
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Falha na compilacao da cidade de exemplo.");
    }

    static void BuildStreetPlan()
    {
        foreach (float x in XRoads) for(int i=0;i<ZRoads.Length-1;i++)
            StreetLink(new Vector3(x,0,ZRoads[i]),new Vector3(x,0,ZRoads[i+1]),Mathf.Abs(x)==192?16:12);
        foreach(float z in ZRoads)
        {
            for(int i=0;i<XRoads.Length-1;i++)
            {
                if(XRoads[i]<0&&XRoads[i+1]>0&&z!=-384&&z!=0&&z!=384)continue;
                StreetLink(new Vector3(XRoads[i],0,z),new Vector3(XRoads[i+1],0,z),z==0?18:12);
            }
            StreetLink(new Vector3(576,0,z),new Vector3(624,0,z),10);
        }
        for(int i=0;i<ZRoads.Length-1;i++)StreetLink(new Vector3(624,0,ZRoads[i]),new Vector3(624,0,ZRoads[i+1]),6);
        StreetLink(new Vector3(-576,0,0),new Vector3(-656,0,0),8);
        StreetLink(new Vector3(-656,0,0),new Vector3(-656,0,192),8);
        StreetLink(new Vector3(-656,0,0),new Vector3(-656,0,-192),8);
        StreetLink(new Vector3(-576,0,384),new Vector3(-644,0,384),10);
        StreetLink(new Vector3(-644,0,384),new Vector3(-644,0,416),10);
    }

    static void StreetLink(Vector3 a,Vector3 b,float width)
    {
        bool horizontal = Mathf.Abs(a.z-b.z)<.01f;
        var rect = horizontal?Rect.MinMaxRect(Mathf.Min(a.x,b.x)-width*.5f,a.z-width*.5f,Mathf.Max(a.x,b.x)+width*.5f,a.z+width*.5f):
            Rect.MinMaxRect(a.x-width*.5f,Mathf.Min(a.z,b.z)-width*.5f,a.x+width*.5f,Mathf.Max(a.z,b.z)+width*.5f);
        var outer = Rect.MinMaxRect(rect.xMin-4,rect.yMin-4,rect.xMax+4,rect.yMax+4);
        streets.Add(new Street { a=a,b=b,width=width,asphalt=rect,reserve=outer });
        Route("Via/"+streets.Count,a,b);
    }

    static void BuildDistricts()
    {
        var columns = new[] { new[]{-576f,-384,-192,-56},new[]{56f,192,384,576} };
        foreach(var side in columns)for(int col=0;col<side.Length-1;col++)for(int row=0;row<ZRoads.Length-1;row++)
        {
            float x0=side[col]+22,x1=side[col+1]-22,z0=ZRoads[row]+22,z1=ZRoads[row+1]-22;
            float cx=(x0+x1)*.5f,cz=(z0+z1)*.5f; int key=col*13+row+(cx>0?100:0);
            if(cx>0&&cx<192&&cz>0&&cz<192)
            {
                // Civic square has a clear, continuous approach from the central avenue.
                Pave(new Rect(x0,z0,x1-x0,z1-z0));
                AddBuilding(new Vector3(126,0,142),new Vector2(44,34),11,"cityhall",0,key,true);
                Route("Acesso/Prefeitura",new Vector3(126,0,0),new Vector3(126,0,152));
                Destination("Prefeitura e praça","cityhall",new Vector3(126,.08f,133),0);
                for(int i=0;i<4;i++)Tree(new Vector3(84,0,40+i*30),5);
                continue;
            }
            if(cx>192&&cx<384&&cz>0&&cz<192)
            {
                Pave(new Rect(x0,z0,x1-x0,z1-z0));
                AddBuilding(new Vector3(264,0,78),new Vector2(38,38),12,"hospital",0,key,true);
                AddBuilding(new Vector3(336,0,142),new Vector2(36,34),10,"police",0,key+1,true);
                Route("Acesso/Hospital",new Vector3(264,0,0),new Vector3(264,0,90));
                Route("Acesso/Delegacia",new Vector3(336,0,192),new Vector3(336,0,182),new Vector3(374,0,182),new Vector3(374,0,112),new Vector3(336,0,112),new Vector3(336,0,153));
                Destination("Hospital","hospital",new Vector3(264,.08f,70),0);
                Destination("Delegacia","police",new Vector3(336,.08f,137),0);
                continue;
            }
            if(cx>0&&cz<-384&&cx<192)
            {
                Pave(new Rect(x0,z0,x1-x0,z1-z0));
                AddBuilding(new Vector3(cx,0,cz+22),new Vector2(58,42),10,"commercial",0,key,true);
                Route("Acesso/Mercado",new Vector3(cx,0,-576),new Vector3(cx,0,cz+30));
                Destination("Mercado e comércio","commercial",new Vector3(cx,.08f,cz+12),0); continue;
            }
            if(cx>384&&cz>384)
            {
                Park(Rect.MinMaxRect(x0,z0,x1,z1));
                Route("Acesso/Parque",new Vector3(cx,0,384),new Vector3(cx,0,576));
                Destination("Parque urbano","park",new Vector3(cx,.08f,cz),0);continue;
            }
            bool port=cx<0&&cx>-192&&cz<-192;
            bool industry=cx<-192&&cz<-192;
            bool community=cx<0&&cx>-384&&cz>192;
            bool centre=cx>0&&cz>-192&&cz<192;
            int nx=port?1:industry?2:community?4:3,nz=industry?2:community?3:2;
            for(int ix=0;ix<nx;ix++)for(int iz=0;iz<nz;iz++)
            {
                float dx=(x1-x0)/nx,dz=(z1-z0)/nz;
                var p=new Vector3(x0+dx*(ix+.5f),0,z0+dz*(iz+.5f));
                string kind=port||industry?"warehouse":community?"community":centre?"commercial":(key%3==0?"apartment":"house");
                var size=new Vector2(Mathf.Min(dx-10,industry?48:community?19:30),Mathf.Min(dz-16,industry?42:community?24:32));
                float h=industry||port?9:community?6:kind=="apartment"?21+key%3*3:centre?12+key%4*4:5.5f;
                bool inside=port&&iz==0 || !industry&&!community&&!centre&&cx>192&&cz>192&&row==4&&ix==1&&iz==0;
                AddBuilding(p,size,h,kind,0,key+ix+iz,inside);
                if(inside)
                {
                    // The strip between the two building rows is reserved as an access lane.
                    float fromZ=ZRoads[row];
                    Route("Acesso/"+kind+"/"+key+"/"+ix,new Vector3(p.x,0,fromZ),new Vector3(p.x,0,p.z+size.y*.25f));
                    if(port&&row==0)Destination("Porto e galpão","warehouse",p+new Vector3(0,.08f,0),0);
                    if(kind=="house"||kind=="apartment")Destination("Bairro residencial · interior","house",p+Vector3.up*.08f,0);
                }
            }
            if(community&&row==4&&col==1)
            {
                float laneZ=z0+(z1-z0)/3;
                Route("Acesso/Comunidade",new Vector3(side[col],0,laneZ),new Vector3(cx,0,laneZ),new Vector3(cx,0,cz));
                Destination("Comunidade planejada","community",new Vector3(cx,.08f,cz),90);
            }
            if(industry&&row==1&&col==0)
            {
                Route("Acesso/Industria",new Vector3(cx,0,ZRoads[row]),new Vector3(cx,0,cz));
                Destination("Distrito industrial","industry",new Vector3(cx,.08f,cz),0);
            }
        }
        Destination("Avenida central","centre",new Vector3(96,.08f,0),90);
        // Keep the first QA destination on the unobstructed avenue, suitable for real walking.
        var first=destinations[destinations.Count-1];destinations.RemoveAt(destinations.Count-1);destinations.Insert(0,first);
    }

    static void AddBuilding(Vector3 p,Vector2 size,float height,string kind,float yaw,int variant,bool inside)
    {
        CityExampleBuildings.Create(CellFor(p),p,size,height,kind,yaw,variant,inside);
        Vector3 rotated = Mathf.Abs(Mathf.Sin(yaw*Mathf.Deg2Rad))>.5f?new Vector3(size.y,height,size.x):new Vector3(size.x,height,size.y);
        footprints.Add(new Bounds(p+Vector3.up*height*.5f,rotated)); buildingCount++;
        Pave(new Rect(p.x-rotated.x*.5f-2,p.z-rotated.z*.5f-2,rotated.x+4,rotated.z+4));
    }

    static void BuildTerrain()
    {
        var xCuts=new SortedSet<float>{-Edge,Edge,-36,36,640,672};
        var zCuts=new SortedSet<float>{-Edge,Edge};
        for(int i=-6;i<=6;i++){if(i*Cell>-Edge&&i*Cell<Edge){xCuts.Add(i*Cell);zCuts.Add(i*Cell);}}
        foreach(var r in streets.SelectMany(s=>new[]{s.asphalt,s.reserve}).Concat(paved).Concat(specialAsphalt))
        {xCuts.Add(r.xMin);xCuts.Add(r.xMax);zCuts.Add(r.yMin);zCuts.Add(r.yMax);}
        float[] xs=xCuts.Where(v=>v>=-Edge&&v<=Edge).ToArray(),zs=zCuts.Where(v=>v>=-Edge&&v<=Edge).ToArray();
        for(int i=0;i<xs.Length-1;i++)for(int j=0;j<zs.Length-1;j++)
        {
            var c=new Vector2((xs[i]+xs[i+1])*.5f,(zs[j]+zs[j+1])*.5f);
            bool river=Mathf.Abs(c.x)<36,bridge=IsBridge(c.y)&&Mathf.Abs(c.x)<36;
            if(c.x>672||river&&!bridge)continue;
            string mat=streets.Any(s=>s.asphalt.Contains(c))||specialAsphalt.Any(p=>p.Contains(c))?"asphalt":streets.Any(s=>s.reserve.Contains(c))||paved.Any(p=>p.Contains(c))?"concrete":c.x>640?"sand":"grass";
            AddQuad(xs[i],zs[j],xs[i+1],zs[j+1],.012f,mat);
        }
        // Continuous sector foundations; no thin collision slivers at road intersections.
        for(int x=-6;x<=5;x++)for(int z=-6;z<=5;z++)
        {
            float x0=Mathf.Max(-Edge,x*Cell),x1=Mathf.Min(672,(x+1)*Cell),z0=Mathf.Max(-Edge,z*Cell),z1=Mathf.Min(Edge,(z+1)*Cell);
            if(x1<=x0||z1<=z0)continue;
            foreach(var land in new[]{Rect.MinMaxRect(-Edge,-Edge,-36,Edge),Rect.MinMaxRect(36,-Edge,672,Edge)})
            {
                float a=Mathf.Max(x0,land.xMin),b=Mathf.Min(x1,land.xMax);if(b<=a)continue;
                ColliderBox(CellFor(new Vector3((a+b)*.5f,0,(z0+z1)*.5f)),"Fundacao continua",new Vector3((a+b)*.5f,-.5f,(z0+z1)*.5f),new Vector3(b-a,1,z1-z0));
            }
        }
        foreach(var s in streets)
        {
            bool horizontal=Mathf.Abs(s.a.z-s.b.z)<.01f;
            float length=Vector3.Distance(s.a,s.b);
            for(float d=15;d<length-12;d+=12)
            {
                Vector3 p=Vector3.Lerp(s.a,s.b,d/length);
                bool crossing=streets.Any(o=>o!=s&&o.asphalt.Contains(new Vector2(p.x,p.z))&&Mathf.Abs(Vector3.Dot((o.b-o.a).normalized,(s.b-s.a).normalized))<.5f);
                if(!crossing)QuadRect(p,horizontal?new Vector2(5,.13f):new Vector2(.13f,5),.02f,"paintYellow");
            }
        }
    }

    static void BuildBridges()
    {
        foreach(float z in new[]{-384f,0,384})
        {
            float width=z==0?26:20;
            Box(CellFor(new Vector3(0,0,z)),"Ponte conectada - tabuleiro",new Vector3(0,-.4f,z),new Vector3(80,.8f,width),materials["concrete"]);
            foreach(float side in new[]{-1f,1f})
            {
                Box(CellFor(new Vector3(0,0,z)),"Guarda corpo ponte",new Vector3(0,.65f,z+side*(width*.5f-.3f)),new Vector3(78,1.3f,.35f),materials["rail"]);
                foreach(float x in new[]{-22f,22f})Box(CellFor(new Vector3(x,0,z)),"Pilar ponte",new Vector3(x,-2.8f,z+side*5),new Vector3(2.5f,5,2.5f),materials["concrete"]);
            }
        }
        Destination("Ponte central e rio","bridge",new Vector3(0,.08f,0),90);
    }

    static void BuildRiverfront()
    {
        // Retaining walls and railings are interrupted exactly at bridge sidewalks.
        foreach(float x in new[]{-36.5f,36.5f})
        {
            for(float z=-696;z<696;z+=8)
            {
                if(IsBridge(z))continue;
                Box(CellFor(new Vector3(x,0,z)),"Muro de margem",new Vector3(x,-1.6f,z),new Vector3(1.2f,3.2f,8),materials["concrete"]);
                Box(CellFor(new Vector3(x,0,z)),"Protecao rio",new Vector3(x,.55f,z),new Vector3(.18f,1.1f,8),materials["rail"]);
            }
        }
        // Water is scenery; banks and bridges have physical barriers.
        Box(city,"Rio cenografico",new Vector3(0,-2.8f,0),new Vector3(72,.04f,1600),materials["water"],false);
    }

    static void BuildAirport()
    {
        // Small regional aerodrome: a 220 m demonstration runway, not a full-size international airport.
        specialAsphalt.Add(new Rect(-662,440,24,220));
        specialAsphalt.Add(new Rect(-656,416,18,28));
        Pave(new Rect(-595,517,19,6));
        for(float z=455;z<650;z+=18)QuadRect(new Vector3(-650,0,z),new Vector2(.6f,8),.03f,"paintWhite");
        AddBuilding(new Vector3(-610,0,520),new Vector2(30,44),8,"airport",-90,0,true);
        Route("Acesso/Aeroporto",new Vector3(-576,0,520),new Vector3(-615,0,520));
        Route("Acesso/Pista",new Vector3(-644,0,416),new Vector3(-650,0,440),new Vector3(-650,0,650));
        Destination("Aeroporto regional · terminal","airport",new Vector3(-605,.08f,520),270);
        // Perimeter excludes the open taxi access at Z=384.
        Box(CellFor(new Vector3(-690,0,550)),"Cerca aeroporto",new Vector3(-690,1,550),new Vector3(.15f,2,250),materials["rail"]);
    }

    static void BuildRural()
    {
        AddBuilding(new Vector3(-614,0,114),new Vector2(28,28),6,"farm",90,0,true);
        Pave(new Rect(-656,111,42,6));
        Route("Acesso/Fazenda",new Vector3(-656,0,114),new Vector3(-607,0,114));
        Destination("Área rural · fazenda","farm",new Vector3(-615,.08f,114),90);
        for(int i=0;i<6;i++)for(int j=0;j<7;j++)
            Box(CellFor(new Vector3(-614+i*4,0,-145+j*6)),"Canteiro rural",new Vector3(-614+i*4,.12f,-145+j*6),new Vector3(1.6f,.24f,4),materials["crop"],false);
        for(int i=0;i<10;i++)Tree(new Vector3(-680,0,-170+i*34),6);
    }

    static void BuildCoast()
    {
        Box(city,"Oceano cenografico",new Vector3(1000,-.45f,0),new Vector3(656,.05f,1800),materials["water"],false);
        for(int i=0;i<8;i++)Tree(new Vector3(644,0,-500+i*140),6);
        Route("Acesso/Praia",new Vector3(624,0,192),new Vector3(660,0,192));
        Destination("Praia e orla","beach",new Vector3(654,.08f,192),0);
        // An explicit physical swimming boundary in this walking prototype.
        ColliderBox(city,"Limite de agua - prototipo sem natacao",new Vector3(671,1,0),new Vector3(.3f,3,1408));
        Sign(new Vector3(642,2,200),"PRAIA HORIZONTE",90);
    }

    static void Park(Rect area)
    {
        float cx=area.center.x,cz=area.center.y;
        Pave(new Rect(cx-4,area.yMin,8,area.height));Pave(new Rect(area.xMin,cz-4,area.width,8));
        foreach(float x in new[]{area.xMin+16,area.xMax-16})for(float z=area.yMin+16;z<area.yMax;z+=32)Tree(new Vector3(x,0,z),6);
        foreach(float sign in new[]{-1f,1f})
        {
            var p=new Vector3(cx+sign*18,0,cz+14);
            Box(CellFor(p),"Banco parque",p+Vector3.up*.48f,new Vector3(3,.15f,.65f),materials["wood"]);
            Box(CellFor(p),"Pes banco",p+Vector3.up*.22f,new Vector3(2.4f,.45f,.4f),materials["rail"]);
        }
    }

    static void BuildUrbanFurniture()
    {
        foreach(float z in new[]{-384f,0,384})foreach(float x in new[]{-576f,-384,-192,-56,56,192,384,576})
        {
            if(Mathf.Abs(x)==56&&z!=0)continue;
            float offset=z==0?12:9;
            var p=new Vector3(x+offset,0,z+offset);
            Box(CellFor(p),"Poste - base no solo",p+Vector3.up*3.7f,new Vector3(.18f,7.4f,.18f),materials["rail"]);
            Box(CellFor(p),"Luminaria",p+new Vector3(-.6f,7.35f,0),new Vector3(1.3f,.15f,.45f),materials["lamp"],false);
            // No real-time point light per post: emissive fixtures avoid hundreds of lights on mobile.
            var q=new Vector3(x-offset,0,z+offset);
            Box(CellFor(q),"Semaforo - suporte",q+Vector3.up*2.5f,new Vector3(.12f,5,.12f),materials["rail"]);
            Box(CellFor(q),"Semaforo - caixa",q+Vector3.up*4.4f,new Vector3(.38f,1.15f,.3f),materials["rail"],false);
            for(int j=0;j<3;j++)Box(CellFor(q),"Lente sinal",q+new Vector3(0,4.1f+j*.3f,-.17f),new Vector3(.22f,.22f,.03f),materials[j==0?"signalGreen":j==1?"paintYellow":"signalRed"],false);
        }
        for(int i=0;i<6;i++)
        {
            var p=new Vector3(-90,0,-548+i*18);
            Box(CellFor(p),"Conteiner porto",p+Vector3.up*1.3f,new Vector3(12,2.6f,4.8f),materials[i%2==0?"containerRed":"containerBlue"]);
        }
        // Fences, retaining walls, and all street props use ground-based dimensions.
        Sign(new Vector3(92,3,-18),"CIDADE HORIZONTE  |  EXEMPLO",0);
    }

    static void BuildBoundaries()
    {
        foreach(float z in new[]{-699f,699f})
            Box(CellFor(new Vector3(-16,0,z)),"Limite externo da cidade",new Vector3(-16,.8f,z),new Vector3(1366,1.6f,.3f),materials["stone"]);
        Box(CellFor(new Vector3(-699,0,0)),"Limite externo oeste",new Vector3(-699,.8f,0),new Vector3(.3f,1.6f,1398),materials["stone"]);
    }

    static void Tree(Vector3 p,float height)
    {
        var parent=CellFor(p);treeCount++;
        var trunk=GameObject.CreatePrimitive(PrimitiveType.Cylinder);trunk.name="Arvore - tronco";trunk.transform.SetParent(parent,true);
        trunk.transform.position=p+Vector3.up*height*.32f;trunk.transform.localScale=new Vector3(.38f,height*.32f,.38f);trunk.GetComponent<Renderer>().sharedMaterial=materials["wood"];trunk.isStatic=true;
        var leaf=GameObject.CreatePrimitive(PrimitiveType.Sphere);leaf.name="Arvore - copa";leaf.transform.SetParent(parent,true);
        leaf.transform.position=p+Vector3.up*height*.76f;leaf.transform.localScale=new Vector3(height*.7f,height*.63f,height*.7f);leaf.GetComponent<Renderer>().sharedMaterial=materials["leaves"];leaf.isStatic=true;Object.DestroyImmediate(leaf.GetComponent<Collider>());
    }

    static void Sign(Vector3 p,string text,float yaw)
    {
        var parent=CellFor(p);Box(parent,"Placa - poste",new Vector3(p.x,p.y*.5f,p.z),new Vector3(.1f,p.y,.1f),materials["rail"]);
        var sign=new GameObject("Placa: "+text,typeof(TextMesh));sign.transform.SetParent(parent,true);sign.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
        var label=sign.GetComponent<TextMesh>();label.text=text;label.fontSize=44;label.characterSize=.17f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.06f,.13f,.16f);
    }

    static Transform CellFor(Vector3 p)
    {
        var key=new Vector2Int(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.z/Cell));
        if(!cells.TryGetValue(key,out var root)){root=new GameObject("Setor exemplo "+key.x+" "+key.y);root.transform.SetParent(city,false);cells.Add(key,root);}
        return root.transform;
    }
    static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collision=true)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,true);obj.transform.position=position;obj.transform.localScale=size;
        obj.GetComponent<Renderer>().sharedMaterial=material;obj.isStatic=true;if(!collision)Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
    }
    static void ColliderBox(Transform parent,string name,Vector3 p,Vector3 size)
    {
        var obj=new GameObject(name,typeof(BoxCollider));obj.transform.SetParent(parent,true);obj.transform.position=p;obj.GetComponent<BoxCollider>().size=size;obj.isStatic=true;
    }
    static void Pave(Rect rect)=>paved.Add(rect);
    static bool IsBridge(float z)=>Mathf.Abs(z)<13||Mathf.Abs(z-384)<10||Mathf.Abs(z+384)<10;
    static void Route(string name,params Vector3[] points)=>routes.Add(new CityExamplePreview.Route{name=name,points=points.Select(p=>new Vector3(p.x,.08f,p.z)).ToArray()});
    static void Destination(string name,string kind,Vector3 p,float yaw)=>destinations.Add(new CityExamplePreview.Destination{name=name,kind=kind,position=p,yaw=yaw});
    static Bounds RectBounds(Rect r)=>new Bounds(new Vector3(r.center.x,0,r.center.y),new Vector3(r.width,2,r.height));
    static float Length(Vector3[] points){float length=0;for(int i=1;i<points.Length;i++)length+=Vector3.Distance(points[i-1],points[i]);return length;}
    static Vector3 NearestRoad(Vector3 p)
    {
        Vector3 result=Vector3.zero;float best=float.MaxValue;
        foreach(var street in streets){Vector3 v=street.b-street.a;Vector3 q=street.a+v*Mathf.Clamp01(Vector3.Dot(p-street.a,v)/v.sqrMagnitude);float d=(p-q).sqrMagnitude;if(d<best){best=d;result=q;}}return result;
    }

    static void AddQuad(float x0,float z0,float x1,float z1,float y,string material)
    {
        if(x1-x0<.001f||z1-z0<.001f)return;
        int a=Mathf.FloorToInt(x0/Cell),b=Mathf.FloorToInt((x1-.0001f)/Cell),c=Mathf.FloorToInt(z0/Cell),d=Mathf.FloorToInt((z1-.0001f)/Cell);
        for(int x=a;x<=b;x++)for(int z=c;z<=d;z++)
        {
            string key=x+"_"+z+"_"+material;
            if(!surfaces.TryGetValue(key,out var mesh)){mesh=new QuadMesh{parent=CellFor(new Vector3((x+.5f)*Cell,0,(z+.5f)*Cell)),material=materials[material]};surfaces.Add(key,mesh);}
            mesh.Add(Mathf.Max(x0,x*Cell),Mathf.Max(z0,z*Cell),Mathf.Min(x1,(x+1)*Cell),Mathf.Min(z1,(z+1)*Cell),y);
        }
    }
    static void QuadRect(Vector3 p,Vector2 size,float y,string material)=>AddQuad(p.x-size.x*.5f,p.z-size.y*.5f,p.x+size.x*.5f,p.z+size.y*.5f,y,material);
    static void FlushSurfaces()
    {
        foreach(var pair in surfaces)
        {
            var data=pair.Value;var mesh=new Mesh{name="Piso "+pair.Key,indexFormat=IndexFormat.UInt32};mesh.SetVertices(data.vertices);mesh.SetUVs(0,data.uv);mesh.SetTriangles(data.triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,AssetsRoot+"/Meshes/Floor_"+pair.Key+".asset");
            var obj=new GameObject("Pavimento "+pair.Key,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(data.parent,false);obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<MeshRenderer>().sharedMaterial=data.material;obj.isStatic=true;
        }
    }
    static void CombineScenery(Vector2Int key,GameObject cell)
    {
        var sources=cell.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh&&f.GetComponent<MeshRenderer>()&&!f.GetComponentInParent<LODGroup>()&&!f.GetComponent<TextMesh>()).ToArray();
        foreach(var group in sources.GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial))
        {
            if(!group.Key)continue;var list=group.ToArray();
            var mesh=new Mesh{name="Setor "+key+" "+group.Key.name,indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(list.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=cell.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
            string name="Sector_"+key.x+"_"+key.y+"_"+group.Key.name;mesh=SaveMesh(mesh,AssetsRoot+"/Meshes/"+name+".asset");
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(cell.transform,false);obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<MeshRenderer>().sharedMaterial=group.Key;obj.isStatic=true;
            foreach(var source in list){Object.DestroyImmediate(source.GetComponent<MeshRenderer>());Object.DestroyImmediate(source);}
        }
    }
    static Mesh SaveMesh(Mesh mesh,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }

    static void BuildHorizon(CitySectorStreaming streaming)
    {
        var root=new GameObject("Horizonte leve - sem colisoes");root.transform.SetParent(city,false);
        // The city plan remains visible on the horizon/map. Detailed sector floors cover this layer.
        foreach(string material in new[]{"asphalt","concrete","grass","sand"})
        {
            var data=new QuadMesh();
            foreach(var bucket in surfaces.Values.Where(s=>s.material==materials[material]))
            {
                int offset=data.vertices.Count;
                data.vertices.AddRange(bucket.vertices.Select(v=>v-Vector3.up*.09f));data.uv.AddRange(bucket.uv);
                data.triangles.AddRange(bucket.triangles.Select(i=>i+offset));
            }
            if(data.vertices.Count==0)continue;
            var mesh=new Mesh{name="Plano distante "+material,indexFormat=IndexFormat.UInt32};mesh.SetVertices(data.vertices);mesh.SetUVs(0,data.uv);mesh.SetTriangles(data.triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,AssetsRoot+"/Meshes/HorizonGround_"+material+".asset");
            var go=new GameObject("Pavimento distante "+material,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=materials[material];renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);var cube=primitive.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(primitive);
        var proxies=new List<CityExampleHorizon.SectorProxy>();
        MakeMaterial("horizon",new Color(.53f,.57f,.57f),null,.02f);
        foreach(var group in footprints.GroupBy(b=>new Vector2Int(Mathf.FloorToInt(b.center.x/Cell),Mathf.FloorToInt(b.center.z/Cell))))
        {
            var mesh=new Mesh{name="Volumes distantes "+group.Key,indexFormat=IndexFormat.UInt32};
            mesh.CombineMeshes(group.Select(b=>new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(b.center,Quaternion.identity,new Vector3(b.size.x*.97f,b.size.y,b.size.z*.97f))}).ToArray(),true,true);
            mesh=SaveMesh(mesh,AssetsRoot+"/Meshes/HorizonBuildings_"+group.Key.x+"_"+group.Key.y+".asset");
            var obj=new GameObject("Volumes distantes "+group.Key,typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(root.transform,false);obj.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.GetComponent<MeshRenderer>();renderer.sharedMaterial=materials["horizon"];renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            proxies.Add(new CityExampleHorizon.SectorProxy{scene="CitySector_Example_"+group.Key.x+"_"+group.Key.y,renderers=new Renderer[]{renderer}});
        }
        var horizon=root.AddComponent<CityExampleHorizon>();horizon.streaming=streaming;horizon.proxies=proxies.ToArray();
    }

    static void PrepareMaterials()
    {
        MakeMaterial("asphalt",new Color(.48f,.5f,.53f),"T_Concrete_Asphalt_BaseColor",.05f);
        MakeMaterial("concrete",new Color(.76f,.76f,.72f),"T_Concrete_BaseColor",.08f);
        MakeMaterial("grass",new Color(.28f,.4f,.19f),"T_Dirt_BaseColor",.01f);
        MakeMaterial("sand",new Color(.82f,.72f,.5f),"T_Dirt_BaseColor",.01f);
        MakeMaterial("stone",new Color(.36f,.4f,.35f),"T_Concrete_BaseColor",.02f);
        MakeMaterial("rail",new Color(.19f,.22f,.24f),null,.28f);
        MakeMaterial("wood",new Color(.31f,.19f,.105f),null,.06f);
        MakeMaterial("leaves",new Color(.21f,.39f,.14f),null,.02f);
        MakeMaterial("crop",new Color(.36f,.48f,.1f),null,.02f);
        MakeMaterial("water",new Color(.07f,.32f,.42f),null,.65f);
        MakeMaterial("paintYellow",new Color(.95f,.73f,.13f),null,.1f);
        MakeMaterial("paintWhite",new Color(.9f,.9f,.85f),null,.1f);
        MakeMaterial("signalGreen",new Color(.05f,.8f,.3f),null,.3f);
        MakeMaterial("signalRed",new Color(.9f,.06f,.04f),null,.3f);
        MakeMaterial("containerRed",new Color(.6f,.17f,.08f),null,.15f);
        MakeMaterial("containerBlue",new Color(.08f,.28f,.42f),null,.15f);
        MakeMaterial("lamp",new Color(.92f,.83f,.52f),null,.2f);
        materials["lamp"].EnableKeyword("_EMISSION");materials["lamp"].SetColor("_EmissionColor",new Color(.6f,.5f,.24f));EditorUtility.SetDirty(materials["lamp"]);
    }
    static void MakeMaterial(string name,Color tint,string texture,float smoothness)
    {
        string path=AssetsRoot+"/Materials/Example_"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!mat){mat=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(mat,path);}
        mat.color=tint;mat.SetFloat("_Glossiness",smoothness);mat.enableInstancing=true;
        if(texture!=null)mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/DowntownCity/Textures/"+texture+".png");
        EditorUtility.SetDirty(mat);materials[name]=mat;
    }
    static void ConfigureLighting()
    {
        var sun=new GameObject("Sol do exemplo",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-32,0);
        var moon=new GameObject("Lua do exemplo",typeof(Light)).GetComponent<Light>();moon.type=LightType.Directional;moon.transform.rotation=Quaternion.Euler(42,140,0);moon.shadows=LightShadows.None;
        var cycle=city.gameObject.AddComponent<CityDayNight>();cycle.sun=sun;cycle.moon=moon;cycle.startAtNight=false;
        string skyPath=AssetsRoot+"/Materials/ExampleSky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if(!sky){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath);}sky.SetFloat("_SunSize",.035f);sky.SetFloat("_AtmosphereThickness",1);
        string nightPath=AssetsRoot+"/Materials/ExampleNightSky.mat";var night=AssetDatabase.LoadAssetAtPath<Material>(nightPath);
        if(!night){night=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(night,nightPath);}
        night.SetColor("_SkyTint",new Color(.025f,.04f,.085f));night.SetColor("_GroundColor",new Color(.012f,.018f,.03f));night.SetFloat("_Exposure",.18f);night.SetFloat("_AtmosphereThickness",.6f);night.SetFloat("_SunSize",.012f);EditorUtility.SetDirty(night);
        cycle.daySky=sky;cycle.nightSky=night;cycle.SetNight(false);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=350;RenderSettings.fogEndDistance=900;
    }
    static Camera[] ReviewCameras()
    {
        var overview=ReviewCamera("Planta completa",new Vector3(0,1600,0),Vector3.zero,4000);overview.orthographic=true;overview.orthographicSize=740;
        return new[]{overview,ReviewCamera("Cidade inteira",new Vector3(1050,1350,-1250),new Vector3(0,0,0),4000),
            ReviewCamera("Centro e ponte",new Vector3(-115,80,-130),new Vector3(155,3,70),800),
            ReviewCamera("Praca e prefeitura",new Vector3(95,4,88),new Vector3(126,6,142),250),
            ReviewCamera("Bairros e parque",new Vector3(500,145,290),new Vector3(350,3,470),750),
            ReviewCamera("Porto e industria",new Vector3(-65,130,-620),new Vector3(-310,4,-420),800),
            ReviewCamera("Aeroporto regional",new Vector3(-700,95,365),new Vector3(-625,2,550),700),
            ReviewCamera("Interior hospital",new Vector3(264,2,65),new Vector3(264,1.5f,85),120),
            ReviewCamera("Praia e orla",new Vector3(690,36,90),new Vector3(575,2,235),650)};
    }
    static Camera ReviewCamera(string name,Vector3 p,Vector3 focus,float far)
    {
        var cam=new GameObject(name,typeof(Camera)).GetComponent<Camera>();cam.transform.position=p;cam.transform.LookAt(focus);cam.enabled=false;cam.nearClipPlane=.15f;cam.farClipPlane=far;cam.fieldOfView=52;return cam;
    }
    static Dictionary<string,string> ProtectedHashes()
    {
        return new[]{"Assets/Scenes/TestePersonagem.unity","Assets/Scenes/GTAMapaTeste.unity","ProjectSettings/EditorBuildSettings.asset","Assets/Prefabs/Jogador.prefab"}.Where(File.Exists).ToDictionary(p=>p,Hash);
    }
    static string Hash(string path)
    {
        using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
