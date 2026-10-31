using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hbrp.Realtime
{
    public static class LocalNetworkLab
    {
        public static bool IsolateSavedAccess=>Active||Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-multiplayer")>=0;
        public static bool Active
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                return Array.IndexOf(Environment.GetCommandLineArgs(),"--hbrp-local-lab-config")>=0;
#else
                return false;
#endif
            }
        }
        public static string Argument(string name)
        {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:null;}
        public static ApiPublicConfig Load()
        {
            var config=JsonUtility.FromJson<ApiPublicConfig>(File.ReadAllText(Argument("--hbrp-local-lab-config")));
            var uri=new Uri(config.baseUrl);
            if(uri.Scheme!="https"||!uri.IsLoopback||uri.Port!=18443||config.certificateSha256?.Length!=64)
                throw new InvalidOperationException("Local lab accepts only pinned HTTPS on loopback:18443");
            return config;
        }
    }
    public sealed class LocalLabLogin:MonoBehaviour
    {
        [Serializable] sealed class Fixture {public string email,password;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(LocalNetworkLab.Active&&LocalNetworkLab.Argument("--hbrp-local-player")!=null&&Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-multiplayer")<0&&!FindAnyObjectByType<LocalLabLogin>())
                DontDestroyOnLoad(new GameObject("Login temporário local").AddComponent<LocalLabLogin>().gameObject);
        }
        IEnumerator Start()
        {
            var fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(LocalNetworkLab.Argument("--hbrp-local-player")));
            if(!fixture.email.EndsWith("@example.invalid"))throw new InvalidOperationException("Only temporary local users are allowed");
            while(!AccountSession.Instance||!AccountSession.Instance.Document||AccountSession.Instance.Document.rootVisualElement.Q<Button>("auth-login-email")==null)yield return null;
            var root=AccountSession.Instance.Document.rootVisualElement;
            Submit(root.Q<Button>("auth-login-email"));root.Q<TextField>("auth-email").value=fixture.email;root.Q<TextField>("auth-password").value=fixture.password;Submit(root.Q<Button>("auth-submit"));
            while(!AccountSession.HasCharacter)yield return null;
            Submit(root.Q<Button>("character-enter"));Destroy(gameObject);
        }
        static void Submit(Button button){button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())button.SendEvent(evt);}
    }
}
