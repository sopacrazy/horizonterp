using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameUpdateSetup
{
    public const string GameScene="Assets/Scenes/TestePersonagem.unity";
    public const string BootScene="Assets/Scenes/Entrada.unity";
    public static string[] Scenes => new[]{BootScene,GameScene};
    public static void Prepare()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BrandingSetup.Prepare();
        EditorSceneManager.OpenScene(GameScene);
        var foundation=GameObject.Find("Base solida 48m");
        if(!foundation || !foundation.GetComponent<Collider>())throw new Exception("Base de colisão ausente");
        foundation.GetComponent<Renderer>().enabled=false;
        Camera.main.nearClipPlane=.15f;
        var player=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
        player.runSpeed=4.5f;
        var controller=(AnimatorController)player.animator.runtimeAnimatorController;
        var tree=(BlendTree)controller.layers[0].stateMachine.defaultState.motion;
        var children=tree.children;
        var runClip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/FastRunRetarget.anim");
        int run=Array.FindIndex(children,c=>c.motion==runClip && runClip);
        if(run<0)throw new Exception("Animação de corrida ausente");
        children[run].timeScale*=4.5f/children[run].threshold;
        children[run].threshold=4.5f;
        tree.children=children;EditorUtility.SetDirty(tree);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        PrefabUtility.SaveAsPrefabAsset(player.gameObject,"Assets/Prefabs/Jogador.prefab");

        const string loginArtworkPath="Assets/Startup/IntroLogin.png";
        string loginSource=Path.GetFullPath("../personagem/amigos.png");
        if(!File.Exists(loginSource))throw new FileNotFoundException("Arte de autenticação ausente",loginSource);
        Directory.CreateDirectory("Assets/Startup");
        File.Copy(loginSource,loginArtworkPath,true);
        AssetDatabase.ImportAsset(loginArtworkPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        PrepareArtwork("Assets/Startup/HorizonteBrasil.png");
        PrepareArtwork(loginArtworkPath);
        Directory.CreateDirectory("Assets/Resources/Characters");
        foreach(var entry in new[]{("feminina.png","Feminina.png"),("masculino.png","Masculino.png")})
        {
            string source=Path.GetFullPath("../personagem/"+entry.Item1);
            string target="Assets/Resources/Characters/"+entry.Item2;
            if(!File.Exists(source))throw new FileNotFoundException("Arte do personagem ausente",source);
            File.Copy(source,target,true);
            AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            PrepareArtwork(target);
        }
        var audio=(AudioImporter)AssetImporter.GetAtPath("Assets/Startup/intro.mp3");
        var settings=audio.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;
        settings.preloadAudioData=true;audio.defaultSampleSettings=settings;audio.SaveAndReimport();
        var boot=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var camera=new GameObject("Camera da entrada").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=0;
        camera.gameObject.AddComponent<AudioListener>();
        var loader=new GameObject("Entrada Horizonte Brasil").AddComponent<StartupLoader>();
        loader.artwork=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Startup/HorizonteBrasil.png");
        loader.loginArtwork=AssetDatabase.LoadAssetAtPath<Texture2D>(loginArtworkPath);
        loader.intro=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Startup/intro.mp3");
        if(!loader.artwork || !loader.loginArtwork || !loader.intro || loader.intro.length<10)throw new Exception("Arte ou música inválida");
        loader.minimumSeconds=10;
        EditorSceneManager.SaveScene(boot,BootScene);
        EditorBuildSettings.scenes=Scenes.Select(s=>new EditorBuildSettingsScene(s,true)).ToArray();
        PlayerSettings.bundleVersion="0.3.0";
        PlayerSettings.Android.bundleVersionCode=5;
        AssetDatabase.SaveAssets();
        File.WriteAllText("../tools/game-update-setup.txt","Base visual desativada; colisão preservada.\nCorrida 4.5 m/s; animação sincronizada.\nEntrada 10s mínimo com arte original em tela inteira; cena assíncrona.\nLogin com amigos.png em IntroLogin.png e música contínua até entrar no jogo.\nNome Horizonte e ícone logo.png preparados para Android; APK somente sob solicitação.\n");
        Debug.Log("GAME_UPDATE_SETUP_OK");
    }

    static void PrepareArtwork(string path)
    {
        var texture=AssetImporter.GetAtPath(path) as TextureImporter;
        if(!texture)throw new Exception("Arte de entrada não importada: "+path);
        texture.textureType=TextureImporterType.Default;texture.npotScale=TextureImporterNPOTScale.None;
        texture.GetSourceTextureWidthAndHeight(out int width,out int height);
        texture.maxTextureSize=Mathf.Max(2048,Mathf.NextPowerOfTwo(Mathf.Max(width,height)));
        texture.mipmapEnabled=false;texture.wrapMode=TextureWrapMode.Clamp;
        texture.textureCompression=TextureImporterCompression.Uncompressed;texture.SaveAndReimport();
    }

    public static void PrepareAndBuildWindows(){Prepare();HudSetup.Prepare();PrototypeSetup.BuildWindows();}

    public static void PrepareAndBuildAndroid(){Prepare();AndroidSetup.Build();}
    public static void InspectGround()
    {
        EditorSceneManager.OpenScene(GameScene);
        var lines=new List<string>();
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.transform.GetComponentInParent<CityDayNight>())continue;
            var mesh=filter.sharedMesh;if(!mesh || !filter.GetComponent<Renderer>())continue;
            var world=mesh.vertices.Select(v=>filter.transform.TransformPoint(v)).ToArray();
            if(!world.Any(v=>Mathf.Abs(v.y)<.25f))continue;
            var bounds=filter.GetComponent<Renderer>().bounds;
            if(bounds.size.y>1)continue;
            lines.Add(filter.transform.parent?.name+"/"+filter.name+" bounds="+bounds.ToString("F5"));
            var levels=world.Select(v=>Mathf.Round(v.y*100000)/100000).Distinct().OrderBy(v=>v);
            lines.Add("Y: "+string.Join(",",levels));
        }
        File.WriteAllLines("../tools/ground-inspection.txt",lines);
        Debug.Log("GROUND_INSPECTION_OK");
    }
}
