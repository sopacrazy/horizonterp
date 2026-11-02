using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Explicit opt-in import: never reimport the city or install a global postprocessor.
public static class RifleAnimationImport
{
    public const string Folder = "Assets/Resources/RifleAnimations/";
    public static readonly string[] Names = { "Idle", "Walk", "Run", "Shoot" };

    [MenuItem("Horizonte/Testes locais/Preparar animações do rifle")]
    public static void Prepare()
    {
        foreach (string name in Names)
        {
            string path = Folder + name + ".fbx";
            string authoredFile=name=="Idle"?"mirando_parado.fbx":name=="Run"?"correndo_armado.fbx":name=="Shoot"?"atirando.fbx":null;
            if(authoredFile!=null)
            {
                string sourceFile=Path.GetFullPath("../personagem/"+authoredFile);
                if(File.Exists(sourceFile)&&(!File.Exists(path)||!File.ReadAllBytes(sourceFile).SequenceEqual(File.ReadAllBytes(path))))
                    File.Copy(sourceFile,path,true);
            }
            if (!File.Exists(path)) throw new FileNotFoundException("Animação do rifle ausente", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("FBX não reconhecido: " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            // The supplied clips use the existing ByBeyby source skeleton.
            // Keep its calibrated rest pose instead of inferring a T-pose from
            // a first animation frame with raised arms or a running body lean.
            if(name!="Walk")
            {
                var reference=AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/Animations/Shooting.fbx") as ModelImporter;
                if(!reference)throw new InvalidOperationException("Esqueleto de referência ByBeyby ausente");
                importer.humanDescription=reference.humanDescription;
            }
            var source = importer.defaultClipAnimations.FirstOrDefault();
            if (source == null) throw new InvalidOperationException("FBX sem take: " + path);
            var clip = new ModelImporterClipAnimation
            {
                name = "Rifle" + name,
                takeName = source.takeName,
                firstFrame = source.firstFrame,
                lastFrame = source.lastFrame,
                loopTime = name!="Shoot",
                loopPose = name!="Shoot",
                lockRootRotation = true,
                lockRootHeightY = true,
                lockRootPositionXZ = true,
                keepOriginalOrientation = false,
                keepOriginalPositionXZ = false,
                keepOriginalPositionY = true,
                heightFromFeet = name!="Shoot"
            };
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman)
            {
                if (!prefab) throw new InvalidOperationException("Modelo do rifle ausente: " + path);
                var description = importer.humanDescription;
                // Walk is the retained Unlicense pack clip with its own rig.
                // Authored ByBeyby clips must use the reference mapping above.
                if(name!="Walk")throw new InvalidOperationException("Avatar ByBeyby inválido no clip "+name);
                description.human = Bones().Select(pair => new HumanBone
                {
                    humanName = pair.Item1,
                    boneName = pair.Item2,
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray();
                description.skeleton = prefab.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                }).ToArray();
                description.upperArmTwist = .5f; description.lowerArmTwist = .5f;
                description.upperLegTwist = .5f; description.lowerLegTwist = .5f;
                description.armStretch = .05f; description.legStretch = .05f;
                importer.humanDescription = description;
                importer.SaveAndReimport();
            }
            avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "Rifle" + name);
            if (!avatar || !avatar.isValid || !avatar.isHuman || !imported || !imported.humanMotion)
                throw new InvalidOperationException("Retarget Humanoid inválido: " + path);
            Debug.Log("RIFLE_CLIP_READY " + name + " seconds=" + imported.length + " humanoid=" + imported.humanMotion);
        }
        AssetDatabase.SaveAssets();
    }

    static System.Collections.Generic.IEnumerable<Tuple<string, string>> Bones()
    {
        yield return Tuple.Create("Hips", "Pelvis");
        yield return Tuple.Create("Spine", "Spine1");
        yield return Tuple.Create("Chest", "Spine2");
        yield return Tuple.Create("UpperChest", "Spine3");
        yield return Tuple.Create("Neck", "Neck_Base");
        yield return Tuple.Create("Head", "Head");
        foreach (var side in new[] { Tuple.Create("Left", "L_"), Tuple.Create("Right", "R_") })
        {
            yield return Tuple.Create(side.Item1 + "Shoulder", side.Item2 + "Clavicle");
            yield return Tuple.Create(side.Item1 + "UpperArm", side.Item2 + "Arm");
            yield return Tuple.Create(side.Item1 + "LowerArm", side.Item2 + "Forearm");
            yield return Tuple.Create(side.Item1 + "Hand", side.Item2 + "Hand");
            yield return Tuple.Create(side.Item1 + "UpperLeg", side.Item2 + "Thigh");
            yield return Tuple.Create(side.Item1 + "LowerLeg", side.Item2 + "Shin");
            yield return Tuple.Create(side.Item1 + "Foot", side.Item2 + "Ankle");
            yield return Tuple.Create(side.Item1 + "Toes", side.Item2 + "Toes");
            foreach (var finger in new[] { Tuple.Create("Thumb", "Thumb"), Tuple.Create("Index", "Index"),
                Tuple.Create("Middle", "Middle"), Tuple.Create("Ring", "Ring"), Tuple.Create("Little", "Pinky") })
            {
                yield return Tuple.Create(side.Item1 + finger.Item1 + "Proximal", side.Item2 + finger.Item2 + "1");
                yield return Tuple.Create(side.Item1 + finger.Item1 + "Intermediate", side.Item2 + finger.Item2 + "2");
                yield return Tuple.Create(side.Item1 + finger.Item1 + "Distal", side.Item2 + finger.Item2 + "3");
            }
        }
    }
}
