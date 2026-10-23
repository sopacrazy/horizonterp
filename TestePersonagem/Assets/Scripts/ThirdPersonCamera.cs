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
        var hud=GameHud.Instance;
        if(Input.GetMouseButtonDown(0)||Input.GetMouseButtonDown(1))
            dragAllowed=!(hud&&(hud.IsPanelOpen||hud.BlocksPointer(mouse)));
        if (!MobileControls.HasTouches && (Input.GetMouseButton(0) || Input.GetMouseButton(1)) && dragAllowed && !(hud&&hud.IsPanelOpen) && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
        {
            Vector3 delta = mouse - lastMouse;
            yaw += delta.x * .2f * sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * .15f * sensitivity, -10, 65);
        }
        Vector2 touchLook=MobileControls.Look;
        yaw+=touchLook.x*.2f*sensitivity;
        pitch=Mathf.Clamp(pitch-touchLook.y*.15f*sensitivity,-10,65);
        lastMouse = mouse;
        if(!(hud&&(hud.IsPanelOpen||hud.BlocksPointer(mouse))))
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * .35f, 2f, 6f);
        Place(false);
    }

    public void ResetView()
    {
        yaw=target.eulerAngles.y;pitch=14;distance=4.2f;
        Place(true);
    }

    void Place(bool snap)
    {
        Vector3 focus = target.position + Vector3.up * 1.25f;
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
