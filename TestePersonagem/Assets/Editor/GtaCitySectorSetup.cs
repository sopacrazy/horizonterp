using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Split only the regenerated official city. Sector scenes share the same imported assets.
public static class GtaCitySectorSetup
{
    public static void SplitAndBuildWindows(){SplitOfficialCity();PrototypeSetup.BuildWindows();}
    const string OfficialScene = "Assets/Scenes/TestePersonagem.unity";
    const string BootScene = "Assets/Scenes/Entrada.unity";
    const string OfficialRoot = "Cidade Horizonte - Mapa Oficial";
    const string SectorFolder = "Assets/Scenes/CitySectors";
    const string SectorPrefix = "CitySector_";
    const string ReviewFolder = "../tools/gta-map-review";
    const float CellSize = 128f;

    sealed class Node
    {
        public MeshRenderer renderer;
        public Bounds bounds;
        public bool wasActive;
    }

    sealed class Cell
    {
        public int x, z;
        public readonly List<Node> nodes = new List<Node>();
        public Bounds bounds;
        public string SceneName => SectorPrefix + x + "_" + z;
        public string ScenePath => SectorFolder + "/" + SceneName + ".unity";
    }

    [Serializable]
    sealed class SectorReport
    {
        public string scene, scenePath;
        public int cellX, cellZ, renderers;
        public Bounds bounds;
    }

    [Serializable]
    sealed class SplitReport
    {
        public string officialScene, backup, scenePrefix;
        public float cellSize;
        public int sectorCount, totalRenderers, minObjectsPerSector, maxObjectsPerSector;
        public Bounds fullBounds;
        public SectorReport[] sectors;
    }

    [MenuItem("Horizonte/Cidade oficial/Dividir cidade em setores de streaming")]
    public static void SplitOfficialCity()
    {
        var official = EditorSceneManager.OpenScene(OfficialScene, OpenSceneMode.Single);
        var city = official.GetRootGameObjects().SingleOrDefault(root => root.name == OfficialRoot);
        if (!city) throw new InvalidOperationException("Raiz da cidade oficial ausente: " + OfficialRoot);
        var existing = city.GetComponent<CitySectorStreaming>();
        var renderers = city.GetComponentsInChildren<MeshRenderer>(true);
        if (existing && existing.sectors != null && existing.sectors.Length > 0)
            throw new InvalidOperationException("A cidade oficial ja foi dividida. Regenere a cidade com GtaOfficialCitySetup.Apply antes de dividir novamente.");
        if (renderers.Length == 0)
            throw new InvalidOperationException("Cidade sem geometria para dividir. Regenere a cena oficial antes de exportar os setores.");
        if (renderers.Any(renderer => renderer.gameObject == city))
            throw new InvalidOperationException("A raiz oficial deve conter somente sistemas; mova sua geometria para um filho antes da divisao.");
        var player = official.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ThirdPersonPlayer>(true)).SingleOrDefault();
        if (!player) throw new InvalidOperationException("Personagem da cidade oficial ausente.");

