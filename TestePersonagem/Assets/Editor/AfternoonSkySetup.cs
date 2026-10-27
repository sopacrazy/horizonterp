using System;
using UnityEditor;
using UnityEngine;

public static class AfternoonSkySetup
{
    const string TexturePath="Assets/Resources/Environment/LateAfternoonSky.png";
    const string MaterialPath="Assets/Resources/Environment/LateAfternoonSkyMaterial.mat";

    public static void Apply()
    {
        AssetDatabase.ImportAsset(TexturePath,ImportAssetOptions.ForceUpdate);
        var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if(!importer)throw new InvalidOperationException("Textura do céu da tarde não encontrada.");
        importer.textureType=TextureImporterType.Default;
        importer.textureShape=TextureImporterShape.Texture2D;
        importer.sRGBTexture=true;
        importer.wrapMode=TextureWrapMode.Repeat;
        importer.filterMode=FilterMode.Trilinear;
        importer.mipmapEnabled=true;
        importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();

        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var shader=Shader.Find("Skybox/Panoramic");
        if(!texture||!shader)throw new InvalidOperationException("Não foi possível preparar o céu panorâmico.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!material)
        {
            material=new Material(shader){name="Horizonte · Céu de fim de tarde"};
            AssetDatabase.CreateAsset(material,MaterialPath);
        }
        material.shader=shader;
        material.SetTexture("_MainTex",texture);
        material.SetColor("_Tint",new Color(.92f,.96f,1f,1));
        material.SetFloat("_Exposure",.68f);
        material.SetFloat("_Rotation",8f);
        if(material.HasProperty("_Mapping"))material.SetFloat("_Mapping",1);
        if(material.HasProperty("_ImageType"))material.SetFloat("_ImageType",0);
        material.shaderKeywords=Array.Empty<string>();
        material.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        material.DisableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log("AFTERNOON_SKY_READY");
    }
}
