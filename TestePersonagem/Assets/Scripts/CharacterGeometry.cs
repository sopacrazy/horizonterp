using System;
using UnityEngine;

public static class CharacterGeometry
{
    // Skin directly into world space. This applies the skeleton's scale exactly once,
    // independent of BakeMesh's scale handling and the renderer's culling bounds.
    public static Bounds GetWorldBounds(SkinnedMeshRenderer renderer)
    {
        if (!renderer) throw new ArgumentNullException(nameof(renderer));
        Mesh mesh = renderer.sharedMesh;
        if (!mesh) throw new InvalidOperationException(renderer.name + ": malha ausente.");
        if (!mesh.isReadable) throw new InvalidOperationException(renderer.name + ": a malha precisa de Read/Write habilitado.");

        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bindPoses = mesh.bindposes;
        Transform[] bones = renderer.bones;
        if (vertices.Length == 0 || weights.Length != vertices.Length)
            throw new InvalidOperationException(renderer.name + ": vértices e pesos de skinning incompletos.");
        if (bindPoses.Length == 0 || bones.Length < bindPoses.Length)
            throw new InvalidOperationException(renderer.name + ": ossos e bind poses incompatíveis.");

        var worldSkinMatrices = new Matrix4x4[bindPoses.Length];
        for (int i = 0; i < bindPoses.Length; i++)
        {
            if (!bones[i]) throw new InvalidOperationException(renderer.name + ": osso ausente no índice " + i + ".");
            worldSkinMatrices[i] = bones[i].localToWorldMatrix * bindPoses[i];
        }

        Bounds result = new Bounds();
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight weight = weights[i];
            float total = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
            if (!Finite(total) || Mathf.Abs(total - 1f) > .002f)
                throw new InvalidOperationException(renderer.name + ": pesos não normalizados no vértice " + i + ".");

            Vector3 point = Vector3.zero;
            Accumulate(ref point, vertices[i], weight.boneIndex0, weight.weight0, worldSkinMatrices, renderer.name, i);
            Accumulate(ref point, vertices[i], weight.boneIndex1, weight.weight1, worldSkinMatrices, renderer.name, i);
            Accumulate(ref point, vertices[i], weight.boneIndex2, weight.weight2, worldSkinMatrices, renderer.name, i);
            Accumulate(ref point, vertices[i], weight.boneIndex3, weight.weight3, worldSkinMatrices, renderer.name, i);
            if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                throw new InvalidOperationException(renderer.name + ": posição inválida no vértice " + i + ".");
            if (i == 0) result = new Bounds(point, Vector3.zero);
            else result.Encapsulate(point);
        }
        return result;
    }

    static void Accumulate(ref Vector3 point, Vector3 vertex, int boneIndex, float weight,
        Matrix4x4[] matrices, string rendererName, int vertexIndex)
    {
        if (!Finite(weight) || weight < 0f)
            throw new InvalidOperationException(rendererName + ": peso inválido no vértice " + vertexIndex + ".");
        if (weight == 0f) return;
        if (boneIndex < 0 || boneIndex >= matrices.Length)
            throw new InvalidOperationException(rendererName + ": índice de osso inválido no vértice " + vertexIndex + ".");
        point += matrices[boneIndex].MultiplyPoint3x4(vertex) * weight;
    }

    static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
}
