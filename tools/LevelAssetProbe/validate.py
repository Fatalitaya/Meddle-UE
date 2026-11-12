import unreal

def near(actual, expected):
    assert (actual - expected).length() < .01, (actual, expected)

editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
assert editor.load_level('/Game/Meddle/Probe/Territory')
actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
actor = next(a for a in actors if a.get_actor_label() == 'Test group')
components = actor.get_components_by_class(unreal.InstancedStaticMeshComponent)
assert len(components) == 2, components
c = next(c for c in components if c.get_instance_count() == 2)
mirrored = next(c for c in components if c.get_instance_count() == 1)
assert c.get_instance_count() == 2
near(c.get_instance_transform(0, False).translation, unreal.Vector(10,20,30))
near(c.get_instance_transform(0, True).translation, unreal.Vector(1010,2020,3030))
near(c.get_instance_transform(1, False).scale3d, unreal.Vector(2,3,4))
near(c.get_instance_transform(1, False).transform_location(unreal.Vector(1,0,0)), unreal.Vector(51.755165,60.958851,70))
assert c.static_mesh.get_path_name() == '/Game/Meddle/Probe/LocalMesh.LocalMesh'
assert mirrored.static_mesh == c.static_mesh
assert c.get_material(0).get_name() == 'Red'
assert mirrored.get_material(0).get_name() == 'Blue'
assert mirrored.get_editor_property('reverse_culling')
near(mirrored.get_instance_transform(0, False).transform_location(unreal.Vector(1,0,0)), unreal.Vector(-11,0,0))
dynamic, outcome = unreal.GeometryScript_AssetUtils.copy_mesh_from_static_mesh(
    c.static_mesh, unreal.DynamicMesh(), unreal.GeometryScriptCopyMeshFromAssetOptions(), unreal.GeometryScriptMeshReadLOD())
assert outcome == unreal.GeometryScriptOutcomePins.SUCCESS
positions = [unreal.GeometryScript_MeshQueries.get_vertex_position(dynamic,i)[0] for i in range(3)]
assert any(p.length() < .01 for p in positions), positions
assert max(p.length() for p in positions) < 101, positions
# Render geometry alone is insufficient: a missing ExtendedBounds makes ISMs
# disappear through frustum/occlusion culling, especially single-instance rooms.
bounds = c.static_mesh.get_bounds()
near(bounds.origin, unreal.Vector(50, 50, 0))
near(bounds.box_extent, unreal.Vector(50, 50, 0))
assert abs(bounds.sphere_radius - (50 * 50 * 2) ** .5) < .01, bounds
assert editor.save_current_level()
unreal.log('MEDDLE_LEVEL_VALIDATION_PASSED')
