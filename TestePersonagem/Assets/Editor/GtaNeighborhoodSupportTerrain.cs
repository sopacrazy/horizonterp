using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Complements holes beneath the ready-made neighbourhood without replacing its original floors.
public static class GtaNeighborhoodSupportTerrain
{
    const float GridStep = 4f;
    const float FloorInset = .10f;
    const float OuterExtent = 1000f;

    public static GameObject Create(Transform parent, Rect area, Vector3[] sourceWalkFloors,
        Func<Vector3,float?> originalHeight, Material material, string folder)
    {
        if (!Finite(area.xMin) || !Finite(area.xMax) || !Finite(area.yMin) || !Finite(area.yMax) ||
            area.width <= 0 || area.height <= 0)
            throw new ArgumentException("Area de apoio invalida.", nameof(area));
        if (area.xMin <= -OuterExtent || area.xMax >= OuterExtent || area.yMin <= -OuterExtent || area.yMax >= OuterExtent)
            throw new ArgumentException("A area interna deve ficar dentro do anel visual de +/-1000 metros.", nameof(area));
        if (!material) throw new ArgumentNullException(nameof(material));
        folder=(folder??"").Replace('\\','/').TrimEnd('/');
        if (!folder.StartsWith("Assets/",StringComparison.Ordinal) || folder.Contains(".."))
            throw new ArgumentException("Use uma pasta de assets do projeto.",nameof(folder));

        var samples=new List<Vector3>();
        if(sourceWalkFloors!=null)
            foreach(var sample in sourceWalkFloors)
                if(Finite(sample.x)&&Finite(sample.y)&&Finite(sample.z)) samples.Add(sample);
        if(samples.Count==0) throw new ArgumentException("Pisos originais validos sao necessarios para preencher as lacunas.",nameof(sourceWalkFloors));

        int xSegments=Mathf.Max(1,Mathf.CeilToInt(area.width/GridStep));
        int zSegments=Mathf.Max(1,Mathf.CeilToInt(area.height/GridStep));
        int stride=xSegments+1;
        var vertices=new Vector3[stride*(zSegments+1)];
        var uv=new Vector2[vertices.Length];
        var triangles=new int[xSegments*zSegments*6];
        var nearestDistance=new float[4];
        var nearestHeight=new float[4];
        for(int z=0;z<=zSegments;z++) for(int x=0;x<=xSegments;x++)
        {
            float worldX=x==xSegments?area.xMax:area.xMin+area.width*x/xSegments;
            float worldZ=z==zSegments?area.yMax:area.yMin+area.height*z/zSegments;
            var query=new Vector3(worldX,0,worldZ);
            float? known=originalHeight?.Invoke(query);
            float height=known.HasValue&&Finite(known.Value)?known.Value:
                Interpolate(query,samples,nearestDistance,nearestHeight);
            // A coarse cell may straddle a raised sidewalk and a lower road. Keep its
            // support below both, otherwise the grass can poke through the asphalt.
            if(originalHeight!=null)
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0&&dz==0)continue;
                    var nearby=originalHeight(query+new Vector3(dx*GridStep,0,dz*GridStep));
                    if(nearby.HasValue&&Finite(nearby.Value))height=Mathf.Min(height,nearby.Value);
                }
            int index=z*stride+x;
            vertices[index]=new Vector3(worldX,height-FloorInset,worldZ);
            uv[index]=new Vector2(worldX/5f,worldZ/5f);
        }
        int write=0;
        for(int z=0;z<zSegments;z++) for(int x=0;x<xSegments;x++)
        {
            int a=z*stride+x,b=a+1,c=a+stride,d=c+1;
            triangles[write++]=a;triangles[write++]=c;triangles[write++]=b;
            triangles[write++]=b;triangles[write++]=c;triangles[write++]=d;
        }
        var ground=Mesh("SupportTerrain",vertices,uv,triangles);
        var ring=Ring(vertices,xSegments,zSegments);
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string path=folder+"/SupportTerrain.asset";
        ground=SaveMain(ground,path);
        ring=SaveRing(ring,path);
        AssetDatabase.SaveAssets();

        var root=new GameObject("Terreno de apoio - pisos originais preservados");
        // Preserve world identity when parenting: vertices and original samples share world coordinates.
        root.transform.SetParent(parent,true);root.isStatic=true;
        var inner=new GameObject("ground_apoio_bairro_teste",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
        inner.transform.SetParent(root.transform,false);inner.isStatic=true;
        inner.GetComponent<MeshFilter>().sharedMesh=ground;
        inner.GetComponent<MeshCollider>().sharedMesh=ground;
        Configure(inner.GetComponent<MeshRenderer>(),material);
        var outer=new GameObject("Terreno distante",typeof(MeshFilter),typeof(MeshRenderer));
        outer.transform.SetParent(root.transform,false);outer.isStatic=true;
        outer.GetComponent<MeshFilter>().sharedMesh=ring;
        Configure(outer.GetComponent<MeshRenderer>(),material);
        return root;
    }

    static float Interpolate(Vector3 query,List<Vector3> samples,float[] distance,float[] height)
    {
        for(int i=0;i<4;i++){distance[i]=float.PositiveInfinity;height[i]=0;}
        foreach(var sample in samples)
        {
            float dx=sample.x-query.x,dz=sample.z-query.z;
            float squared=dx*dx+dz*dz;
            if(squared<.000001f)return sample.y;
            if(squared>=distance[3])continue;
            int slot=3;
            while(slot>0&&squared<distance[slot-1])
            {
                distance[slot]=distance[slot-1];height[slot]=height[slot-1];slot--;
            }
            distance[slot]=squared;height[slot]=sample.y;
        }
        double sum=0,weights=0;
        for(int i=0;i<4;i++)
        {
            if(float.IsPositiveInfinity(distance[i]))continue;
            double weight=1.0/Math.Max(.01,Math.Sqrt(distance[i]));
            sum+=height[i]*weight;weights+=weight;
        }
        return (float)(sum/weights);
    }

    static UnityEngine.Mesh Ring(Vector3[] ground,int xSegments,int zSegments)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        int stride=xSegments+1;
        for(int side=0;side<4;side++)
        {
            int segments=side%2==0?xSegments:zSegments;
            int start=vertices.Count;
            for(int i=0;i<=segments;i++)
            {
                float t=(float)i/segments;Vector3 inner,outer;
                switch(side)
                {
                    case 0:
                        inner=ground[zSegments*stride+i];
                        outer=new Vector3(Mathf.Lerp(-OuterExtent,OuterExtent,t),inner.y,OuterExtent);break;
                    case 1:
                        inner=ground[(zSegments-i)*stride+xSegments];
                        outer=new Vector3(OuterExtent,inner.y,Mathf.Lerp(OuterExtent,-OuterExtent,t));break;
                    case 2:
                        inner=ground[xSegments-i];
                        outer=new Vector3(Mathf.Lerp(OuterExtent,-OuterExtent,t),inner.y,-OuterExtent);break;
                    default:
                        inner=ground[i*stride];
                        outer=new Vector3(-OuterExtent,inner.y,Mathf.Lerp(-OuterExtent,OuterExtent,t));break;
                }
                vertices.Add(inner);vertices.Add(outer);
                uv.Add(new Vector2(inner.x/5f,inner.z/5f));uv.Add(new Vector2(outer.x/5f,outer.z/5f));
                if(i==segments)continue;
                int a=start+i*2,b=a+1,c=a+2,d=a+3;
                triangles.Add(a);triangles.Add(b);triangles.Add(c);
                triangles.Add(c);triangles.Add(b);triangles.Add(d);
            }
        }
        return Mesh("SupportTerrainRing",vertices.ToArray(),uv.ToArray(),triangles.ToArray());
    }

    static UnityEngine.Mesh Mesh(string name,Vector3[] vertices,Vector2[] uv,int[] triangles)
    {
        var mesh=new UnityEngine.Mesh{name=name,indexFormat=vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
        mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    static UnityEngine.Mesh SaveMain(UnityEngine.Mesh mesh,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);
        if(!existing){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
    }

    static UnityEngine.Mesh SaveRing(UnityEngine.Mesh mesh,string path)
    {
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if(asset is UnityEngine.Mesh existing&&existing.name=="SupportTerrainRing")
            {
                EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
            }
        AssetDatabase.AddObjectToAsset(mesh,path);return mesh;
    }

    static void Configure(MeshRenderer renderer,Material material)
    {
        renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
    }

    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
}
