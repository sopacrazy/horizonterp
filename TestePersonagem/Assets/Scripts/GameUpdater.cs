using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class AndroidRelease
{
    public bool available;
    public string version,packageId,apkUrl,sha256;
    public int versionCode;
    public long bytes;
}

public sealed class GameUpdater : MonoBehaviour
{
    AndroidRelease release;
    bool prompt,continuePlaying,waitingPermission;
    string error;
    public static bool IsNewer(AndroidRelease value,int installedCode)=>value!=null&&value.available&&value.versionCode>installedCode;
    public static bool Valid(AndroidRelease value)
    {
        if(value==null||value.packageId!=GameVersion.Current.packageId||value.versionCode<=0||value.bytes<=0||value.bytes>300L*1024*1024||
            !Regex.IsMatch(value.version??"",@"^\d+\.\d+\.\d+$")||!Regex.IsMatch(value.sha256??"",@"^[a-fA-F0-9]{64}$"))return false;
        return value.apkUrl=="https://github.com/sopacrazy/horizonterp/releases/download/v"+value.version+"/HorizonteBrasil-"+value.version+".apk";
    }
    public static string FileHash(string path)
    {
        using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    public IEnumerator Check(Action<string,float> progress)
    {
        if(Application.platform!=RuntimePlatform.Android||Application.isEditor)yield break;
        foreach(string argument in Environment.GetCommandLineArgs())if(argument.StartsWith("--verify-"))yield break;
        progress("VERIFICANDO ATUALIZAÇÕES · v"+Application.version,0);
        using(var request=UnityWebRequest.Get(GameVersion.Current.updateManifestUrl+"?installed="+GameVersion.Current.versionCode))
        {
            request.timeout=8;request.redirectLimit=3;
            yield return request.SendWebRequest();
            if(request.result!=UnityWebRequest.Result.Success){progress("VERIFICAÇÃO INDISPONÍVEL · CONTINUANDO",0);yield return new WaitForSecondsRealtime(1);yield break;}
            try{release=JsonUtility.FromJson<AndroidRelease>(request.downloadHandler.text);}catch{release=null;}
        }
        if(!IsNewer(release,GameVersion.Current.versionCode))yield break;
        if(!Valid(release)){progress("ATUALIZAÇÃO INVÁLIDA · CONTINUANDO",0);yield return new WaitForSecondsRealtime(1);yield break;}
        string path=null;
        try
        {
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using(var bridge=new AndroidJavaClass("com.horizonte.update.HbrpApkProvider"))path=bridge.CallStatic<string>("downloadPath",activity);
        }
        catch{progress("ATUALIZAÇÃO INDISPONÍVEL NESTE APARELHO",0);}
        if(path==null){yield return new WaitForSecondsRealtime(2);yield break;}
        string partial=path+".part";
        if(File.Exists(partial))File.Delete(partial);
        using(var request=UnityWebRequest.Get(release.apkUrl))
        {
            request.timeout=180;request.redirectLimit=5;
            request.downloadHandler=new DownloadHandlerFile(partial){removeFileOnAbort=true};
            var download=request.SendWebRequest();
            while(!download.isDone)
            {
                progress("BAIXANDO v"+release.version+" · "+Mathf.FloorToInt(Mathf.Clamp01((float)request.downloadedBytes/release.bytes)*100)+"%",Mathf.Clamp01((float)request.downloadedBytes/release.bytes)*.8f);
                if(request.downloadedBytes>(ulong)release.bytes){request.Abort();break;}
                yield return null;
            }
            if(request.result!=UnityWebRequest.Result.Success||!File.Exists(partial)||new FileInfo(partial).Length!=release.bytes)
            {
                if(File.Exists(partial))File.Delete(partial);
                progress("DOWNLOAD INTERROMPIDO · TENTE AO REABRIR O JOGO",0);yield return new WaitForSecondsRealtime(2);yield break;
            }
        }
        progress("CONFERINDO ATUALIZAÇÃO",.85f);
        var hashing=Task.Run(()=>FileHash(partial));while(!hashing.IsCompleted)yield return null;
        if(hashing.IsFaulted||!string.Equals(hashing.Result,release.sha256,StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);progress("ARQUIVO INVÁLIDO · ATUALIZAÇÃO CANCELADA",0);yield return new WaitForSecondsRealtime(2);yield break;
        }
        if(File.Exists(path))File.Delete(path);File.Move(partial,path);
        progress("v"+release.version+" PRONTA · CONFIRME NO ANDROID",1);
        prompt=true;OpenInstaller();
        while(!continuePlaying)yield return null;
        prompt=false;
    }
    void OpenInstaller()
    {
        error=null;
        try
        {
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using(var bridge=new AndroidJavaClass("com.horizonte.update.HbrpApkProvider"))
                waitingPermission=bridge.CallStatic<int>("install",activity,release.versionCode)==1;
        }
        catch{error="O Android não abriu a instalação. Tente novamente ou continue nesta versão.";waitingPermission=false;}
    }
    void OnApplicationFocus(bool focused)
    {
        if(!focused||!prompt||!waitingPermission)return;
        waitingPermission=false;
        using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using(var manager=activity.Call<AndroidJavaObject>("getPackageManager"))
            if(manager.Call<bool>("canRequestPackageInstalls"))OpenInstaller();
    }
    void OnGUI()
    {
        if(!prompt)return;
        GUI.depth=-1200;float scale=Screen.height/720f;
        var box=new Rect((Screen.width-570*scale)/2,(Screen.height-220*scale)/2,570*scale,220*scale);
        GUI.color=new Color(.04f,.05f,.07f,.98f);GUI.DrawTexture(box,Texture2D.whiteTexture);GUI.color=Color.white;
        var title=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(22*scale),fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        var body=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(14*scale),wordWrap=true,alignment=TextAnchor.MiddleCenter};
        GUI.Label(new Rect(box.x,box.y+14*scale,box.width,38*scale),"ATUALIZAÇÃO v"+release.version,title);
        GUI.Label(new Rect(box.x+20*scale,box.y+58*scale,box.width-40*scale,60*scale),error??"Confirme a instalação do Horizonte no Android. Se solicitado, permita atualizações por este aplicativo.",body);
        var button=new GUIStyle(GUI.skin.button){fontSize=Mathf.RoundToInt(15*scale)};
        if(GUI.Button(new Rect(box.x+20*scale,box.y+135*scale,255*scale,52*scale),"Instalar atualização",button))OpenInstaller();
        if(GUI.Button(new Rect(box.x+295*scale,box.y+135*scale,255*scale,52*scale),"Continuar nesta versão",button))continuePlaying=true;
    }
}
