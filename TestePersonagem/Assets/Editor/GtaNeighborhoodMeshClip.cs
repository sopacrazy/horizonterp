using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Clips cloned static road geometry in world space. Source meshes/assets are never modified.
public static class GtaNeighborhoodMeshClip
{
    const float PositionEpsilon = .00001f;

    struct Vertex
    {
        public Vector3 position, normal;
        public Vector4 tangent;
        public Vector2 uv, uv2;
        public Color color;

        public static Vertex Lerp(Vertex a, Vertex b, float t)
        {
            return new Vertex
            {
                position = Vector3.LerpUnclamped(a.position, b.position, t),
                normal = Vector3.LerpUnclamped(a.normal, b.normal, t),
                tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t),
                uv = Vector2.LerpUnclamped(a.uv, b.uv, t),
                uv2 = Vector2.LerpUnclamped(a.uv2, b.uv2, t),
                color = Color.LerpUnclamped(a.color, b.color, t)
            };
        }
    }

    public static Mesh Clip(Mesh mesh, Transform source, Rect worldRect, string name)
    {
        if (!mesh || mesh.vertexCount == 0 || mesh.subMeshCount == 0) return null;
        if (!source) throw new ArgumentNullException(nameof(source));
        if (!Finite(worldRect.xMin) || !Finite(worldRect.xMax) || !Finite(worldRect.yMin) ||
            !Finite(worldRect.yMax) || worldRect.width <= 0 || worldRect.height <= 0)
            throw new ArgumentException("O recorte precisa de um retângulo finito e de área positiva.", nameof(worldRect));
        if (!mesh.isReadable)
            throw new ArgumentException("A malha de origem precisa permitir leitura: " + mesh.name, nameof(mesh));

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Vector2[] uv = mesh.uv;
        Vector2[] uv2 = mesh.uv2;
        Color[] colors = mesh.colors;
        bool hasNormals = normals.Length == positions.Length;
        bool hasTangents = tangents.Length == positions.Length;
        bool hasUv = uv.Length == positions.Length;
        bool hasUv2 = uv2.Length == positions.Length;
        bool hasColors = colors.Length == positions.Length;
        Matrix4x4 world = source.localToWorldMatrix;
        Matrix4x4 normalMatrix = world.inverse.transpose;
        float determinant = world.determinant;
        if (!Finite(determinant) || Mathf.Abs(determinant) < .0000000001f)
            throw new ArgumentException("Transform de origem singular ou inválido: " + source.name, nameof(source));
        float tangentHandedness = determinant < 0 ? -1 : 1;

        var input = new Vertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var vertex = new Vertex { position = world.MultiplyPoint3x4(positions[i]) };
            if (!Finite(vertex.position.x) || !Finite(vertex.position.y) || !Finite(vertex.position.z))
                throw new ArgumentException("Vértice de origem não finito: " + mesh.name, nameof(mesh));
            if (hasNormals) vertex.normal = normalMatrix.MultiplyVector(normals[i]).normalized;
            if (hasTangents)
            {
                var direction = world.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                vertex.tangent = new Vector4(direction.x, direction.y, direction.z, tangents[i].w * tangentHandedness);
            }
            if (hasUv) vertex.uv = uv[i];
            if (hasUv2) vertex.uv2 = uv2[i];
            if (hasColors) vertex.color = colors[i];
            input[i] = vertex;
        }

        var outputPositions = new List<Vector3>();
        var outputNormals = new List<Vector3>();
        var outputTangents = new List<Vector4>();
        var outputUv = new List<Vector2>();
        var outputUv2 = new List<Vector2>();
        var outputColors = new List<Color>();
        var submeshes = new List<int>[mesh.subMeshCount];
        var polygon = new List<Vertex>(8);
        var clipped = new List<Vertex>(8);

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                throw new ArgumentException("O recorte suporta apenas submalhas triangulares: " + mesh.name + " / " + submesh, nameof(mesh));
            int[] indices = mesh.GetTriangles(submesh);
            var outputIndices = new List<int>();
            submeshes[submesh] = outputIndices;
            for (int triangle = 0; triangle + 2 < indices.Length; triangle += 3)
            {
                Vertex a = input[indices[triangle]], b = input[indices[triangle + 1]], c = input[indices[triangle + 2]];
                if (Mathf.Max(a.position.x, Mathf.Max(b.position.x, c.position.x)) < worldRect.xMin ||
                    Mathf.Min(a.position.x, Mathf.Min(b.position.x, c.position.x)) > worldRect.xMax ||
                    Mathf.Max(a.position.z, Mathf.Max(b.position.z, c.position.z)) < worldRect.yMin ||
                    Mathf.Min(a.position.z, Mathf.Min(b.position.z, c.position.z)) > worldRect.yMax) continue;

                polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                for (int plane = 0; plane < 4 && polygon.Count > 0; plane++)
                {
                    ClipPlane(polygon, clipped, worldRect, plane);
                    var swap = polygon; polygon = clipped; clipped = swap;
                }
                if (polygon.Count < 3) continue;
                // Preserve polygon order. A mirrored source also needs its original render
                // winding compensation baked in because the output transform is identity.
                for (int fan = 1; fan + 1 < polygon.Count; fan++)
                {
                    var first = polygon[0]; var second = polygon[fan]; var third = polygon[fan + 1];
                    if (Vector3.Cross(second.position - first.position, third.position - first.position).sqrMagnitude < .0000000001f) continue;
                    if (determinant < 0) { var swap = second; second = third; third = swap; }
                    int start = outputPositions.Count;
                    Append(first); Append(second); Append(third);
                    outputIndices.Add(start); outputIndices.Add(start + 1); outputIndices.Add(start + 2);
                }
            }
        }
        if (outputPositions.Count == 0) return null;

        var result = new Mesh
        {
            name = string.IsNullOrWhiteSpace(name) ? mesh.name + "_Recorte" : name,
            indexFormat = outputPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        result.SetVertices(outputPositions);
        if (hasNormals) result.SetNormals(outputNormals);
        if (hasTangents) result.SetTangents(outputTangents);
        if (hasUv) result.SetUVs(0, outputUv);
        if (hasUv2) result.SetUVs(1, outputUv2);
        if (hasColors) result.SetColors(outputColors);
        result.subMeshCount = mesh.subMeshCount;
        for (int submesh = 0; submesh < submeshes.Length; submesh++)
            result.SetTriangles(submeshes[submesh], submesh, false);
        if (!hasNormals) result.RecalculateNormals();
        result.RecalculateBounds();
        return result;

        void Append(Vertex vertex)
        {
            outputPositions.Add(vertex.position);
            Vector3 normal = vertex.normal.normalized;
            if (hasNormals) outputNormals.Add(normal);
            if (hasTangents)
            {
                Vector3 direction = new Vector3(vertex.tangent.x, vertex.tangent.y, vertex.tangent.z);
                if (hasNormals) direction -= normal * Vector3.Dot(normal, direction);
                if (direction.sqrMagnitude < .0000000001f)
                    direction = hasNormals ? Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right) : Vector3.right;
                direction.Normalize();
                outputTangents.Add(new Vector4(direction.x, direction.y, direction.z, vertex.tangent.w < 0 ? -1 : 1));
            }
            if (hasUv) outputUv.Add(vertex.uv);
            if (hasUv2) outputUv2.Add(vertex.uv2);
            if (hasColors) outputColors.Add(vertex.color);
        }
    }

    static void ClipPlane(List<Vertex> input, List<Vertex> output, Rect rectangle, int plane)
    {
        output.Clear();
        if (input.Count == 0) return;
        Vertex previous = input[input.Count - 1];
        float previousDistance = Distance(previous.position, rectangle, plane);
        bool previousInside = previousDistance >= 0;
        foreach (var current in input)
        {
            float currentDistance = Distance(current.position, rectangle, plane);
            bool currentInside = currentDistance >= 0;
            if (previousInside != currentInside)
            {
                float denominator = previousDistance - currentDistance;
                float t = Mathf.Abs(denominator) < .000000000001f ? 0 : Mathf.Clamp01(previousDistance / denominator);
                Vertex intersection = Vertex.Lerp(previous, current, t);
                // Assign the clipped coordinate exactly, avoiding tiny gaps between adjacent cuts.
                if (plane == 0) intersection.position.x = rectangle.xMin;
                else if (plane == 1) intersection.position.x = rectangle.xMax;
                else if (plane == 2) intersection.position.z = rectangle.yMin;
                else intersection.position.z = rectangle.yMax;
                AddDistinct(output, intersection);
            }
            if (currentInside) AddDistinct(output, current);
            previous = current; previousDistance = currentDistance; previousInside = currentInside;
        }
        if (output.Count > 1 && (output[0].position - output[output.Count - 1].position).sqrMagnitude <= PositionEpsilon * PositionEpsilon)
            output.RemoveAt(output.Count - 1);
    }

    static void AddDistinct(List<Vertex> vertices, Vertex vertex)
    {
        if (vertices.Count == 0 || (vertices[vertices.Count - 1].position - vertex.position).sqrMagnitude > PositionEpsilon * PositionEpsilon)
            vertices.Add(vertex);
    }

    static float Distance(Vector3 position, Rect rectangle, int plane)
    {
        switch (plane)
        {
            case 0: return position.x - rectangle.xMin;
            case 1: return rectangle.xMax - position.x;
            case 2: return position.z - rectangle.yMin;
            default: return rectangle.yMax - position.z;
        }
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
