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
assert unreal.EditorAssetLibrary.save_loaded_asset(texture, False) # Wait for initial texture compilation.
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
adapter = load('/Game/Meddle/Adapter/Animations/A_Adapter_Move_0_0', unreal.AnimSequence)
adapter_mesh = load('/Game/Meddle/Adapter/Meshes/SK_Adapter', unreal.SkeletalMesh)
assert editor.get_num_verts(adapter_mesh, 0) == 3
adapter_skeleton = adapter_mesh.get_editor_property('skeleton')
assert adapter_skeleton.get_path_name().startswith('/Game/Meddle/Adapter/Skeletons/')
assert adapter.get_editor_property('skeleton') == adapter_skeleton
adapter_material = load('/Game/Meddle/Adapter/Materials/M_0_Body', unreal.Material)
assert adapter_mesh.get_editor_property('materials')[0].material_interface == adapter_material
assert adapter_material.get_editor_property('two_sided')
assert not material.get_editor_property('two_sided')
mirrored_mesh = load('/Game/Meddle/Mirrored/Meshes/SK_Mirrored', unreal.SkeletalMesh)
queries = unreal.GeometryScript_MeshQueries
for mode,expected_count in [('All',1),('ByPart',1),('None',3)]:
    directory='/Game/Meddle/Furniture'+mode+'/Meshes/'
    assets=unreal.EditorAssetLibrary.list_assets(directory,False,False)
    assert len(assets)==expected_count, (mode,assets)
    positions=[]
    for path in assets:
        part=load(path,unreal.SkeletalMesh)
        assert part.get_editor_property('skeleton')
        dynamic,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(
            part,unreal.DynamicMesh(),unreal.GeometryScriptCopyMeshFromAssetOptions(),unreal.GeometryScriptMeshReadLOD())
        assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS
        for vertex in range(editor.get_num_verts(part,0)):
            position,valid=queries.get_vertex_position(dynamic,vertex)
            assert valid
            positions.append(round(position.y,2))
        for triangle in range(queries.get_num_triangle_i_ds(dynamic)):
            face,valid=queries.get_triangle_face_normal(dynamic,triangle)
            _,a,b,c,normals_valid=queries.get_triangle_normals(dynamic,triangle)
            assert valid and normals_valid and face.dot(a+b+c)>2.99, path
        assert unreal.EditorAssetLibrary.save_loaded_asset(part,False)
    assert sorted(positions)==[1200,1200,1300,1400,1400,1500,1500,1600,1600], (mode,positions)
for mode,expected in [('All',{'SK_角色All':12}),
                      ('ByPart',{'SK_Top':6,'SK_Face':3,'SK_Weapon_Attach1':3}),
                      ('None',{'SK_原始模型':3,'SK_原始模型_1':3,'SK_原始模型_2':3,'SK_武器':3})]:
    directory='/Game/Meddle/角色'+mode+'/Meshes/'
    assets=unreal.EditorAssetLibrary.list_assets(directory,False,False)
    assert len(assets)==len(expected), (mode,assets)
    all_positions=[]
    for name,count in expected.items():
        part=load(directory+name,unreal.SkeletalMesh)
        assert editor.get_num_verts(part,0)==count, name
        assert len(part.get_editor_property('materials'))==count//3, name
        assert len(part.get_editor_property('morph_targets'))==1, name
        assert part.get_editor_property('morph_targets')[0].get_name()=='形态'
        dynamic,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(
            part,unreal.DynamicMesh(),unreal.GeometryScriptCopyMeshFromAssetOptions(),unreal.GeometryScriptMeshReadLOD())
        assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS
        for vertex in range(count):
            position,valid=queries.get_vertex_position(dynamic,vertex)
            assert valid
            all_positions.append(round(position.y,2))
        assert unreal.EditorAssetLibrary.save_loaded_asset(part,False)
    assert sorted(all_positions)==[0,0,100,200,200,300,400,400,500,600,600,700], (mode,all_positions)
