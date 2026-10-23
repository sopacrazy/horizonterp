using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.IO;

public static class HudSetup
{
    [MenuItem("Teste/Preparar HUD")]
    public static void Prepare()
    {
        AssetDatabase.Refresh();
        var atlas=(TextureImporter)AssetImporter.GetAtPath("Assets/Resources/HudAtlas.png");
        atlas.textureType=TextureImporterType.Default;atlas.alphaIsTransparency=true;
        atlas.npotScale=TextureImporterNPOTScale.None;atlas.maxTextureSize=2048;
        atlas.mipmapEnabled=false;atlas.textureCompression=TextureImporterCompression.Uncompressed;
        atlas.filterMode=FilterMode.Bilinear;atlas.SaveAndReimport();
        string path="Assets/Resources/HudPanel.asset";
        var panel=AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if(!panel){panel=ScriptableObject.CreateInstance<PanelSettings>();AssetDatabase.CreateAsset(panel,path);}
        panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution=new Vector2Int(1280,720);
        panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match=.5f;
        panel.themeStyleSheet=AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Resources/HudTheme.tss");
        if(!panel.themeStyleSheet)throw new System.Exception("Tema do HUD não importado.");
        EditorUtility.SetDirty(panel);AssetDatabase.SaveAssets();
        Debug.Log("HUD_SETUP_OK");
    }

    public static void PrepareAndBuild()
    {
        Prepare();
        PrototypeSetup.BuildWindows();
        File.WriteAllText("../tools/hud-build-result.txt","HUD_BUILD_OK");
    }

    public static void BuildAndVerifyMovement()
    {
        PrepareAndBuild();
        PrototypePlayVerification.Start();
    }
}
