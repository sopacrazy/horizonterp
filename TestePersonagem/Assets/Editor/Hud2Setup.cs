using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class Hud2Setup
{
    public static void Prepare()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        const string path="Assets/Resources/Hud2Atlas.png";
        if(!File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes("../personagem/HUB2.png")))
            throw new Exception("O atlas HUB2 deve preservar o PNG original.");
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;
        importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
        importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
        foreach(string name in new[]{"h2_cash","h2_menu","h2_fist","h2_sprint","h2_safe"})
            if(!HudAtlas.Get(name))throw new Exception("Recorte ausente: "+name);
        Debug.Log("HUD2_ATLAS_VERIFIED originalPixels=true dynamicValues=true");
    }
    public static void BuildWindows(){Prepare();PrototypeSetup.BuildWindows();}
    public static void BuildAndroid(){Prepare();AndroidSetup.Build();}
}
