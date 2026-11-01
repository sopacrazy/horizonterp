using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds reversible runtime pivots around the existing FBX parts. The imported
// asset and its original hierarchy/materials are never modified on disk.
public sealed class PoliceCarRig : MonoBehaviour
{
    readonly Transform[] doors = new Transform[4];
    readonly float[] doorBlend = new float[4];
    readonly Vector3[] handleOffsets = new Vector3[4];
    readonly List<Wheel> wheels = new List<Wheel>();
    readonly List<Material> runtimeMaterials = new List<Material>();
    sealed class Wheel { public Transform steering, spin; public float radius; public bool front; }
    Transform model;
    float wheelAngle;
    public bool CurrentCoordinates => true;
    public bool HasDoors => doors[0] && doors[3];
    public bool RearTransitioning {get;set;}
    public Vector3 Dimensions { get; private set; }
    // All positions use the rig/parent frame, with +Z pointing toward the hood.
    // Seat coordinates locate the humanoid's hips, not its standing feet.
    public Vector3 DriverSeat { get; private set; } = new Vector3(-.43f, .79f, .16f);
    public Vector3 RearSeat { get; private set; } = new Vector3(.43f, .79f, -.66f);
    public Vector3 DriverDoor { get; private set; } = new Vector3(-1.43f, .03f, .42f);
    public Vector3 RearDoor { get; private set; } = new Vector3(1.43f, .03f, -.55f);
    // The handle follows its door's pivot so the reaching hand does not remain
    // at the closed-door position while the panel swings outward.
    public Vector3 DriverHandle => CurrentHandle(0, new Vector3(-.95f, .97f, .42f));
    public Vector3 RearHandle => CurrentHandle(3, new Vector3(.95f, .97f, -.55f));

