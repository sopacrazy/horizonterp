using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

// Separate local gallery for source GTA SA interiors. It never loads exterior sector scenes.
public sealed class GtaSaInteriorGallery : MonoBehaviour
{
    [Serializable] public sealed class InteriorScene
    {
        public int id;
        public string sceneName;
        public int instances;
    }

    [Serializable] public sealed class Destination
    {
        public string key;
        public int interiorId;
        public int instances;
        public bool sourceHasCollisionCandidate;
        public Vector3 center;
    }

    [Serializable] sealed class CaptureResult
    {
        public string destination;
        public int interiorId, renderers, colliders, loadedScenes;
        public bool floorHit;
        public float floorY;
        public float unityReservedMemoryMb;
        public string image;
    }

    [Serializable] sealed class GalleryReport
    {
        public int sceneCount, catalogCount;
        public CaptureResult[] captures;
        public string error;
    }

    public Camera galleryCamera;
    public InteriorScene[] interiorScenes = Array.Empty<InteriorScene>();
    public Destination[] destinations = Array.Empty<Destination>();
    public string reviewDirectory = "";

    int destinationIndex = -1;
    int currentId = -1;
    bool changing;
    string status = "Preparando interiores...";
    float yaw = 35f, pitch;
    readonly List<CaptureResult> captures = new List<CaptureResult>();

    string ReviewFolder => string.IsNullOrEmpty(reviewDirectory)
        ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/gta-sa-import/review/interiors"))
        : Path.GetFullPath(reviewDirectory);

    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        if (!galleryCamera) galleryCamera = Camera.main;
        if (!galleryCamera || interiorScenes.Length != 18 || destinations.Length == 0)
        {
            status = "Galeria incompleta: câmera, 18 cenas ou catálogo ausente.";
            Debug.LogError(status);
            yield break;
        }

