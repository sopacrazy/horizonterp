using System.Collections;
using UnityEngine;

// This scene has no StartupLoader/AccountSession. It exercises only local vehicle
// presentation and controls; no saved account or production endpoint is opened.
public sealed class PoliceVehiclePreview : MonoBehaviour
{
    public ThirdPersonPlayer player;
    public Vector3 carPosition=Vector3.zero;
    PoliceVehicleController car;

    IEnumerator Start()
    {
#if UNITY_EDITOR
        if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--verify-police-car-play")>=0)gameObject.AddComponent<PoliceAnimationPoseProbe>();
#endif
        yield return null; // GangsterPlayerVisual and ThirdPersonPlayer install the avatar first.
        if(!player)player=FindAnyObjectByType<ThirdPersonPlayer>();
        if(!player||!player.animator){Debug.LogError("Prévia da viatura: personagem ausente.");yield break;}
        if(AccountSession.Instance)
        {Debug.LogError("A prévia da viatura requer uma cena offline, sem sessão de conta.");yield break;}
        var world=player.GetComponent<PoliceCarWorld>();
        if(!world)world=player.gameObject.AddComponent<PoliceCarWorld>();
        world.Initialize(player);
        car=world.CreatePreview(carPosition);
        car.LocalPreview=true;
        GameHud.Instance?.ShowToast("PRÉVIA LOCAL · E entrar · WASD dirigir · F sair com a viatura parada");
    }
    void Update()
    {
        if(!car||!player)return;
        if(InGameChat.Editing||GameHud.Instance&&GameHud.Instance.IsPanelOpen)return;
        if(Input.GetKeyDown(KeyCode.E)&&!PoliceVehicleController.Active)car.RequestEnter(player);
        if(Input.GetKeyDown(KeyCode.F)&&PoliceVehicleController.Active==car)car.RequestExit();
    }
    void OnGUI()
    {
        var style=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};
        style.normal.textColor=Color.white;
        GUI.Box(new Rect(20,Screen.height-125,510,64),GUIContent.none);
        GUI.Label(new Rect(32,Screen.height-116,486,50),"PRÉVIA LOCAL — VIATURA ANIMADA\nE: entrar   •   WASD: dirigir   •   F: sair (pare antes)",style);
    }
}
