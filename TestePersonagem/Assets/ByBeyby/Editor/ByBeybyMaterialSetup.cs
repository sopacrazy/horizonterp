using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ByBeyby
{
    /// <summary>Updates only this character's five materials for the active render pipeline.</summary>
    public static class ByBeybyMaterialSetup
    {
        private const string AssetRoot = "Assets/ByBeyby";
        private static readonly string[] TextureNames =
        {
            "beyby_bb", "beyby_ccc", "beyby_ohhg", "beyby_facse", "beyby_occ"
        };

        [MenuItem("Tools/ByBeyby/Atualizar materiais")]
        public static void Apply()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineType = pipeline == null ? "" : pipeline.GetType().FullName;
            bool universal = pipelineType.IndexOf("UniversalRenderPipelineAsset", StringComparison.Ordinal) >= 0;
            bool highDefinition = pipelineType.IndexOf("HDRenderPipelineAsset", StringComparison.Ordinal) >= 0;
            if (pipeline != null && !universal && !highDefinition)
            {
                Debug.LogWarning("ByBeyby: o render pipeline ativo não é Built-in, URP ou HDRP. Os materiais não foram alterados.");
                return;
            }

            string shaderName = universal ? "Universal Render Pipeline/Lit" : highDefinition ? "HDRP/Lit" : "Standard";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning("ByBeyby: shader indisponível: " + shaderName + ". Verifique a instalação do render pipeline e execute o menu novamente.");
                return;
            }

            int updated = 0;
            foreach (string name in TextureNames)
            {
                Material material = FindOwnedAsset<Material>(name, "Materials", "mat");
                Texture2D texture = FindOwnedAsset<Texture2D>(name, "Textures", "png");
                if (material == null || texture == null)
                {
                    Debug.LogWarning("ByBeyby: material ou textura ausente dentro do pacote: " + name);
                    continue;
                }

                bool alphaClip = name == "beyby_ccc" || name == "beyby_occ";
                material.shader = shader;
                material.shaderKeywords = new string[0];
                SetTexture(material, "_MainTex", texture);
                SetTexture(material, "_BaseMap", texture);
                SetTexture(material, "_BaseColorMap", texture);
                SetColor(material, "_Color", Color.white);
                SetColor(material, "_BaseColor", Color.white);
                SetFloat(material, "_Metallic", 0f);
                SetFloat(material, "_Smoothness", 0.25f);
                SetFloat(material, "_Glossiness", 0.25f);
                SetFloat(material, "_GlossMapScale", 0.25f);
                SetFloat(material, "_SmoothnessTextureChannel", 0f);

                SetFloat(material, "_Mode", alphaClip ? 1f : 0f);
                SetFloat(material, "_Surface", 0f);
                SetFloat(material, "_SurfaceType", 0f);
                SetFloat(material, "_Blend", 0f);
                SetFloat(material, "_BlendMode", 0f);
                SetFloat(material, "_WorkflowMode", 1f);
                SetFloat(material, "_AlphaClip", alphaClip ? 1f : 0f);
                SetFloat(material, "_AlphaCutoffEnable", alphaClip ? 1f : 0f);
                SetFloat(material, "_Cutoff", 0.5f);
                SetFloat(material, "_AlphaCutoff", 0.5f);
                SetFloat(material, "_AlphaCutoffShadow", 0.5f);
                SetFloat(material, "_UseShadowThreshold", 0f);
                SetFloat(material, "_SrcBlend", (float)BlendMode.One);
                SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
                SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
                SetFloat(material, "_DstBlendAlpha", (float)BlendMode.Zero);
                SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
                SetFloat(material, "_AlphaDstBlend", (float)BlendMode.Zero);
                SetFloat(material, "_ZWrite", 1f);
                SetFloat(material, "_ZWriteControl", 0f);
                SetFloat(material, "_Cull", (float)CullMode.Back);
                SetFloat(material, "_CullMode", (float)CullMode.Back);
                SetFloat(material, "_CullModeForward", (float)CullMode.Back);
                SetFloat(material, "_DoubleSidedEnable", 0f);
                material.SetShaderPassEnabled("ShadowCaster", true);
                material.SetShaderPassEnabled("DepthOnly", true);
                if (alphaClip) material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
                material.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

                if (highDefinition) RefreshHighDefinitionKeywords(material);
                EditorUtility.SetDirty(material);
                updated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ByBeyby: " + updated + "/5 materiais atualizados para " + shaderName + ".");
        }

        private static T FindOwnedAsset<T>(string name, string folder, string extension) where T : UnityEngine.Object
        {
            string expectedPath = AssetRoot + "/" + folder + "/" + name + "." + extension;
            T asset = AssetDatabase.LoadAssetAtPath<T>(expectedPath);
            if (asset != null || !AssetDatabase.IsValidFolder(AssetRoot)) return asset;
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { AssetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal)
                    && string.Equals(Path.GetExtension(path), "." + extension, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property)) material.SetColor(property, value);
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (!material.HasProperty(property)) return;
            material.SetTexture(property, texture);
            material.SetTextureScale(property, Vector2.one);
            material.SetTextureOffset(property, Vector2.zero);
        }

        private static void RefreshHighDefinitionKeywords(Material material)
        {
            // Keep the package independent from optional HDRP editor assemblies.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("UnityEditor.Rendering.HighDefinition.HDShaderUtils", false);
                if (type == null) continue;
                MethodInfo reset = type.GetMethod("ResetMaterialKeywords", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Material) }, null);
                if (reset == null) continue;
                try { reset.Invoke(null, new object[] { material }); }
                catch (Exception exception)
                {
                    Debug.LogWarning("ByBeyby: não foi possível atualizar os keywords HDRP de " + material.name + ": " + exception.GetBaseException().Message);
                }
                return;
            }
        }
    }
}
