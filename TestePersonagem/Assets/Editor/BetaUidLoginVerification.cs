using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// UID-only login verification. Fixtures never contact a server or the real vault.
// Include --verify-beta-login to also exercise the explicit offline transport.
public static class BetaUidLoginVerification
{
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    [Serializable] sealed class Result {public bool passed;public List<string> checks=new List<string>();}
    static object Call(AccountSession account,string name,params object[] values)=>typeof(AccountSession).GetMethod(name,Instance).Invoke(account,values);
    static IEnumerator Response(Action done){done();yield break;}
    static void Drain(IEnumerator routine)
    {
        int count=0;
        while(routine.MoveNext())
        {
            if(++count>100)throw new InvalidOperationException("Unexpected asynchronous wait in the offline login fixture");
            if(routine.Current is IEnumerator child)Drain(child);
        }
    }
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play antes desta verificação.");
        if(AccountSession.Instance)throw new InvalidOperationException("An existing account session must not be used by verification.");
        var result=new Result();
        void Check(bool pass,string description){if(!pass)throw new InvalidOperationException(description);result.checks.Add(description);}
        var type=typeof(AccountSession);var transport=type.GetField("VerificationTransport",Static);
        var singleton=type.GetField("<Instance>k__BackingField",Static);
        var vault=type.Assembly.GetType("SessionVault");
        var read=vault.GetField("TestRead",Static);var write=vault.GetField("TestWrite",Static);var clear=vault.GetField("TestClear",Static);
        object oldTransport=transport.GetValue(null),oldRead=read.GetValue(null),oldWrite=write.GetValue(null),oldClear=clear.GetValue(null);
        bool hadUid=PlayerPrefs.HasKey("account.uid"),hadEmail=PlayerPrefs.HasKey("account.email");
        string oldUid=PlayerPrefs.GetString("account.uid",""),oldEmail=PlayerPrefs.GetString("account.email","");
        GameObject holder=null;
        try
        {
            // Intercept vault access before any authenticated fixture is created.
            var written=new List<string>();int cleared=0;
            read.SetValue(null,(Func<string>)(()=>new string('U',43)));
            write.SetValue(null,(Action<string>)(value=>written.Add(value)));
            clear.SetValue(null,(Action)(()=>cleared++));
            Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> noNetwork=(method,path,body,authenticated,done)=>Response(()=>done(null,"No fixture response"));
            transport.SetValue(null,noNetwork);
            PlayerPrefs.SetString("account.email","legacy-address@example.invalid");PlayerPrefs.DeleteKey("account.uid");
            holder=new GameObject("Offline UID beta login check"){hideFlags=HideFlags.HideAndDontSave};
            var account=holder.AddComponent<AccountSession>();singleton.SetValue(null,account);
            Call(account,"Build",new object[]{null});
            var root=account.Document.rootVisualElement;
            Check(root.Q<TextField>("auth-uid")!=null,"The login form has a UID field");
            var uid=root.Q<TextField>("auth-uid");var password=root.Q<TextField>("auth-password");
            Check(uid.keyboardType==TouchScreenKeyboardType.NumberPad&&uid.maxLength==19,"Mobile UID entry uses the numeric keyboard with a bounded identifier");
            Check(uid.value=="","An old e-mail preference is never reused as a UID");
            Check(root.Q<TextField>("auth-email")==null&&root.Q<Button>("auth-register-email")==null&&root.Q<Button>("auth-toggle")==null,
                "The public e-mail and registration controls are absent");
            Check(root.Q<Button>("auth-facebook")==null&&root.Q<Button>("auth-google")==null,"Alternative provider entry points are absent in the closed beta");
            Check(root.Q<TextField>("auth-confirm")==null,"There is no public password-creation form");
            Call(account,"DisplayEmailForm",true);
            Check(!(bool)type.GetField("register",Instance).GetValue(account),"The legacy register-form entry point can only display UID login");
            password.value="LocalBetaFixture123";
            var validUid=type.GetMethod("IsBetaUid",Static);
            foreach(string invalid in new[]{"","0","018","-18","18.0","equipe@example.invalid","١٨","18\n20","12345678901234567890"})
                Check(!(bool)validUid.Invoke(null,new object[]{invalid}),"UID contract rejects invalid input "+result.checks.Count);
            // The TextField itself truncates overlong input and strips line breaks;
            // exercise those cases directly above, then test the actual form.
            foreach(string invalid in new[]{"","0","018","-18","18.0","invalid@uid","١٨"})
            {
                uid.value=invalid;Check(!(bool)Call(account,"ValidateForm"),"UID validation rejects invalid input "+result.checks.Count);
            }
            uid.value=" 18 ";
            Check((bool)Call(account,"ValidateForm")&&uid.value=="18","A valid UID is trimmed and accepted");
            password.value="short";Check(!(bool)Call(account,"ValidateForm"),"The provided password must satisfy the existing length policy");
            password.value="LocalBetaFixture123";
            string serialized=JsonUtility.ToJson(new BetaLoginBody {uid="18",password="LocalBetaFixture123"});
            Check(serialized.Contains("\"uid\":\"18\"")&&!serialized.Contains("email")&&!serialized.Contains("accountRole"),"Login serialization sends UID/password without legacy or privileged fields");
            var envelope=JsonUtility.FromJson<ApiReply>("{\"token\":\"fixture\",\"state\":null,\"needsCharacter\":true,\"account\":{\"uid\":\"900018\",\"accountRole\":\"user\",\"adminLevel\":0}}");
            // JsonUtility can materialize an empty nested serializable object for
            // JSON null. Assert the runtime no-character contract (the same
            // characterId condition used by HasCharacter), not object identity.
            Check(envelope.needsCharacter&&string.IsNullOrEmpty(envelope.state?.characterId)&&envelope.account?.uid=="900018",
                "A parsed approved account provides its UID without an existing character");
            var denial=type.GetMethod("IsAccessRevokedCode",Static);
            foreach(string code in new[]{"BETA_ACCESS_REQUIRED","BETA_ACCESS_REVOKED","UNAUTHORIZED","CHARACTER_BLOCKED"})
                Check((bool)denial.Invoke(null,new object[]{code}),"Saved access recognizes terminal authorization denial "+code);
            Check(!(bool)denial.Invoke(null,new object[]{"CONNECTION_LOST"}),"A network outage alone does not erase remembered access");
            if(Environment.GetCommandLineArgs().Contains("--verify-beta-login"))
            {
                string sentMethod=null,sentPath=null,sentBody=null;bool sentAuthenticated=true;
                Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> approved=(method,path,body,authenticated,done)=>Response(()=>{
                    sentMethod=method;sentPath=path;sentBody=body;sentAuthenticated=authenticated;
                    done(new ApiReply {token=new string('U',43),needsCharacter=true,account=new ApiAccount {uid="900018",accountRole="user",adminLevel=0}},null);
                });
                transport.SetValue(null,approved);uid.value="900018";password.value="LocalBetaFixture123";
                type.GetField("register",Instance).SetValue(account,true);
                Drain((IEnumerator)Call(account,"Submit"));
                Check(sentMethod=="POST"&&sentPath=="/v1/auth/login"&&!sentAuthenticated,"Even a forced legacy flag submits only to UID login, never to register");
                Check(sentBody.Contains("\"uid\":\"900018\"")&&!sentBody.Contains("email"),"The actual submitted payload uses only the UID login contract");
                Check(AccountSession.Authenticated&&!AccountSession.HasCharacter&&root.Q("character-create").style.display.value==DisplayStyle.Flex,
                    "Approved credentials without a character open character creation");
                Check(written.Count==1&&written[0].Length==43,"Remembered access stores only the issued session token in the intercepted vault");
                Check(uid.value=="900018"&&!PlayerPrefs.HasKey("account.uid")&&PlayerPrefs.GetString("account.email")=="legacy-address@example.invalid",
                    "The pre-character UID is retained without touching real preferences during verification");
                Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> revoked=(method,path,body,authenticated,done)=>Response(()=>done(new ApiReply {code="BETA_ACCESS_REQUIRED"},"Acesso beta não autorizado."));
                transport.SetValue(null,revoked);
                Drain((IEnumerator)type.GetMethod("RestoreRemembered",Instance).Invoke(account,null));
                Check(!AccountSession.Authenticated&&!AccountSession.HasCharacter&&!account.ReadyToEnterCity&&cleared>0,
                    "Revoked beta authorization clears the old vault and cannot enter the city");
                Check(account.Document.rootVisualElement.Q<Label>("auth-message").text=="Acesso beta não autorizado.","The UID form displays the beta access denial");
            }
            result.passed=true;
            string folder=Path.GetFullPath("../tools/beta-access");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"unity-login-validation.json"),JsonUtility.ToJson(result,true));
            Debug.Log("BETA_UID_LOGIN_VERIFIED checks="+result.checks.Count);
        }
        finally
        {
            transport.SetValue(null,oldTransport);read.SetValue(null,oldRead);write.SetValue(null,oldWrite);clear.SetValue(null,oldClear);
            singleton.SetValue(null,null);if(holder)UnityEngine.Object.DestroyImmediate(holder);
            if(hadUid)PlayerPrefs.SetString("account.uid",oldUid);else PlayerPrefs.DeleteKey("account.uid");
            if(hadEmail)PlayerPrefs.SetString("account.email",oldEmail);else PlayerPrefs.DeleteKey("account.email");
            PlayerPrefs.Save();
        }
    }
}
