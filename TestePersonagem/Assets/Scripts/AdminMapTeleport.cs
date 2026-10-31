using System.Collections;
using UnityEngine;

public partial class ThirdPersonPlayer
{
    public bool MapTeleportLoading { get; private set; }

    public bool BeginAdminMapTeleport(Vector2 point)
    {
        if(!HasAdminAccess || teleporting || IsCuffed || InPoliceCar || IsSpectating ||
            CombatLocked || MissionVehicle.Driver == this) return false;
        teleportRoutine = StartCoroutine(AdminMapArrival(point));
        return true;
    }

    bool FindMapLanding(Vector2 point, out Vector3 landing)
    {
        landing = default;
        var map = NeighborhoodMap.Active;
        float top = map ? map.maxY + 20 : 1020;
        float bottom = map ? map.minY : -200;
        var hits = Physics.RaycastAll(new Vector3(point.x, top, point.y), Vector3.down,
            top - bottom, 1, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a,b) => b.point.y.CompareTo(a.point.y));
        float radius = controller.radius * .95f;
        float segment = Mathf.Max(0, controller.height * .5f - radius);
        foreach(var hit in hits)
        {
            // No underwater arrivals, steep faces or blocked spaces above the capsule.
            if(hit.normal.y < .72f || hit.point.y < 0) continue;
            var feet = hit.point + Vector3.up * (.04f - (controller.center.y - controller.height * .5f));
            Vector3 center = feet + controller.center;
            if(Physics.CheckCapsule(center - Vector3.up * segment,
                center + Vector3.up * (segment + 2.5f), radius, 1, QueryTriggerInteraction.Ignore)) continue;
            landing = feet; return true;
        }
        return false;
    }

    IEnumerator AdminMapArrival(Vector2 point)
    {
        Vector3 original = transform.position;
        var streaming = FindAnyObjectByType<GtaSaSectorStreaming>();
        var camera = FindAnyObjectByType<ThirdPersonCamera>();
        bool arrived = false;
        var visuals = animator.GetComponentsInChildren<Renderer>();
        var visualStates = new bool[visuals.Length];
        for(int i=0;i<visuals.Length;i++){visualStates[i]=visuals[i].enabled;visuals[i].enabled=false;}
        teleporting = true; MapTeleportLoading = true;
        controller.enabled = false;
        verticalSpeed = 0; IsJumping = false; jumpRequested = false; jumpLaunched = false; CurrentSpeed = 0;
        animator.SetBool("Jumping", false); animator.SetFloat("Speed", 0);
        try
        {
            transform.position = new Vector3(point.x, original.y, point.y);
            if(streaming)
            {
                streaming.PreviewMapDestination(point);
                streaming.RefreshNow();
                float deadline = Time.realtimeSinceStartup + 60;
                while(streaming.DestinationSectorAvailable(point) && !streaming.DestinationSectorLoaded(point) && string.IsNullOrEmpty(streaming.LastError) && Time.realtimeSinceStartup < deadline) yield return null;
            }
            Physics.SyncTransforms();
            if((streaming && !streaming.DestinationSectorLoaded(point)) || !HasAdminAccess || !FindMapLanding(point, out var landing))
            {
                GameHud.Instance?.ShowAccountMessage("Destino sem chão seguro ou ainda indisponível. Escolha outro ponto.");
                transform.position = original;
                if(streaming)
                {
                    streaming.ClearMapDestinationPreview();
                    streaming.RefreshNow();
                    float deadline = Time.realtimeSinceStartup + 30;
                    while(!streaming.Ready && string.IsNullOrEmpty(streaming.LastError) && Time.realtimeSinceStartup < deadline) yield return null;
                }
                yield break;
            }
            transform.position = landing + Vector3.up * 2.5f;
            controller.enabled = true; MapTeleportLoading = false;
            for(int i=0;i<visuals.Length;i++)if(visuals[i])visuals[i].enabled=visualStates[i];
            IsJumping = true; jumpLaunched = true; jumpElapsed = JumpRecovery * .4f;
            animator.SetBool("Jumping", true); animator.Play("Jump", 0, .4f);
            camera?.SnapToTarget();
            float elapsed = 0, fallSpeed = 0;
            while(elapsed < 2)
            {
                float dt = Time.deltaTime; elapsed += dt; jumpElapsed += dt;
                fallSpeed = Mathf.Min(fallSpeed + Gravity * dt, 15);
                var flags = controller.Move(Vector3.down * fallSpeed * dt);
                if((flags & CollisionFlags.Below) != 0 || transform.position.y <= landing.y + .025f) break;
                yield return null;
            }
            controller.enabled = false; transform.position = landing;
            arrived = true;
        }
        finally
        {
            if(!arrived) transform.position = original;
            controller.enabled = true;
            for(int i=0;i<visuals.Length;i++)if(visuals[i])visuals[i].enabled=visualStates[i];
            IsJumping = false; jumpLaunched = false; jumpElapsed = 0; verticalSpeed = -2;
            animator.SetBool("Jumping", false); animator.SetFloat("Speed", 0);
            animator.CrossFadeInFixedTime("Locomocao", .1f, 0);
            MapTeleportLoading = false; teleporting = false; teleportRoutine = null;
            if(streaming)streaming.ClearMapDestinationPreview();
            if(camera) camera.SnapToTarget();
            GameHud.Instance?.NotifyRespawn();
        }
        if(arrived) GameHud.Instance?.ShowAccountMessage("Teleporte concluído.");
    }
}
