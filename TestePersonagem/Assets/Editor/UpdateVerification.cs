using System;
using System.IO;
using UnityEngine;

public static class UpdateVerification
{
    public static void Run()
    {
        var release=new AndroidRelease {available=true,version="0.6.0",versionCode=9,packageId=GameVersion.Current.packageId,
            apkUrl="https://github.com/sopacrazy/horizonterp/releases/download/v0.6.0/HorizonteBrasil-0.6.0.apk",sha256=new string('a',64),bytes=60000000};
        Require(GameUpdater.Valid(release),"Valid signed-release metadata accepted");
        Require(GameUpdater.IsNewer(release,8),"New version offered");
        Require(!GameUpdater.IsNewer(release,9)&&!GameUpdater.IsNewer(release,10),"Same version and downgrade ignored");
        release.apkUrl="http://github.com/untrusted.apk";Require(!GameUpdater.Valid(release),"HTTP and untrusted download rejected");
        release.apkUrl="https://github.com/sopacrazy/horizonterp/releases/download/v0.6.0/HorizonteBrasil-0.6.0.apk";
        release.packageId="another.game";Require(!GameUpdater.Valid(release),"Wrong package rejected");release.packageId=GameVersion.Current.packageId;
        release.bytes=0;Require(!GameUpdater.Valid(release),"Missing size rejected");release.bytes=60000000;
        release.sha256="bad";Require(!GameUpdater.Valid(release),"Missing digest rejected");
        string path=Path.GetFullPath("../tools/update-hash-fixture.tmp");
        try{File.WriteAllText(path,"abc");Require(GameUpdater.FileHash(path)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","Downloaded file SHA-256 verified");}
        finally{if(File.Exists(path))File.Delete(path);}
        Debug.Log("UPDATE_POLICY_VERIFIED: version ordering, source/package/size/digest validation and file integrity");
        PrototypeSetup.BuildWindows();
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
