using System;
using System.Collections;
using UnityEngine;
public static class VaultProcessVerification
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Run()
    {
        string[] args=Environment.GetCommandLineArgs();int flag=Array.IndexOf(args,"--vault-fixture");
        if(flag<0||flag+2>=args.Length)return;
        bool success=SessionVault.CrossProcessFixture(args[flag+1],args[flag+2]);
        AccountSession.VerificationTransport=NoNetwork;SessionVault.TestRead=()=>null;
        Debug.Log("VAULT_CROSS_PROCESS_"+(success?"OK":"FAILED")+" mode="+args[flag+1]);
        Application.Quit(success?0:1);
    }
    static IEnumerator NoNetwork(string method,string path,string body,bool authenticated,Action<ApiReply,string> done)
    {done(null,"Teste isolado do cofre, sem rede.");yield break;}
}
