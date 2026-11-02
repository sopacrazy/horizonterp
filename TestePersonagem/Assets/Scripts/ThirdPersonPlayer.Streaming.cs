using UnityEngine;

public partial class ThirdPersonPlayer
{
    GtaSaSectorStreaming groundStreaming;
    float streamingGroundWaitDeadline;
    bool streamingGroundWaitExpired;
    const float MaximumGroundWait = 8f;

    // Called before gravity/jump integration. A loaded physical floor permits
    // movement even while scenery elsewhere or a map preview is still loading.
    bool WaitForStreamingGround(Vector3 displacement)
    {
        if (!groundStreaming || !controller.enabled || !controller.isGrounded ||
            !string.IsNullOrEmpty(groundStreaming.LastError))
        {
            ResetStreamingGroundWait();
            return false;
        }
        Vector3 destination = transform.position + Vector3.ProjectOnPlane(displacement, Vector3.up);
        if (groundStreaming.CollisionAreaLoadedAt(destination, controller.radius + controller.skinWidth))
        {
            ResetStreamingGroundWait();
            return false;
        }
        float step = Mathf.Max(.1f, controller.stepOffset) + .1f;
        float feet = destination.y + controller.center.y - controller.height * .5f;
        Vector3 origin = new Vector3(destination.x, feet + step, destination.z);
        if (Physics.Raycast(origin, Vector3.down, out var hit, step + .5f,
            1, QueryTriggerInteraction.Ignore) && hit.normal.y > .45f)
        {
            ResetStreamingGroundWait();
            return false;
        }
        // An unavailable build sector or an unrelated overlapping bound must
        // not suspend the controller forever. Afterwards normal physics and
        // the existing fall/respawn handling remain in charge.
        if (streamingGroundWaitExpired) return false;
        if (streamingGroundWaitDeadline == 0)
            streamingGroundWaitDeadline = Time.realtimeSinceStartup + MaximumGroundWait;
        if (Time.realtimeSinceStartup >= streamingGroundWaitDeadline)
        {
            streamingGroundWaitExpired = true;
            Debug.LogWarning("SA_GROUND_WAIT_TIMEOUT position=" + destination);
            return false;
        }
        CurrentSpeed = 0;
        animator.SetFloat("Speed", 0);
        return true;
    }

    void ResetStreamingGroundWait()
    {
        streamingGroundWaitDeadline = 0;
        streamingGroundWaitExpired = false;
    }
}
