using System;
using System.IO;
using UnityEngine;

namespace Hbrp.Realtime
{
    public sealed class RealtimeServerHost:MonoBehaviour
    {
        [Serializable] public sealed class Settings
        {public string bindAddress="127.0.0.1",certificateFile,privateKeyFile;public int port=7777;}
        UtpRoomServer server;
        void Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            try
            {
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--hbrp-udp-server-config");
                if(index<0||index+1>=args.Length)throw new ArgumentException("--hbrp-udp-server-config is required");
                string path=Path.GetFullPath(args[index+1]),folder=Path.GetDirectoryName(path);
                var config=JsonUtility.FromJson<Settings>(File.ReadAllText(path));
                if(config.port<1||config.port>65535)throw new ArgumentException("Invalid UDP port");
                server=new UtpRoomServer(config.bindAddress,(ushort)config.port,File.ReadAllText(Path.Combine(folder,config.certificateFile)),
                    File.ReadAllText(Path.Combine(folder,config.privateKeyFile)),Environment.GetEnvironmentVariable("HBRP_REALTIME_SECRET"));
                Debug.Log("HBRP_UDP_SERVER_READY port="+config.port+" encrypted=true tickRate=20");
            }
            catch(Exception e){Debug.LogError("HBRP_UDP_SERVER_FAILED "+e.GetType().Name);Application.Quit(2);}
        }
        void Update(){server?.Tick();}
        void OnDestroy(){server?.Dispose();}
    }
}
