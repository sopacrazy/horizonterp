using System;
using UnityEngine;

// One-time skin sizing from real weighted geometry, never culling/render bounds.
public static class CharacterSkinSizing
{
    public static Bounds Measure(GameObject visual)
    {
        bool found=false;var result=new Bounds();
        var toParent=visual.transform.parent?visual.transform.parent.worldToLocalMatrix:Matrix4x4.identity;
        foreach(var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var world=CharacterGeometry.GetWorldBounds(renderer);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var point=toParent.MultiplyPoint3x4(world.center+Vector3.Scale(world.extents,new Vector3(x,y,z)));
                if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
            }
        }
        if(!found||!float.IsFinite(result.size.y)||result.size.y<.1f)throw new InvalidOperationException("Geometria da skin inválida para normalizar altura.");
        return result;
    }
    public static void Match(GameObject visual,float height,float ground)
    {
        var bounds=Measure(visual);
        visual.transform.localScale*=height/bounds.size.y;
        bounds=Measure(visual);
        visual.transform.localPosition+=Vector3.up*(ground-bounds.min.y);
    }
}
