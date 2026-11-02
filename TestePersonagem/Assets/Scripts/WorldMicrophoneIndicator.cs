using UnityEngine;

// The same Tabler microphone used by the HUD, aligned with the player's name and ID.
public sealed class WorldMicrophoneIndicator : MonoBehaviour
{
    Material material;
    Texture2D diskTexture;
    Sprite diskSprite,microphoneSprite;
    SpriteRenderer disk,microphone;
    public static WorldMicrophoneIndicator Create(TextMesh name)
    {
        var root = new GameObject("Microfone do jogador"); root.layer = 2;
        root.transform.SetParent(name.transform, false);
        var icon = root.AddComponent<WorldMicrophoneIndicator>();
        icon.material = new Material(Shader.Find("Sprites/Default"));
        const int size=64;
        icon.diskTexture=new Texture2D(size,size,TextureFormat.RGBA32,false);
        icon.diskTexture.filterMode=FilterMode.Bilinear;
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float radius=Vector2.Distance(new Vector2(x,y),new Vector2(31.5f,31.5f));
            float alpha=Mathf.Clamp01(31.5f-radius);
            icon.diskTexture.SetPixel(x,y,new Color(1,1,1,alpha));
        }
        icon.diskTexture.Apply();
        icon.diskSprite=Sprite.Create(icon.diskTexture,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);
        var background=new GameObject("Fundo redondo do microfone");background.layer=2;
        background.transform.SetParent(root.transform,false);
        icon.disk=background.AddComponent<SpriteRenderer>();icon.disk.sprite=icon.diskSprite;
        icon.disk.sharedMaterial=icon.material;icon.disk.sortingOrder=2;
        var texture=HudPack.Texture("microphone");
        if(texture)
        {
            icon.microphoneSprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),texture.width,0,SpriteMeshType.FullRect);
            var symbol=new GameObject("Microfone Tabler");symbol.layer=2;symbol.transform.SetParent(root.transform,false);
            symbol.transform.localPosition=new Vector3(0,0,-.002f);
            symbol.transform.localScale=Vector3.one*.65f;
            icon.microphone=symbol.AddComponent<SpriteRenderer>();icon.microphone.sprite=icon.microphoneSprite;
            icon.microphone.sharedMaterial=icon.material;icon.microphone.sortingOrder=3;
        }
        root.SetActive(false); return icon;
    }
    public void Refresh(bool active, TextMesh name)
    {
        gameObject.SetActive(active);
        if (!active) return;
        var bounds=name.GetComponent<MeshRenderer>().localBounds;
        float diameter=Mathf.Clamp(bounds.size.y*.85f,.075f,.14f);
        transform.localScale=Vector3.one*diameter;
        transform.localPosition = new Vector3(bounds.max.x+diameter*.65f,bounds.center.y,-.014f);
        float pulse = .5f + .5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5));
        disk.color=Color.Lerp(new Color(.035f,.48f,.27f,.95f),new Color(.1f,.8f,.43f,1f),pulse);
        if(microphone)microphone.color=new Color(1f,1f,1f,.85f+.15f*pulse);
    }
    void OnDestroy() { if (material) Destroy(material); if(diskSprite)Destroy(diskSprite); if(microphoneSprite)Destroy(microphoneSprite); if(diskTexture)Destroy(diskTexture); }
}
