using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 4.2f;
    public float pitch = 14f;
    public float yaw;
    public float sensitivity = 1f;
    public LayerMask obstacleMask = 1; // Environment only; player is on Ignore Raycast.
    Vector3 smoothFocus;
    Vector3 lastMouse;
    bool dragAllowed;
    float vehicleYawOffset,vehicleLastLook;
    public bool VehicleFollowActive {get;private set;}
    public bool AimActive {get;private set;}
    float aimPreviousDistance,aimPreviousPitch;

    void Start()
    {
        smoothFocus = target.position + Vector3.up * 1.25f;
        lastMouse = Input.mousePosition;
        Place(true);
    }

    void LateUpdate()
    {
        if(StartupLoader.IsLoading || AccountSession.InputBlocked || InGameChat.Editing){lastMouse=Input.mousePosition;dragAllowed=false;Place(false);return;}
        Vector3 mouse = Input.mousePosition;
        float yawDelta=0;
        var hud=GameHud.Instance;
        if((!AimActive&&Input.GetMouseButtonDown(0))||Input.GetMouseButtonDown(1))
            dragAllowed=!(hud&&(hud.IsPanelOpen||hud.BlocksPointer(mouse)));
        if (!MobileControls.HasTouches && ((!AimActive&&Input.GetMouseButton(0)) || Input.GetMouseButton(1)) && dragAllowed && !(hud&&hud.IsPanelOpen) && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
        {
            Vector3 delta = mouse - lastMouse;
            yawDelta += delta.x * .2f * sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * .15f * sensitivity,AimActive?-5f:-10f,AimActive?20f:65f);
        }
        Vector2 touchLook=MobileControls.Look;
        yawDelta+=touchLook.x*.2f*sensitivity;
        yaw+=yawDelta;
        pitch=Mathf.Clamp(pitch-touchLook.y*.15f*sensitivity,AimActive?-5f:-10f,AimActive?20f:65f);
        if(VehicleFollowActive&&target)
        {
            if(Mathf.Abs(yawDelta)>.001f)
            {
                vehicleYawOffset=Mathf.DeltaAngle(target.eulerAngles.y,yaw);
                vehicleLastLook=Time.unscaledTime;
            }
            else
            {
                if(Time.unscaledTime-vehicleLastLook>.9f)
                    vehicleYawOffset=Mathf.MoveTowards(vehicleYawOffset,0,85f*Time.deltaTime);
                yaw=Mathf.LerpAngle(yaw,target.eulerAngles.y+vehicleYawOffset,
                    1-Mathf.Exp(-9f*Time.deltaTime));
            }
        }
        lastMouse = mouse;
        if(!AimActive&&!(hud&&(hud.IsPanelOpen||hud.BlocksPointer(mouse))))
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .35f, 2f, 6f);
        if(AimActive)
        {
            distance=.72f;
            pitch=Mathf.Clamp(pitch,-5f,20f);
        }
        Place(false);
    }

    public void ResetView()
    {
        yaw=target.eulerAngles.y;pitch=14;distance=4.2f;
        Place(true);
    }

    public void BeginAim()
    {
        if(AimActive)return;
        AimActive=true;aimPreviousDistance=distance;aimPreviousPitch=pitch;
        distance=.72f;pitch=3f;yaw=target.eulerAngles.y;Place(true);
    }

    public void EndAim()
    {
        if(!AimActive)return;
        AimActive=false;distance=Mathf.Clamp(aimPreviousDistance,2f,6f);pitch=aimPreviousPitch;Place(true);
    }

    public void BeginVehicleFollow(Transform vehicle)
    {
        if(!vehicle)return;
        target=vehicle;VehicleFollowActive=true;vehicleYawOffset=0;
        vehicleLastLook=Time.unscaledTime;yaw=vehicle.eulerAngles.y;Place(true);
    }

    public void EndVehicleFollow(Transform restoredTarget)
    {
        VehicleFollowActive=false;vehicleYawOffset=0;
        if(restoredTarget)target=restoredTarget;
    }

    void Place(bool snap)
    {
        Vector3 focus = target.position + Vector3.up * (AimActive?1.52f:1.25f);
        if(AimActive)focus+=Quaternion.Euler(0,yaw,0)*Vector3.right*.68f;
        smoothFocus = snap ? focus : Vector3.Lerp(smoothFocus, focus, 1 - Mathf.Exp(-14 * Time.deltaTime));
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 backward = rotation * Vector3.back;
        float safeDistance = distance;
        if (Physics.SphereCast(smoothFocus, .18f, backward, out RaycastHit hit, distance, obstacleMask, QueryTriggerInteraction.Ignore))
            safeDistance = Mathf.Clamp(hit.distance - .03f, .05f, distance);
        transform.position = smoothFocus + backward * safeDistance;
        transform.rotation = rotation;
    }
}