    public static PoliceCarRig Create(Transform parent, GameObject source)
    {
        if (!parent) throw new ArgumentNullException(nameof(parent));
        var rig = parent.gameObject.GetComponent<PoliceCarRig>();
        if (rig) return rig;
        rig = parent.gameObject.AddComponent<PoliceCarRig>();
        if (!source) source = Resources.Load<GameObject>("Vehicles/Police/PoliceCar");
        if (!source)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Viatura provisória"; body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0, .8f, 0); body.transform.localScale = new Vector3(2, 1.55f, 4.55f);
            Destroy(body.GetComponent<Collider>()); rig.Dimensions = body.transform.localScale;
            return rig;
        }
        rig.model = Instantiate(source, parent, false).transform;
        rig.model.name = "carro_policia";
        rig.NormalizeModel(); rig.PrepareGlass(); rig.BuildDoors(); rig.BuildWheels();
        return rig;
    }

    void NormalizeModel()
    {
        var bounds = BoundsIn(transform, model);
        var front = Find("bump_front");
        if (front)
        {
            Vector3 direction = BoundsIn(transform, front).center - bounds.center;
            direction.y = 0;
            if (direction.sqrMagnitude > .01f)
                model.localRotation = Quaternion.AngleAxis(-Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, Vector3.up) * model.localRotation;
        }
        else if (bounds.size.x > bounds.size.z)
            model.localRotation = Quaternion.Euler(0, 90, 0) * model.localRotation;
        bounds = BoundsIn(transform, model);
        if (bounds.size.z > .01f) model.localScale *= 4.55f / bounds.size.z;
        bounds = BoundsIn(transform, model);
        model.localPosition += new Vector3(-bounds.center.x, .03f - bounds.min.y, -bounds.center.z);
        Dimensions = BoundsIn(transform, model).size;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    void BuildDoors()
    {
        string[] names = { "door_lf", "door_rf", "door_lr", "door_rr" };
        for (int index = 0; index < names.Length; index++)
        {
            var part = Find(names[index]); if (!part) continue;
            var bounds = BoundsIn(transform, part);
            // Lower metal door edge is wider than the inward-sloping glass;
            // exclude mirrors when locating the vertical front hinge.
            bool left = index == 0 || index == 2;
            float outer = left ? Mathf.Max(bounds.min.x, -.92f) : Mathf.Min(bounds.max.x, .92f);
            var pivot = new GameObject("Dobradiça " + names[index]).transform;
            pivot.gameObject.layer = gameObject.layer; pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(outer, bounds.center.y, bounds.max.z - .055f);
            part.SetParent(pivot, true); doors[index] = pivot;
            if (index == 0)
            {
                DriverDoor = new Vector3(-Dimensions.x * .5f - .37f, .03f, bounds.center.z);
                SaveHandle(index, part, "Object_37", new Vector3(bounds.min.x + .07f, .97f, bounds.center.z));
            }
            if (index == 3)
            {
                RearDoor = new Vector3(Dimensions.x * .5f + .37f, .03f, bounds.center.z);
                SaveHandle(index, part, "Object_57", new Vector3(bounds.max.x - .04f, .97f, bounds.center.z));
            }
        }
    }

    void SaveHandle(int index, Transform part, string handleName, Vector3 fallback)
    {
        Vector3 point = fallback;
        foreach (var child in part.GetComponentsInChildren<Transform>(true))
            if (child.name == handleName) { point = BoundsIn(transform, child).center; break; }
        handleOffsets[index] = doors[index].InverseTransformPoint(transform.TransformPoint(point));
    }
    Vector3 CurrentHandle(int index, Vector3 fallback) => doors[index]
        ? transform.InverseTransformPoint(doors[index].TransformPoint(handleOffsets[index])) : fallback;

    void BuildWheels()
    {
        string[] names = { "wheel_24", "wheel.001", "wheel.002", "wheel.003" };
        foreach (string name in names)
        {
            var part = Find(name); if (!part) continue;
            var bounds = BoundsIn(transform, part);
            var steering = new GameObject("Direção " + name).transform;
            steering.SetParent(transform, false); steering.localPosition = bounds.center;
            var spin = new GameObject("Rolagem " + name).transform;
            spin.SetParent(steering, false); part.SetParent(spin, true);
            wheels.Add(new Wheel { steering = steering, spin = spin, radius = Mathf.Clamp(bounds.extents.y, .2f, .6f), front = bounds.center.z > 0 });
        }
    }

    public void SetDoor(bool driver, float blend) => SetDoor(driver ? 0 : 3, blend);
    // Door indices: front left, front right, rear left, rear right.
    public void SetDoor(int index, float blend)
    {
        if (index < 0 || index >= doors.Length || !doors[index]) return;
        doorBlend[index] = Mathf.Clamp01(blend);
        float side = index == 0 || index == 2 ? 1 : -1;
        doors[index].localRotation = Quaternion.Euler(0, side * 68 * Mathf.SmoothStep(0, 1, doorBlend[index]), 0);
    }
    public void SetDoorOpen(bool driver, bool open, float deltaTime)
    {
        int index = driver ? 0 : 3;
        SetDoor(index, Mathf.MoveTowards(doorBlend[index], open ? 1 : 0, Mathf.Max(0, deltaTime) * 2.8f));
    }
    public float DoorOpenAmount(bool driver) => doorBlend[driver ? 0 : 3];
    // distance is signed metres moved this frame; steer is normalized -1..1.
    public void RotateWheels(float distance, float steer)
    {
        if (wheels.Count == 0) return;
        wheelAngle = Mathf.Repeat(wheelAngle + distance / wheels[0].radius * Mathf.Rad2Deg, 360);
        foreach (var wheel in wheels)
        {
            wheel.steering.localRotation = Quaternion.Euler(0, wheel.front ? Mathf.Clamp(steer, -1, 1) * 28 : 0, 0);
            wheel.spin.localRotation = Quaternion.Euler(wheelAngle, 0, 0);
        }
    }

    Transform Find(string prefix)
    {
        foreach (var item in model.GetComponentsInChildren<Transform>(true))
            if (item.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }
    static Bounds BoundsIn(Transform space, Transform source)
    {
        bool found = false; var result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            var bounds = renderer.localBounds;
            var matrix = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
            }
        }
        return result;
    }

    void PrepareGlass()
    {
        // Exact glass meshes identified in the source, rather than making all
        // black materials transparent (which would also hide dashboard/trim).
        var glass = new HashSet<string> { "Object_36", "Object_42", "Object_50", "Object_56", "Object_69", "Object_91" };
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!glass.Contains(renderer.name)) continue;
            var originals = renderer.sharedMaterials; var materials = new Material[originals.Length];
            for (int index = 0; index < originals.Length; index++)
            {
                var material = new Material(Shader.Find("Standard")); material.name = "Vidro viatura · runtime";
                material.color = new Color(.19f, .28f, .31f, .22f); material.SetFloat("_Mode", 2);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0); material.EnableKeyword("_ALPHABLEND_ON"); material.renderQueue = 3000;
                runtimeMaterials.Add(material); materials[index] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }
    void OnDestroy() { foreach (var material in runtimeMaterials) if (material) {if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);} }
}