mixed=load('/Game/Meddle/MixedInstances/Meshes/SK_MixedInstances',unreal.SkeletalMesh)
assert editor.get_num_verts(mixed,0)==15
dynamic,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(
    mixed,unreal.DynamicMesh(),unreal.GeometryScriptCopyMeshFromAssetOptions(),unreal.GeometryScriptMeshReadLOD())
assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS
positions=[round(queries.get_vertex_position(dynamic,i)[0].y,2) for i in range(15)]
assert sorted(positions)==[0,0,100,200,200,300,400,400,500,600,600,700,800,800,900], positions
for oriented_mesh in [mesh, adapter_mesh, mirrored_mesh]:
    dynamic, outcome = unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(
        oriented_mesh, unreal.DynamicMesh(), unreal.GeometryScriptCopyMeshFromAssetOptions(),
        unreal.GeometryScriptMeshReadLOD())
    assert outcome == unreal.GeometryScriptOutcomePins.SUCCESS
    assert queries.get_num_triangle_i_ds(dynamic) == 1
    face, valid = queries.get_triangle_face_normal(dynamic, 0)
    _, n1, n2, n3, normals_valid = queries.get_triangle_normals(dynamic, 0)
    assert valid and normals_valid
    assert face.dot(n1+n2+n3) > 2.99, oriented_mesh.get_path_name()
pose = unreal.AnimationLibrary.get_bone_pose_for_time(adapter, 'root', .5, False)
assert abs(pose.translation.z-50) < .01, pose
motion = load('/Game/Meddle/RootMotion/Animations/A_RootMotion_Move_0_0', unreal.AnimSequence)
assert motion.get_editor_property('skeleton') != adapter_skeleton
assert motion.get_editor_property('skeleton').get_path_name().startswith('/Game/Meddle/RootMotion/Skeletons/')
assert motion.get_editor_property('enable_root_motion')
pose = unreal.AnimationLibrary.get_bone_pose_for_time(motion, 'Meddle_Root', .5, False)
assert abs(pose.translation.z-50) < .01, pose
for asset in [skeleton,mesh,texture,material,adapter,adapter_mesh,motion]:
    assert unreal.EditorAssetLibrary.save_loaded_asset(asset, False)
for name in ['hair', 'iris', 'skin', 'character']:
    mat = load(p+name, unreal.Material)
    for prop in [unreal.MaterialProperty.MP_BASE_COLOR, unreal.MaterialProperty.MP_NORMAL,
                 unreal.MaterialProperty.MP_ROUGHNESS, unreal.MaterialProperty.MP_METALLIC,
                 unreal.MaterialProperty.MP_EMISSIVE_COLOR]:
        assert unreal.MaterialEditingLibrary.get_material_property_input_node(mat, prop), (name, prop)
    unreal.MaterialEditingLibrary.recompile_material(mat)
    assert unreal.EditorAssetLibrary.save_loaded_asset(mat, False)
linear = load(p+'Linear', unreal.Texture2D)
assert not linear.get_editor_property('srgb')
assert linear.get_editor_property('compression_settings') == unreal.TextureCompressionSettings.TC_VECTOR_DISPLACEMENTMAP
floating = load(p+'FloatTable', unreal.Texture2D)
assert floating.get_editor_property('compression_settings') == unreal.TextureCompressionSettings.TC_HDR_F32
assert floating.get_editor_property('filter') == unreal.TextureFilter.TF_NEAREST
assert floating.get_editor_property('address_x') == unreal.TextureAddress.TA_CLAMP
assert floating.get_editor_property('address_y') == unreal.TextureAddress.TA_CLAMP
assert not floating.get_editor_property('srgb')
assert unreal.EditorAssetLibrary.save_loaded_asset(floating, False)
unreal.log('MEDDLE_NATIVE_ASSET_VALIDATION_PASSED')
