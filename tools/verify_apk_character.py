"""Check the actual feminine Resources prefab and avatar in a built Unity APK."""
import argparse
import json
import math
from pathlib import Path
import sys
import zipfile


def resource_pointers(environment, name, type_name=None):
    result = []
    for obj in environment.objects:
        if obj.type.name != 'ResourceManager':
            continue
        for resource_name, pointer in obj.read().m_Container:
            if resource_name.lower() == name.lower() and pointer:
                if type_name is None or pointer.type.name == type_name:
                    result.append(pointer)
    return result


def resource_object(environment, name, type_name):
    pointers = resource_pointers(environment, name, type_name)
    assert len(pointers) == 1, 'Expected one packaged ' + type_name + ' resource: ' + name
    return pointers[0].read()


def hierarchy_components(prefab):
    pending = [prefab]
    result = []
    while pending:
        game_object = pending.pop()
        for entry in game_object.m_Component:
            component = entry.component
            result.append((game_object.m_Name, component))
            if component.type.name == 'Transform':
                pending.extend(child.read().m_GameObject.read()
                               for child in component.read().m_Children)
    return result


def humanoid_avatar(pointer, label):
    assert pointer, label + ' Animator has no Avatar; the game falls back to the old character'
    avatar = pointer.read_typetree()
    human = avatar['m_Avatar']['m_Human']['data']
    assert sum(index >= 0 for index in human['m_HumanBoneIndex']) >= 15, label + ' Avatar has no valid humanoid bone mapping'
    mapping = {bone['m_HumanName'].replace(' ', ''): bone['m_BoneName']
               for bone in avatar['m_HumanDescription']['m_Human']}
    required = {'Hips', 'Spine', 'Head'}
    for side in ('Left', 'Right'):
        required.update(side + bone for bone in
                        ('UpperArm', 'LowerArm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot'))
    assert required <= mapping.keys(), label + ' Avatar is missing required humanoid bones: ' + str(sorted(required - mapping.keys()))
    return avatar, mapping


def verify_female_character(environment):
    prefab = resource_object(environment, 'characters/bybeyby', 'GameObject')
    animators, skins = [], []
    for _, component in hierarchy_components(prefab):
        if component.type.name == 'Animator':
            animators.append(component.read())
        elif component.type.name == 'SkinnedMeshRenderer':
            skins.append(component.read())
    assert len(animators) == 1, 'Female prefab must contain one Animator'
    avatar, mapping = humanoid_avatar(animators[0].m_Avatar, 'Female')
    assert skins, 'Female prefab has no skinned mesh'
    materials = 0
    for skin in skins:
        assert skin.m_Mesh and skin.m_Bones and all(skin.m_Bones), 'Female mesh or bone references are unresolved'
        skin.m_Mesh.read()  # Also reject pointers to an asset that was not packaged.
        for pointer in skin.m_Materials:
            assert pointer, 'Female material reference is unresolved'
            material = pointer.read()
            textures = dict(material.m_SavedProperties.m_TexEnvs)
            color = textures.get('_MainTex') or textures.get('_BaseMap')
            assert color is not None and color.m_Texture, 'Female material has no color texture: ' + material.m_Name
            color.m_Texture.read()
            materials += 1
    assert materials >= 5, 'Female character materials are incomplete'
    return {'resources_prefab': prefab.m_Name, 'avatar': avatar['m_Name'],
            'humanoid_bones': len(mapping), 'skinned_meshes': len(skins),
            'material_references': materials}


def verify_named_access(metadata):
    identifiers = (b'BetaLoginBody', b'IsBetaUid', b'RememberAccountUid', b'auth-uid')
    for identifier in identifiers:
        assert identifier in metadata, 'Named beta login implementation is missing: ' + identifier.decode()
    # This is the compiled input validator, not a list of accounts or passwords.
    pattern = rb'\A(?:[1-9][0-9]{0,18}|[A-Za-z][A-Za-z0-9_]{2,31})\z'
    assert pattern in metadata, 'APK still contains the obsolete numeric-only or incompatible beta identifier validator'
    return {'numeric_uid_or_named_alias': True, 'maximum_identifier_length': 32,
            'validation': 'compiled metadata and validator literal'}


