import unreal

editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
assert editor.new_level('/Game/Meddle/Probe/Host')
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
offset = unreal.Transform(location=unreal.Vector(500, 600, 700), rotation=unreal.Rotator(0, 90, 0))
streaming = unreal.EditorLevelUtils.add_level_to_world_with_transform(
    world, '/Game/Meddle/Probe/Territory', unreal.LevelStreamingAlwaysLoaded, offset)
assert streaming
actor = next(a for a in unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
             if a.get_actor_label() == 'Test group')
c = next(c for c in actor.get_components_by_class(unreal.InstancedStaticMeshComponent) if c.get_instance_count() == 2)
expected = offset.transform_location(unreal.Vector(1010,2020,3030))
actual = c.get_instance_transform(0, True).translation
assert (actual - expected).length() < .01, (actual, expected)
assert editor.save_current_level()
unreal.log('MEDDLE_SUBLEVEL_VALIDATION_PASSED')
