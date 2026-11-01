using UnityEngine;

public struct RadarPlayer
{
    public string characterId, uid, name;
    public Vector3 position;
}

public static class PlayerRadar
{
    public const float Radius = 1000f;
    public static bool Visible(bool admin, bool fullMap, Vector3 self, Vector3 target)
    {
        if (!float.IsFinite(target.x) || !float.IsFinite(target.z)) return false;
        float x = self.x - target.x, z = self.z - target.z;
        return admin && fullMap || x * x + z * z <= Radius * Radius;
    }
    public static Vector2 OnMinimap(Vector3 self, Vector3 target, float yaw, float diameter, float visibleMetres = Radius * 2)
    {
        // Atlas z increases downwards; use exactly the same rotation as the artwork.
        var delta = new Vector2(target.x - self.x, target.z - self.z) * (diameter / visibleMetres);
        float a = (yaw + 180) * Mathf.Deg2Rad;
        return new Vector2(delta.x * Mathf.Cos(a) - delta.y * Mathf.Sin(a), delta.x * Mathf.Sin(a) + delta.y * Mathf.Cos(a));
    }
}
