import unreal

def load(path, cls):
    obj = unreal.load_asset(path)
    assert isinstance(obj, cls), (path, obj)
    return obj

p = '/Game/Meddle/Probe/'
skeleton = load(p+'Skeleton', unreal.Skeleton)
mesh = load(p+'Mesh', unreal.SkeletalMesh)
assert mesh.get_editor_property('skeleton') == skeleton
editor = unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
assert editor.get_num_verts(mesh, 0) == 3
assert len(mesh.get_editor_property('morph_targets')) == 1
texture = load(p+'Texture', unreal.Texture2D)
assert texture.blueprint_get_size_x() == 2 and texture.blueprint_get_size_y() == 2
material = load(p+'Material', unreal.Material)
assert unreal.MaterialEditingLibrary.get_material_property_input_node(material, unreal.MaterialProperty.MP_BASE_COLOR)
for fps in [30,60]:
    animation = load(p+'Animation'+str(fps), unreal.AnimSequence)
    assert abs(animation.get_play_length()-1) < 1e-5
    for time in [0, .25, .5, 1]:
        pose = unreal.AnimationLibrary.get_bone_pose_for_time(animation, 'root', time, False)
        assert abs(pose.translation.z-time*100) < .01, (time, pose)
    assert unreal.EditorAssetLibrary.save_loaded_asset(animation, False)
adapter = load('/Game/Meddle/Adapter/A_0_0', unreal.AnimSequence)
adapter_mesh = load('/Game/Meddle/Adapter/SK_0', unreal.SkeletalMesh)
assert editor.get_num_verts(adapter_mesh, 0) == 3
pose = unreal.AnimationLibrary.get_bone_pose_for_time(adapter, 'root', .5, False)
assert abs(pose.translation.z-50) < .01, pose
motion = load('/Game/Meddle/RootMotion/A_0_0', unreal.AnimSequence)
assert motion.get_editor_property('enable_root_motion')
pose = unreal.AnimationLibrary.get_bone_pose_for_time(motion, 'Meddle_Root', .5, False)
assert abs(pose.translation.z-50) < .01, pose
for asset in [skeleton,mesh,texture,material,adapter,adapter_mesh,motion]:
    assert unreal.EditorAssetLibrary.save_loaded_asset(asset, False)
unreal.log('MEDDLE_NATIVE_ASSET_VALIDATION_PASSED')
