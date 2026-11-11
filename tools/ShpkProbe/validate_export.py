"""Run in a disposable UE project containing a copy of the repaired export."""
import json
from pathlib import Path
import unreal

content = Path(unreal.Paths.project_content_dir())
checked = 0
for path in content.rglob('*.formula.json'):
    report = json.loads(path.read_text(encoding='utf-8-sig'))
    if report.get('Shader') != 'characterlegacy.shpk':
        continue
    package = '/Game/' + path.relative_to(content).as_posix().removesuffix('.formula.json')
    material = unreal.load_asset(package)
    assert isinstance(material, unreal.Material), package
    normal = unreal.MaterialEditingLibrary.get_material_property_input_node(material, unreal.MaterialProperty.MP_NORMAL)
    assert '(detail-sign(b))+sign(b)' in normal.get_editor_property('code'), package
    for texture in report['Textures']:
        assert isinstance(unreal.load_asset(texture['Package']), unreal.Texture2D), texture
    assert not unreal.MaterialEditingLibrary.recompile_material(material), package
    assert unreal.MaterialEditingLibrary.get_statistics(material).num_pixel_shader_instructions > 0, package
    assert unreal.EditorAssetLibrary.save_loaded_asset(material, False), package
    checked += 1
assert checked > 0
unreal.log(f'MEDDLE_REPAIRED_EXPORT_PASSED {checked}')
