using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Run while the entire example is loaded, before its geometry is exported into sectors.
public static class CityExampleValidation
{
    const float SampleSpacing = 2f;
    const float MaximumFloorDifference = .22f;
    const int MaximumRecordedErrors = 80;

    [Serializable]
    sealed class ValidationReport
    {
        public bool passed;
        public string capturedUtc;
        public int buildings, renderers, colliders, meshes, routes, routeSegments;
        public int floorSamples, clearCapsuleSamples, graphConnectedComponents;
        public int missingMaterials, invalidShaders, issueCount;
        public float routeLengthMetres;
        public List<string> checks = new List<string>();
        public List<string> errors = new List<string>();
    }

    struct Segment
    {
        public Vector3 a, b;
        public Segment(Vector3 from, Vector3 to) { a = from; b = to; }
    }

    public static string Validate(GameObject root, CityExamplePreview.Route[] routes,
        Bounds[] buildingFootprints, Bounds[] roadReserves, string reportDirectory)
    {
        if (!root) throw new ArgumentNullException(nameof(root));
        if (string.IsNullOrWhiteSpace(reportDirectory)) throw new ArgumentException("Pasta do relatório ausente.", nameof(reportDirectory));
        var report = new ValidationReport { capturedUtc = DateTime.UtcNow.ToString("O") };
        routes = routes ?? Array.Empty<CityExamplePreview.Route>();
        buildingFootprints = buildingFootprints ?? Array.Empty<Bounds>();
        roadReserves = roadReserves ?? Array.Empty<Bounds>();
        report.buildings = buildingFootprints.Length;
        report.routes = routes.Length;
        Physics.SyncTransforms();

        ValidateGeometry(root, report);
        ValidatePlots(buildingFootprints, roadReserves, report);
        ValidateRoutes(routes, report);
        ValidateConnectivity(routes, report);
        report.passed = report.issueCount == 0;
        report.checks.Add("Amostragem do piso e da cápsula a cada no máximo 2 m, em todas as rotas declaradas.");
        report.checks.Add("Teste da conectividade das vias e acessos declarados, incluindo cruzamentos no interior dos segmentos.");
        report.checks.Add("Teste dos lotes contra outros lotes e contra a faixa reservada às ruas e calçadas.");
        report.checks.Add("Esta verificação cobre os percursos declarados; não certifica ausência de todo possível defeito fora deles.");
        Directory.CreateDirectory(reportDirectory);
        string jsonPath = Path.Combine(reportDirectory, "geometry-validation.json");
        string textPath = Path.Combine(reportDirectory, "geometry-validation.txt");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true));
        var lines = new List<string>
        {
            "Cidade Horizonte · exemplo separado · verificação geométrica",
            "Passed=" + report.passed,
            "Buildings=" + report.buildings + "; renderers=" + report.renderers + "; colliders=" + report.colliders,
            "Routes=" + report.routes + "; segments=" + report.routeSegments + "; connected components=" + report.graphConnectedComponents,
            "Route length=" + report.routeLengthMetres.ToString("F2", CultureInfo.InvariantCulture) + " m",
            "Floor samples=" + report.floorSamples + "; clear capsule samples=" + report.clearCapsuleSamples,
            "Missing material slots=" + report.missingMaterials + "; invalid shaders=" + report.invalidShaders,
            "Issues=" + report.issueCount
        };
        lines.AddRange(report.checks);
        lines.AddRange(report.errors.Select(error => "ERROR: " + error));
        File.WriteAllLines(textPath, lines);
        if (!report.passed)
            throw new InvalidOperationException("A cidade de exemplo falhou na validação geométrica: " +
                report.issueCount + " problema(s). " + string.Join(" | ", report.errors.Take(5)) +
                " Consulte " + Path.GetFullPath(textPath));
        Debug.Log("CITY_EXAMPLE_GEOMETRY_OK buildings=" + report.buildings + " samples=" + report.floorSamples +
            " connectedComponents=" + report.graphConnectedComponents);
        return Path.GetFullPath(textPath);
    }

    static void ValidateGeometry(GameObject root, ValidationReport report)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        report.renderers = renderers.Length;
        report.colliders = root.GetComponentsInChildren<Collider>(true).Length;
        if (report.renderers == 0) Error(report, "Cidade sem geometria visual.");
        if (report.colliders == 0) Error(report, "Cidade sem colisores.");
        var inspectedShaders = new HashSet<Shader>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!Finite(transform.position) || !Finite(transform.localScale) || !Finite(transform.rotation))
                Error(report, "Transform inválido: " + transform.name);
            if (transform.localScale.x <= 0 || transform.localScale.y <= 0 || transform.localScale.z <= 0)
                Error(report, "Escala nula ou invertida: " + transform.name);
        }
        foreach (var renderer in renderers)
        {
            if (!Finite(renderer.bounds.center) || !Finite(renderer.bounds.extents))
                Error(report, "Limites visuais inválidos: " + renderer.name);
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) { report.missingMaterials++; Error(report, "Objeto sem material: " + renderer.name); }
            foreach (var material in materials)
            {
                if (!material) { report.missingMaterials++; Error(report, "Slot de material ausente: " + renderer.name); continue; }
                if (!material.shader || material.shader.name.Contains("InternalError"))
                { report.invalidShaders++; Error(report, "Shader ausente/inválido: " + renderer.name); continue; }
                if (inspectedShaders.Add(material.shader) && ShaderUtil.ShaderHasError(material.shader))
                { report.invalidShaders++; Error(report, "Erro de compilação no shader: " + material.shader.name); }
            }
        }
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.GetComponent<TextMesh>()) continue;
            if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0)
                Error(report, "Malha vazia: " + filter.name);
            else report.meshes++;
        }
    }

    static void ValidatePlots(Bounds[] buildings, Bounds[] roads, ValidationReport report)
    {
        for (int i = 0; i < buildings.Length; i++)
        {
            var building = buildings[i];
            if (!Finite(building.center) || !Finite(building.extents) || building.size.x <= 0 || building.size.z <= 0)
            { Error(report, "Lote inválido: " + i); continue; }
            if (Mathf.Abs(building.min.y) > .051f)
                Error(report, "Base do lote " + i + " fora do nível do solo: " + building.min.y.ToString("F3"));
            for (int j = i + 1; j < buildings.Length; j++)
                if (OverlapXZ(building, buildings[j], .02f))
                    Error(report, "Construções sobrepostas: lotes " + i + " e " + j + " em " + building.center + "/" + buildings[j].center);
            for (int j = 0; j < roads.Length; j++)
                if (OverlapXZ(building, roads[j], .02f))
                    Error(report, "Construção invade rua/calçada: lote " + i + " e faixa " + j + " em " + building.center);
        }
    }

    static bool OverlapXZ(Bounds a, Bounds b, float tolerance)
    {
        return Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > tolerance &&
            Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > tolerance;
    }

    static void ValidateRoutes(CityExamplePreview.Route[] routes, ValidationReport report)
    {
        if (routes.Length == 0) Error(report, "Não há percursos declarados para verificar acesso.");
        foreach (var route in routes)
        {
            if (route == null || route.points == null || route.points.Length < 2)
            { Error(report, "Percurso vazio: " + (route == null ? "(null)" : route.name)); continue; }
            for (int i = 1; i < route.points.Length; i++)
            {
                Vector3 a = route.points[i - 1], b = route.points[i];
                if (!Finite(a) || !Finite(b)) { Error(report, "Ponto inválido em " + route.name); continue; }
                float length = Vector3.Distance(a, b);
                if (length < .001f) continue;
                report.routeSegments++;
                report.routeLengthMetres += length;
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
                float previousFloor = 0;
                bool previousValid = false;
                for (int sample = 0; sample <= steps; sample++)
                {
                    Vector3 point = Vector3.Lerp(a, b, sample / (float)steps);
                    string location = route.name + " segmento " + i + " em " + point.ToString("F2");
                    if (!Floor(point, out var hit))
                    { Error(report, "Piso ausente/desnivelado: " + location); previousValid = false; continue; }
                    report.floorSamples++;
                    if (previousValid && Mathf.Abs(previousFloor - hit.point.y) > MaximumFloorDifference)
                        Error(report, "Degrau maior que 22 cm: " + location);
                    previousFloor = hit.point.y;
                    previousValid = true;
                    Vector3 foot = new Vector3(point.x, hit.point.y, point.z);
                    var collisions = Physics.OverlapCapsule(foot + Vector3.up * .35f,
                        foot + Vector3.up * 1.53f, .27f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    if (collisions.Length > 0)
                        Error(report, "Passagem obstruída: " + location + " por " + string.Join(", ", collisions.Take(3).Select(c => c.name)));
                    else report.clearCapsuleSamples++;
                }
            }
        }
    }

    static bool Floor(Vector3 point, out RaycastHit floor)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 2.8f, Vector3.down, 5f,
            ~(1 << 2), QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        floor = default;
        foreach (var hit in hits)
        {
            float difference = Mathf.Abs(hit.point.y - point.y);
            if (hit.normal.y < .85f || difference > MaximumFloorDifference || difference >= best) continue;
            best = difference;
            floor = hit;
        }
        return !float.IsPositiveInfinity(best);
    }

    static void ValidateConnectivity(CityExamplePreview.Route[] routes, ValidationReport report)
    {
        // Every declared road/access is a graph vertex. Two paths connect when any of their
        // segments meet, including a branch joining the middle of a long avenue segment.
        var selected = routes.Where(route => route != null && route.points != null && route.points.Length >= 2 &&
            (route.name != null && (route.name.StartsWith("Via/", StringComparison.Ordinal) ||
                route.name.StartsWith("Acesso/", StringComparison.Ordinal)))).ToArray();
        if (selected.Length == 0)
            selected = routes.Where(route => route != null && route.points != null && route.points.Length >= 2).ToArray();
        if (selected.Length == 0) { report.graphConnectedComponents = 0; return; }
        var parents = Enumerable.Range(0, selected.Length).ToArray();
        for (int i = 0; i < selected.Length; i++)
            for (int j = i + 1; j < selected.Length; j++)
                if (RoutesMeet(selected[i].points, selected[j].points))
                    parents[Find(parents, j)] = Find(parents, i);
        var groups = Enumerable.Range(0, selected.Length).GroupBy(i => Find(parents, i)).ToArray();
        report.graphConnectedComponents = groups.Length;
        if (groups.Length != 1)
            Error(report, "Vias/acessos desconectados: " + groups.Length + " grupos: " + string.Join(" | ",
                groups.Select(group => string.Join(", ", group.Take(5).Select(i => selected[i].name)))));
    }

    static bool RoutesMeet(Vector3[] first, Vector3[] second)
    {
        for (int i = 1; i < first.Length; i++)
            for (int j = 1; j < second.Length; j++)
                if (SegmentsMeet(new Segment(first[i - 1], first[i]), new Segment(second[j - 1], second[j]))) return true;
        return false;
    }

    static bool SegmentsMeet(Segment first, Segment second)
    {
        var a = new Vector2(first.a.x, first.a.z);
        var b = new Vector2(first.b.x, first.b.z);
        var c = new Vector2(second.a.x, second.a.z);
        var d = new Vector2(second.b.x, second.b.z);
        var ab = b - a;
        var cd = d - c;
        float denominator = Cross(ab, cd);
        if (Mathf.Abs(denominator) > .00001f)
        {
            float t = Cross(c - a, cd) / denominator;
            float u = Cross(c - a, ab) / denominator;
            float extensionA = .1f / Mathf.Max(ab.magnitude, .1f);
            float extensionB = .1f / Mathf.Max(cd.magnitude, .1f);
            if (t >= -extensionA && t <= 1 + extensionA && u >= -extensionB && u <= 1 + extensionB)
                return Mathf.Abs(Mathf.Lerp(first.a.y, first.b.y, t) - Mathf.Lerp(second.a.y, second.b.y, u)) <= MaximumFloorDifference;
            return false;
        }
        return EndpointNear(first.a, second) || EndpointNear(first.b, second) ||
            EndpointNear(second.a, first) || EndpointNear(second.b, first);
    }

    static bool EndpointNear(Vector3 point, Segment segment)
    {
        var from = new Vector2(segment.a.x, segment.a.z);
        var delta = new Vector2(segment.b.x - segment.a.x, segment.b.z - segment.a.z);
        var target = new Vector2(point.x, point.z);
        float t = delta.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot(target - from, delta) / delta.sqrMagnitude);
        return Vector2.Distance(target, from + delta * t) <= .1f &&
            Mathf.Abs(point.y - Mathf.Lerp(segment.a.y, segment.b.y, t)) <= MaximumFloorDifference;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static int Find(int[] parents, int index)
    {
        while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
        return index;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    static void Error(ValidationReport report, string message)
    {
        report.issueCount++;
        if (report.errors.Count < MaximumRecordedErrors) report.errors.Add(message);
    }
}