        // The hotel is a useful low-altitude inspection point, rather than an arbitrary prop.
        int first = IndexOf("int-18-165");
        if (first < 0) first = 0;
        yield return OpenDestination(first);

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-sa-interiors") >= 0)
            yield return VerifyGallery();
    }

    void Update()
    {
        if (changing || destinationIndex < 0 || !galleryCamera) return;
        if (Input.GetKeyDown(KeyCode.F3)) StartCoroutine(OpenDestination((destinationIndex + 1) % destinations.Length));
        if (Input.GetKeyDown(KeyCode.F4)) StartCoroutine(OpenDestination((destinationIndex - 1 + destinations.Length) % destinations.Length));

        // Inspection camera deliberately has no player gravity or reset-height rule.
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            yaw += Input.GetAxis("Mouse X") * 2.2f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.2f, -85, 85);
        }
        else Cursor.lockState = CursorLockMode.None;
        galleryCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
        move.y = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
        float speed = Input.GetKey(KeyCode.LeftShift) ? 18 : 5;
        galleryCamera.transform.position += galleryCamera.transform.rotation * move.normalized * speed * Time.deltaTime;
    }

    public void SelectDestination(int index)
    {
        if (!changing && index >= 0 && index < destinations.Length) StartCoroutine(OpenDestination(index));
    }

    int IndexOf(string key)
    {
        for (int i = 0; i < destinations.Length; i++)
            if (string.Equals(destinations[i].key, key, StringComparison.Ordinal)) return i;
        return -1;
    }

    InteriorScene FindScene(int id)
    {
        foreach (var entry in interiorScenes) if (entry.id == id) return entry;
        return null;
    }

    IEnumerator OpenDestination(int index)
    {
        changing = true;
        var destination = destinations[index];
        var target = FindScene(destination.interiorId);
        var shared = FindScene(0);
        if (target == null || shared == null)
        {
            status = "Cena interior ausente para ID " + destination.interiorId;
            Debug.LogError(status);
            changing = false;
            yield break;
        }

        if (currentId != destination.interiorId)
        {
            status = "Carregando " + destination.key + "...";
            bool unloadedPrevious = false;
            if (currentId > 0)
            {
                var previous = FindScene(currentId);
                if (previous != null && SceneManager.GetSceneByName(previous.sceneName).isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(previous.sceneName);
                    unloadedPrevious = true;
                }
            }
            if (unloadedPrevious)
                yield return Resources.UnloadUnusedAssets();
            if (!SceneManager.GetSceneByName(shared.sceneName).isLoaded)
                yield return LoadInteriorScene(shared.sceneName);
            if (destination.interiorId != 0 && !SceneManager.GetSceneByName(target.sceneName).isLoaded)
                yield return LoadInteriorScene(target.sceneName);
            currentId = destination.interiorId;
        }

        destinationIndex = index;
        PlaceCamera(destination);
        status = destination.key + " · interior " + destination.interiorId + " · " + destination.instances +
            " objetos na área" + (destination.sourceHasCollisionCandidate ? "" : " · piso não confirmado na fonte");
        changing = false;
    }

    static AsyncOperation LoadInteriorScene(string name)
    {
#if UNITY_EDITOR
        string path = "Assets/Scenes/GTASanAndreasModInteriors/" + name + ".unity";
        return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
#else
        return SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
#endif
    }

    void PlaceCamera(Destination destination)
    {
        var center = destination.center;
        float floorY = FindFloor(center, out var value) ? value : center.y;
        var eye = new Vector3(center.x, floorY + 1.7f, center.z);
        var offsets = new[] { Vector3.zero, Vector3.right * 1.5f, Vector3.left * 1.5f,
            Vector3.forward * 1.5f, Vector3.back * 1.5f, new Vector3(2.5f, 0, 2.5f),
            new Vector3(-2.5f, 0, -2.5f) };
        foreach (var offset in offsets)
        {
            if (Physics.CheckSphere(eye + offset, .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            eye += offset;
            break;
        }
        galleryCamera.transform.position = eye;
        yaw = 35f;
        pitch = 0f;
        galleryCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
    }

    static bool FindFloor(Vector3 center, out float floorY)
    {
        floorY = center.y;
        bool found = false;
        var ray = new Ray(center + Vector3.up * 8f, Vector3.down);
        foreach (var hit in Physics.RaycastAll(ray, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < .45f || hit.point.y > center.y + 1f || hit.point.y < center.y - 8f) continue;
            if (found && hit.point.y < floorY) continue;
            floorY = hit.point.y;
            found = true;
        }
        return found;
    }

    IEnumerator VerifyGallery()
    {
        Directory.CreateDirectory(ReviewFolder);
        var keys = new[] { "int-03-045", "int-18-165", "int-01-014" };
        string error = "";
        foreach (string key in keys)
        {
            int index = IndexOf(key);
            if (index < 0) { error += "Destino ausente: " + key + "; "; continue; }
            yield return OpenDestination(index);
            yield return null;
            yield return new WaitForEndOfFrame();
            var destination = destinations[index];
            var record = new CaptureResult {
                destination = key, interiorId = destination.interiorId,
                renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length,
                colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None).Length,
                loadedScenes = SceneManager.sceneCount,
                unityReservedMemoryMb = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f),
                image = Path.Combine(ReviewFolder, key + ".png")
            };
            record.floorHit = FindFloor(destination.center, out record.floorY);
            Capture(record.image);
            captures.Add(record);
        }
        var report = new GalleryReport { sceneCount = interiorScenes.Length,
            catalogCount = destinations.Length, captures = captures.ToArray(), error = error };
        File.WriteAllText(Path.Combine(ReviewFolder, "runtime.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GTA_SA_INTERIORS_VERIFY_DONE captures=" + captures.Count + " error=" + error);
        Application.Quit(string.IsNullOrEmpty(error) ? 0 : 1);
    }

    void Capture(string file)
    {
        var target = new RenderTexture(1440, 900, 24);
        var previous = RenderTexture.active;
        var previousTarget = galleryCamera.targetTexture;
        try
        {
            galleryCamera.targetTexture = target;
            galleryCamera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
            Destroy(image);
        }
        finally
        {
            galleryCamera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            target.Release();
            Destroy(target);
        }
    }

    void OnGUI()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-sa-interiors") >= 0) return;
        GUI.Box(new Rect(15, 15, 650, 70), "Galeria GTA SA · teste separado\n" + status +
            "\nF3/F4: próximo/anterior · mouse direito: olhar · WASD/QE: mover · Shift: rápido");
    }
}
