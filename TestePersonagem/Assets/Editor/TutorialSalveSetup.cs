using System;
using UnityEditor;
using UnityEngine;

public static class TutorialSalveSetup
{
    public static void Prepare()
    {
        PreparePair("Assets/Resources/TutorialSalve.png", "Assets/Resources/TutorialSalve.mp3", true);
        PreparePair("Assets/Resources/TutorialCoinBanner.png", "Assets/Resources/TutorialCoin.mp3", true);
        Debug.Log("TUTORIAL_SALVE_ASSETS_READY");
    }

    static void PreparePair(string imagePath, string audioPath, bool hasTransparency)
    {
        var image = AssetImporter.GetAtPath(imagePath) as TextureImporter;
        var audio = AssetImporter.GetAtPath(audioPath) as AudioImporter;
        if (!image || !audio) throw new Exception("Arte ou áudio ausente do tutorial: " + imagePath);

        image.textureType = TextureImporterType.Default;
        image.alphaIsTransparency = hasTransparency;
        image.mipmapEnabled = false;
        image.npotScale = TextureImporterNPOTScale.None;
        image.maxTextureSize = 2048;
        image.textureCompression = TextureImporterCompression.Uncompressed;
        image.filterMode = FilterMode.Bilinear;
        image.SaveAndReimport();

        var settings = audio.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.CompressedInMemory;
        settings.preloadAudioData = true;
        audio.defaultSampleSettings = settings;
        audio.SaveAndReimport();
    }

    public static void BuildWindows()
    {
        Prepare();
        PrototypeSetup.BuildWindows();
    }
}
