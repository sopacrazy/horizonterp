using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public static class BrandingSetup
{
    public const string ProductName="Horizonte";
    public const string AndroidIdentifier="com.adriano.testepersonagem";
    public const string IconAssetPath="Assets/Branding/AppIcon.png";
    public const string AdaptiveIconAssetPath="Assets/Branding/AppIconAdaptive.png";
    static string ProjectRoot=>Directory.GetParent(Application.dataPath).FullName;
    static string SourcePath=>Path.GetFullPath(Path.Combine(ProjectRoot,"../personagem/logo.png"));

    [MenuItem("Horizonte/Aplicar nome e icones")]
    public static void Prepare()
    {
        if(!File.Exists(SourcePath))throw new FileNotFoundException("Logo do Horizonte nao encontrado.",SourcePath);
        string destination=Path.Combine(ProjectRoot,IconAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(SourcePath,destination,true);
        AssetDatabase.ImportAsset(IconAssetPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var importer=AssetImporter.GetAtPath(IconAssetPath) as TextureImporter;
        if(!importer)throw new InvalidOperationException("Nao foi possivel importar o icone do Horizonte.");
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=true;
        importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=true;
        importer.mipmapEnabled=false;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.wrapMode=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Bilinear;
        importer.isReadable=false;
        importer.SaveAndReimport();
        var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
        if(!icon||icon.width!=icon.height)throw new InvalidOperationException("O icone do Horizonte deve ser uma imagem quadrada.");
        var adaptiveIcon=PrepareAdaptiveIcon();

        PlayerSettings.productName=ProductName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,AndroidIdentifier);
        SetApplicationIcons(NamedBuildTarget.Unknown,icon);
        // Unity 6.6 supports adaptive Android icons; launchers apply their own round/square mask.
#if UNITY_ANDROID
        SetAndroidIcons(AndroidPlatformIconKind.Adaptive,adaptiveIcon);
#endif
        AssetDatabase.SaveAssets();
        VerifyConfiguration();
        Debug.Log("HORIZONTE_BRANDING_READY");
    }

    static Texture2D PrepareAdaptiveIcon()
    {
        // Android masks the central viewport. Inset the complete original artwork without redrawing it.
        const int size=1024,content=676;
        var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
        var inset=new Texture2D(size,size,TextureFormat.RGBA32,false);
        try
        {
            if(!source.LoadImage(File.ReadAllBytes(SourcePath)))throw new InvalidOperationException("Logo PNG invalido.");
            var pixels=Enumerable.Repeat(new Color32(0,0,0,255),size*size).ToArray();
            int offset=(size-content)/2;
            for(int y=0;y<content;y++)
                for(int x=0;x<content;x++)
                    pixels[(y+offset)*size+x+offset]=source.GetPixelBilinear((x+.5f)/content,(y+.5f)/content);
            inset.SetPixels32(pixels);inset.Apply(false,false);
            File.WriteAllBytes(Path.Combine(ProjectRoot,AdaptiveIconAssetPath),inset.EncodeToPNG());
        }
        finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(inset);}
        AssetDatabase.ImportAsset(AdaptiveIconAssetPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var importer=(TextureImporter)AssetImporter.GetAtPath(AdaptiveIconAssetPath);
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=false;
        importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=size;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=false;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.isReadable=false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveIconAssetPath);
    }

    static void SetApplicationIcons(NamedBuildTarget target,Texture2D icon)
    {
        int count=Math.Max(1,PlayerSettings.GetIconSizes(target,IconKind.Application).Length);
        PlayerSettings.SetIcons(target,Enumerable.Repeat(icon,count).ToArray(),IconKind.Application);
    }

    static void SetAndroidIcons(PlatformIconKind kind,Texture2D icon)
    {
        var slots=PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android,kind);
        if(slots.Length==0)throw new InvalidOperationException("Slots de icone Android indisponiveis: "+kind);
        foreach(var slot in slots)
        {
            // Preserve the supplied artwork in all required layers; Unity generates the target densities.
            var layers=new Texture2D[slot.maxLayerCount];
            for(int layer=0;layer<slot.minLayerCount;layer++)layers[layer]=icon;
            slot.SetTextures(layers);
        }
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android,kind,slots);
    }

    [MenuItem("Horizonte/Verificar nome e icones")]
    public static void VerifyConfiguration()
    {
        var report=new List<string>();
        void Require(bool valid,string description)
        {
            report.Add((valid?"OK: ":"FALHA: ")+description);
            if(!valid)throw new InvalidOperationException("Configuracao Horizonte: "+description);
        }
        Require(PlayerSettings.productName==ProductName,"Nome do aplicativo = "+ProductName);
        Require(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)==AndroidIdentifier,"Identificador Android preservado = "+AndroidIdentifier);
        var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
        Require(icon&&icon.width==icon.height,"Logo quadrado importado = "+IconAssetPath);
        Require(File.ReadAllBytes(SourcePath).SequenceEqual(File.ReadAllBytes(Path.Combine(ProjectRoot,IconAssetPath))),"Arquivo original de logo preservado sem alterar a arte");
        var defaults=PlayerSettings.GetIcons(NamedBuildTarget.Unknown,IconKind.Application);
        Require(defaults.Length>0&&defaults.All(texture=>texture==icon),"Icones padrao usam o logo ("+defaults.Length+" slots)");
        var adaptiveIcon=AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveIconAssetPath);
        Require(adaptiveIcon&&adaptiveIcon.width==1024&&adaptiveIcon.height==1024,"Adaptive preserva a arte original centralizada em 66% da tela para a area segura");
#if UNITY_ANDROID
        VerifyAndroidIcons(AndroidPlatformIconKind.Adaptive,"Adaptive",adaptiveIcon,Require,report);
#endif
        report.Add("Resultado: HORIZONTE_BRANDING_VERIFIED");
        string reportPath=Path.GetFullPath(Path.Combine(ProjectRoot,"../tools/branding-validation.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllLines(reportPath,report);
        Debug.Log("HORIZONTE_BRANDING_VERIFIED "+reportPath);
    }

    static void VerifyAndroidIcons(PlatformIconKind kind,string label,Texture2D icon,Action<bool,string> require,List<string> report)
    {
        var slots=PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android,kind);
        require(slots.Length>0,"Android "+label+" possui slots configurados");
        foreach(var slot in slots)
        {
            for(int layer=0;layer<slot.minLayerCount;layer++)
                require(slot.GetTexture(layer)==icon,"Android "+label+" "+slot.width+"x"+slot.height+", camada "+layer+" usa o logo");
        }
        report.Add("Android "+label+": "+slots.Length+" slots");
    }
}