def verify_rifle(environment, metadata, expected_calibration=None):
    for identifier in (b'RifleAvatar', b'RifleWeapon', b'get_LowReadyAngle',
                       b'LowerAuthoredCarry', b'ConfigureAuthoredRifle', b'ApplyAuthoredRifle',
                       b'weaponEquipped', b'weaponAiming', b'weaponShotSequence', b'weaponAimPitch',
                       b'FireRifle', b'CombatRespawnBody', b'ApplyConfirmedRespawn'):
        assert identifier in metadata, 'Current rifle/combat implementation is missing: ' + identifier.decode()
    prefab = resource_object(environment, 'weapons/rifle/rifle', 'GameObject')
    components = hierarchy_components(prefab)
    transforms = {name: pointer.read() for name, pointer in components if pointer.type.name == 'Transform'}
    assert {'Muzzle', 'SupportGrip', 'Stock'} <= transforms.keys(), 'Rifle grip, stock or muzzle socket is missing'
    meshes = 0
    for _, pointer in components:
        if pointer.type.name in ('MeshFilter', 'SkinnedMeshRenderer'):
            mesh = pointer.read().m_Mesh
            assert mesh, 'Rifle mesh pointer is unresolved'
            mesh.read()
            meshes += 1
    assert meshes > 0, 'Rifle prefab has no packaged geometry'
    clips = {}
    for name in ('Idle', 'Run', 'Shoot'):
        path = 'rifleanimations/' + name.lower()
        matches = [pointer.read() for pointer in resource_pointers(environment, path, 'AnimationClip')
                   if pointer.read().m_Name == 'Rifle' + name]
        assert len(matches) == 1, 'Authored rifle clip is missing: ' + name
        clip = matches[0]
        assert not clip.m_Legacy and clip.m_MuscleClipSize > 0, 'Rifle clip has no retargetable muscle data: ' + name
        assert clip.m_MuscleClip.m_StopTime > clip.m_MuscleClip.m_StartTime and clip.m_SampleRate > 0, 'Rifle clip is empty: ' + name
        avatars = resource_pointers(environment, path, 'Avatar')
        assert len(avatars) == 1, 'Rifle animation Humanoid Avatar is missing: ' + name
        humanoid_avatar(avatars[0], 'Rifle ' + name)
        clips[name] = {'name': clip.m_Name, 'seconds': clip.m_MuscleClip.m_StopTime - clip.m_MuscleClip.m_StartTime}
    required = {'Rifle', 'RifleArms', 'RifleFire', 'Rifle.Locomotion', 'RifleFire.Shot', 'RifleSpeed'}
    controllers = [obj.read() for obj in environment.objects if obj.type.name == 'AnimatorController']
    assert any(required <= {value for _, value in controller.m_TOS} for controller in controllers), 'Rifle layers, states or speed parameter are missing from the packaged Animator controller'
    text = resource_object(environment, 'weapons/rifle/socketcalibration', 'TextAsset').m_Script
    calibration = json.loads(text)
    if expected_calibration is not None:
        assert calibration == expected_calibration, 'Packaged rifle socket calibration differs from the current verified source'
    entries = calibration.get('entries', [])
    keys = [entry.get('avatarKey') for entry in entries]
    assert len(keys) == len(set(keys)), 'Duplicate rifle socket calibration keys'
    for resource in ('characters/bybeyby', 'gangstermusculo/gangstermusculo'):
        model = resource_object(environment, resource, 'GameObject')
        animators = [pointer.read() for _, pointer in hierarchy_components(model) if pointer.type.name == 'Animator']
        assert len(animators) == 1, 'Character has no unique Animator for rifle calibration: ' + resource
        avatar, mapping = humanoid_avatar(animators[0].m_Avatar, resource)
        key = '|'.join([avatar['m_Name']] + [mapping[bone] for bone in ('RightUpperArm', 'RightLowerArm', 'RightHand')])
        assert key in keys, 'Rifle socket calibration does not support the actual packaged rig: ' + key
    for entry in entries:
        for field in ('weaponInRightHand', 'leftHandInWeapon'):
            values = [entry[field][axis] for axis in 'xyzw']
            assert all(isinstance(value, (int, float)) and math.isfinite(value) for value in values), 'Invalid rifle socket rotation'
            assert abs(sum(value * value for value in values) - 1) < .01, 'Rifle socket rotation is not normalized'
    return {'mesh_components': meshes, 'sockets': ['Muzzle', 'SupportGrip', 'Stock'],
            'clips': clips, 'animator_layers_verified': True, 'supported_rigs': keys,
            'low_ready_implementation_included': True, 'replicated_weapon_state_included': True}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk', type=Path)
    args = parser.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parent / 'apk-inspection-deps'))
    import UnityPy
    with zipfile.ZipFile(args.apk) as archive:
        environment = UnityPy.load(archive.read('assets/bin/Data/data.unity3d'))
        print(json.dumps(verify_female_character(environment), indent=2))