        // Snapshot world-space bounds before any node leaves its translated parent.
        var cells = new Dictionary<Vector2Int, Cell>();
        Bounds fullBounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            if (!Finite(bounds.center) || !Finite(bounds.extents))
                throw new InvalidOperationException("Bounds invalidos na geometria: " + renderer.name);
            var key = new Vector2Int(Mathf.FloorToInt(bounds.center.x / CellSize), Mathf.FloorToInt(bounds.center.z / CellSize));
            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new Cell { x = key.x, z = key.y, bounds = bounds };
                cells.Add(key, cell);
            }
            else cell.bounds.Encapsulate(bounds);
            cell.nodes.Add(new Node { renderer = renderer, bounds = bounds, wasActive = renderer.gameObject.activeInHierarchy });
            fullBounds.Encapsulate(bounds);
        }
        var ordered = cells.Values.OrderBy(cell => cell.x).ThenBy(cell => cell.z).ToArray();

        string backupFolder = Path.GetFullPath(Path.Combine(ReviewFolder, "streaming-backup"));
        Directory.CreateDirectory(backupFolder);
        string backup = Path.Combine(backupFolder, "TestePersonagem-before-sectors-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity");
        File.Copy(OfficialScene, backup, false);
        Directory.CreateDirectory(SectorFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Flatten renderer nodes first: closing one sector must never destroy a nested
        // renderer which belongs to a sector that has not been exported yet.
        foreach (var node in ordered.SelectMany(cell => cell.nodes))
        {
            node.renderer.transform.SetParent(null, true);
            if (!node.wasActive && node.renderer.gameObject.activeSelf) node.renderer.gameObject.SetActive(false);
        }

        var metadata = new List<CitySectorStreaming.Sector>();
        var reports = new List<SectorReport>();
        foreach (var cell in ordered)
        {
            var sectorScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(official);
                var sectorRoot = new GameObject("Setor " + cell.x + " " + cell.z);
                SceneManager.MoveGameObjectToScene(sectorRoot, sectorScene);
                foreach (var node in cell.nodes)
                {
                    var geometry = node.renderer.gameObject;
                    geometry.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(geometry, sectorScene);
                    geometry.transform.SetParent(sectorRoot.transform, true);
                    // An inactive ancestor must not accidentally activate scenery in a new scene.
                    if (!node.wasActive && geometry.activeSelf) geometry.SetActive(false);
                }
                if (!EditorSceneManager.SaveScene(sectorScene, cell.ScenePath))
                    throw new IOException("Nao foi possivel salvar o setor: " + cell.ScenePath);
            }
            finally
            {
                SceneManager.SetActiveScene(official);
                if (sectorScene.IsValid() && sectorScene.isLoaded) EditorSceneManager.CloseScene(sectorScene, true);
            }
            // Runtime uses build scene names; exported paths stay in the manifest/build settings.
            metadata.Add(new CitySectorStreaming.Sector { scene = cell.SceneName, bounds = cell.bounds });
            reports.Add(new SectorReport { scene = cell.SceneName, scenePath = cell.ScenePath,
                cellX = cell.x, cellZ = cell.z, renderers = cell.nodes.Count, bounds = cell.bounds });
        }

        if (city.GetComponentsInChildren<MeshRenderer>(true).Length != 0)
            throw new InvalidOperationException("A divisao deixou renderers na cena principal. Consulte o backup antes de reconstruir.");
        var streaming = existing ? existing : city.AddComponent<CitySectorStreaming>();
        streaming.sectors = metadata.ToArray();
        streaming.target = player.transform;
        streaming.mobileLoadDistance = 125;
        streaming.desktopLoadDistance = 190;
        EditorUtility.SetDirty(streaming);
        EditorSceneManager.MarkSceneDirty(official);
        if (!EditorSceneManager.SaveScene(official, OfficialScene))
            throw new IOException("Nao foi possivel salvar a cidade oficial com os metadados de streaming.");
        EditorBuildSettings.scenes = new[] { BootScene, OfficialScene }.Concat(ordered.Select(cell => cell.ScenePath))
            .Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
        AssetDatabase.SaveAssets();
        SceneManager.SetActiveScene(official);

        var report = new SplitReport
        {
            officialScene = OfficialScene, backup = backup, scenePrefix = SectorPrefix, cellSize = CellSize,
            sectorCount = ordered.Length, totalRenderers = renderers.Length,
            minObjectsPerSector = ordered.Min(cell => cell.nodes.Count), maxObjectsPerSector = ordered.Max(cell => cell.nodes.Count),
            fullBounds = fullBounds, sectors = reports.ToArray()
        };
        File.WriteAllText(Path.Combine(ReviewFolder, "streaming-sectors.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GTA_CITY_SECTORS_OK sectors=" + ordered.Length + " renderers=" + renderers.Length +
            " minObjects=" + report.minObjectsPerSector + " maxObjects=" + report.maxObjectsPerSector + " backup=" + backup);
    }

    static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
