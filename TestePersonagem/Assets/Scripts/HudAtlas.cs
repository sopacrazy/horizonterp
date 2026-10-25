using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Non-destructive sprite slicing: coordinates refer to the original PNG, with Y measured from its top.
public static class HudAtlas
{
    [Serializable] public sealed class Region { public string name;public int x,y,width,height; }
    [Serializable] public sealed class Layout { public Region[] regions; }
    static Texture2D texture;
    static Dictionary<string,Sprite> sprites;
    public static int RegionCount { get { Ensure();return sprites.Count; } }
    static void Ensure()
    {
        if(texture&&sprites!=null)return;
        texture=Resources.Load<Texture2D>("HudAtlas");
        sprites=new Dictionary<string,Sprite>();
        AddRegions(texture,Resources.Load<TextAsset>("HudAtlasLayout"));
        var clean=Resources.Load<Texture2D>("Hud2Atlas");
        if(clean)AddRegions(clean,Resources.Load<TextAsset>("Hud2AtlasLayout"));
    }
    static void AddRegions(Texture2D source,TextAsset description)
    {
        var layout=JsonUtility.FromJson<Layout>(description.text);
        foreach(var r in layout.regions)
        {
            if(r.x<0||r.y<0||r.x+r.width>source.width||r.y+r.height>source.height)
                throw new InvalidOperationException("Recorte do HUD fora da imagem: "+r.name);
            var sprite=Sprite.Create(source,new Rect(r.x,source.height-r.y-r.height,r.width,r.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
            sprite.name="HUD_"+r.name;sprites.Add(r.name,sprite);
        }
    }
    public static Sprite Get(string name){Ensure();return sprites[name];}
    public static Image Image(string name,string css="atlas-image")
    {
        var image=new Image{sprite=Get(name),scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};
        image.AddToClassList(css);return image;
    }
}
