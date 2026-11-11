"""GPU numeric validation. Run without -nullrhi; uses offscreen linear float targets."""
import json
import os
import unreal

roundtrip_only='-MeddleRoundtripOnly' in unreal.SystemLibrary.get_command_line()
cases=[] if roundtrip_only else json.load(open(os.path.join(unreal.Paths.project_dir(),'formula_cases.json'),encoding='utf-8'))
world=unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
target=unreal.RenderingLibrary.create_render_target2d(world,4,4,unreal.TextureRenderTargetFormat.RTF_RGBA32F,
                                                     unreal.LinearColor(0,0,0,0),False,False)
precision=unreal.load_asset('/Game/Meddle/Probe/FloatPrecision')
assert precision
if precision:
    assert not unreal.MaterialEditingLibrary.recompile_material(precision)
    unreal.MaterialEditingLibrary.get_statistics(precision)
    unreal.RenderingLibrary.draw_material_to_render_target(world,target,precision)
    value=unreal.RenderingLibrary.read_render_target_raw_pixel(world,target,2,2,False)
    assert abs(value.r-4)<1e-6 and abs(value.g-.1234567)<1e-7 and value.b==65535, value
    assert unreal.EditorAssetLibrary.save_loaded_asset(unreal.load_asset('/Game/Meddle/Probe/FloatTable'),False)
    assert unreal.EditorAssetLibrary.save_loaded_asset(precision,False)
failures=[]
count=0
if roundtrip_only:
    # Also compile complete Default Lit materials, with all attributes connected
    # together, rather than only the individual Unlit measurement proxies.
    for name in ['hair','iris','skin','character']:
        material=unreal.load_asset('/Game/Meddle/Probe/'+name)
        assert not unreal.MaterialEditingLibrary.recompile_material(material), name
        statistics=unreal.MaterialEditingLibrary.get_statistics(material)
        assert statistics.num_pixel_shader_instructions>0, (name,statistics)
        assert unreal.EditorAssetLibrary.save_loaded_asset(material,False)
for case in cases:
    if case['name'] in ['shpk_legacy_0_0','shpk_GetValuesCompatibility','shpk_GetDecalColorRGBA',
                         'shpk_charactertattoo_0_5_True','shpk_characterocclusion_0_5_False','hair_shpk_normal_0','hair_shpk_alpha_64']:
        full=unreal.load_asset('/Game/Meddle/Probe/'+case['name']+'_Full')
        assert full
        assert not unreal.MaterialEditingLibrary.recompile_material(full), case['name']
        if case['name'].startswith('hair_shpk_'):
            assert full.get_editor_property('blend_mode') == unreal.BlendMode.BLEND_MASKED
            assert full.get_editor_property('dither_opacity_mask') == case['metadata']['IsTransparent']
        assert unreal.MaterialEditingLibrary.get_statistics(full).num_pixel_shader_instructions>0, case['name']
        assert unreal.EditorAssetLibrary.save_loaded_asset(full,False)
    for prop,expected in case['expected'].items():
        material=unreal.load_asset('/Game/Meddle/Probe/'+case['name']+'_'+prop)
        assert material, (case['name'],prop)
        errors=unreal.MaterialEditingLibrary.recompile_material(material)
        assert not errors, (case['name'],prop,list(errors))
        unreal.MaterialEditingLibrary.get_statistics(material) # Wait for actual shader compilation.
        unreal.RenderingLibrary.clear_render_target2d(world,target,unreal.LinearColor(0,0,0,0))
        unreal.RenderingLibrary.draw_material_to_render_target(world,target,material)
        color=unreal.RenderingLibrary.read_render_target_raw_pixel(world,target,2,2,False)
        actual=[color.r,color.g,color.b]
        if prop=='Normal':actual=[2*x-1 for x in actual]
        expected=expected[:3] if isinstance(expected,list) else [expected]*3
        if any(abs(a-b)>max(.002,abs(b)*.002) for a,b in zip(actual,expected)):
            failures.append(dict(case=case['name'],property=prop,actual=actual,expected=expected))
        count+=1
result=dict(attributes=count,failures=failures)
with open(os.path.join(unreal.Paths.project_dir(),'float_roundtrip_results.json' if roundtrip_only else 'formula_results.json'),'w') as f:json.dump(result,f,indent=2)
assert not failures, failures[:12]
unreal.log('MEDDLE_FORMULA_NUMERICS_PASSED '+str(count))
