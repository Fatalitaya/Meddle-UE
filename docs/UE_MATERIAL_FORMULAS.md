# UE 材质属性公式

2026-11-12：皮肤已替换为 [Skin SHPK / MTRL 公式](UE_SKIN_REVERSE.md)。下文皮肤 Blender 映射属于历史参考，不再用于生产导出或皮肤数值验证。

2026-11-12 补充：[Dalamud-VFXEditor 资产解析对照](UE_SHPK_REVERSE.md#dalamud-vfxeditor-资产解析对照补充)记录色表尺寸、Sampler 寻址与有符号 LOD、SHPK 多阶段与 alias cluster 的解析公式和修正。不采用其预览着色器；本页现有表面属性公式保持原验证来源。

参考固定为 [MeddleTools 56aa1252ba341fbc732df6e79b95e388d2057ba0](https://github.com/PassiveModding/MeddleTools/tree/56aa1252ba341fbc732df6e79b95e388d2057ba0)。节点来自该版本的 [shaders.blend](https://github.com/PassiveModding/MeddleTools/blob/56aa1252ba341fbc732df6e79b95e388d2057ba0/MeddleTools/shaders.blend)，输入映射和纹理配置来自 [node_configs.py](https://github.com/PassiveModding/MeddleTools/blob/56aa1252ba341fbc732df6e79b95e388d2057ba0/MeddleTools/node_setup/node_configs.py)，色表奇偶拆分来自 [node_mappings.py](https://github.com/PassiveModding/MeddleTools/blob/56aa1252ba341fbc732df6e79b95e388d2057ba0/MeddleTools/node_setup/node_mappings.py)。不以游戏原始 shader 或视觉相似度作为已验证的依据。

源码归属：MeddleTools / PassiveModding，AGPL-3.0；其角色 shader 的上游归属包括 Lizzer_Tools_Meddle（GPL v3）。本仓库沿用自身 AGPL-3.0 许可。`tools/NativeAssetProbe/shaders.snapshot.json` 是四类材质及其递归子组的节点数据快照，包含来源提交、blend SHA256、Blender 版本、插槽序号、默认值、连接、操作、钳制、色带和 UV 信息。它只用于证据与测试，不在插件运行时加载。

## 支持条件

只精确匹配 `character.shpk`、`skin.shpk`、`hair.shpk`、`iris.shpk`。`CharacterMaterial` 描述 shader 分支和输出计算，`MaterialGraph` 描述有限 UE 表达式；没有通用 Blender 图转换器。标准 Material 通过 TextureCoordinate、VertexColor、TextureSample 与 Custom HLSL 表达式生成。

头发需要 Normal、Mask；虹膜需要 Diffuse、Normal、Mask；皮肤另需两张 `chara_tile_*_array`；装备另需 Index、完整 32 行结构化现代色表和两张 Tile 资源。数组从既有 `array_textures/chara/common/texture/tile_*_array/*.vertical.png` 读取，存在多个候选时不猜选。缺资源或色表不完整时保留基础路径，每个材质旁的 `.formula.json` 记录 `FormulaEnabled=false` 和具体缺口。颜色预览 PNG 不参与色表运算。

## Blender 到 UE 的转换

这里迁移的是 BSDF 的输入属性，不是 BSDF 本身。UE 默认 Default Lit 与 Blender Principled 的粗糙度响应、多重散射、高光、IOR、次表面、Sheen 均不等价；不转换闭包、Mix Shader、次表面或高光模型，不把 IOR/Specular IOR Level 换算为 UE Specular。UE 保持默认 Specular。

颜色常量按线性 Rec.709 处理，与上游 `toBlenderColor` 的输入契约一致；要求 UE 默认工作色域。颜色纹理 sRGB 解码一次，Mask/Index/打包法线/Decal/Tile 为线性，PNG 使用未预乘 Alpha 解码。原始法线不再使用 BC5 Normalmap 压缩，也不在源像素中翻绿，保留 B/A 遮罩。数据纹理使用 VectorDisplacementmap、无 mip；Index 和 Tile 最近邻，其他线性。皮肤 Face 的 Diffuse Clamp，其他常规纹理 Repeat。Decal 用 Clamp 采样乘双线性边界覆盖率表示 CLIP：每轴 `sat(min(uv,1−uv)×尺寸+0.5)`，两轴相乘；边界内外各半个 texel 向透明黑插值。

glTF 与 UE 的 V 向下；Blender 导入后的 UVMap 的 V 向上。常规贴图直接使用 UE UV；Tile 先转换为 Blender UV，完成取模/切片后再转换回 UE。`Normal_Fix` 输出先在局部切线空间表示为 `normalize(2R−1, 2G−1, 1)`，转换到 UE 时反转 Y。B 不参与 Z 重建，不做平方根补 Z。基础和 Detail 的法线在同一 UV0 切线基中相加；UE 对最终 Normal 的规范化与实际网格切线基仍会影响最终光照。

Blender Emission Color × Strength 合成为 UE EmissiveColor，保留 HDR。Blender 图明确连接的 Reinhard 运算仍保留，不能把这一步误当作应取消的纹理烘色。纹理导出不再提前乘角色颜色，避免双重乘色和 8 位 HDR 截断。

## 节点路径与实施公式

以下路径均相对于固定提交的 `shaders.blend`。完整序号和默认值见快照；`sat` 为钳制到 0..1，`L(a,b,t)` 为钳制因子的线性混合。

| 路径 | 输入及公式 | UE 实现 |
| --- | --- | --- |
| `Normal_Fix/Separate Color → Combine Color → Normal Map` | RG 原值，Combine 的 B 固定 1，Strength=1；Blue 原样另行输出 | 线性采样 + normalize + 切线 Y 反转 |
| `HDRtoSDR/Reinhard/Math, Math.001` | 每通道 `c/(1+c)`；Blender 除零返回 0 | 有限 Custom HLSL 的安全除法 |
| `alpha_threshold/Math → Math.001` | `a * (a > threshold)`，相等为 0 | 皮肤 Opacity 算术 |
| `alpha_threshold_div/Math.002 → Mix.001 → Math → Mix` | `a>0 ? a/(threshold==0 ? 1 : threshold) : 0`，不钳制结果 | 头发/装备 Opacity 算术 |
| `meddle hair.shpk/Mix.005 → Mix → Mix.004 → Mix.001` | UV1 Normal.B 混合主色与挑染；Face 分支改用 UV0.B 和默认黑色 Tattoo Color；乘 Reinhard(g_DiffuseColor)、Mask.A | BaseColor；不把未映射的 Tattoo Color 猜成 OptionColor |
| `meddle hair.shpk/Mix.003, Group.002, Separate Color` | Roughness=Mask.R；Normal/Opacity 已改为 SHPK 依据的实现 | 法线强度、源切线基底、双面法线、独立 Alpha MipBias 及 Masked 抖动覆盖见 [头发逆向记录](UE_HAIR_REVERSE.md) |
| `meddle skin.shpk/Mix.015 → Mix.013 → Mix.012 → Mix.010 → Mix.011 → Mix.014` | Normal.B 选择 Reinhard 漫反射常量/肤色；Face 使用 LipColor.A×Normal.A；FaceEmissive 使用自发光亮度×Normal.A；BodyJJM 混合头发色并强制 Alpha=1；Diffuse 乘色以 Alpha 作因子；最后混 Decal 色 | BaseColor；严格保留分支和连接顺序 |
| `meddle skin.shpk/Separate Color, Math.011` | Roughness=Mask.G；Emissive=g_EmissiveColor×Normal.A×FaceEmissive | Roughness、EmissiveColor |
| `meddle iris.shpk/Math → Mix → Mix.002 → Mix.001` | 顶点 R>G 选左眼，否则右眼（相等选右）；Mask.B 混眼白与眼色；乘 Diffuse | VertexColor + BaseColor |
| `meddle iris.shpk/Mapping → Gradient Texture → Math.001…Math.010` | Texture Mapping 减中心 (0.5,0.5)，SPHERICAL 距离；内外半径及 Fade 的两段 Map Range，保留严格/非严格比较和钳制 | 虹膜环；Emission Strength 为所选眼颜色 A×环自发光强度 |
| `meddle iris.shpk/Group Output` | Metallic 默认 0、Roughness 默认 0.2；无已连接 Alpha 路径 | 常量；默认 Opaque |
| 装备 `ColorRampA/B → meddle colortablemix` | `pair=min(floor(Index.R×16),15)`；A 为偶数行、B 为奇数行；`L(B,A,Index.G)` 混 Diffuse/Emissive | 常量数组色带，保留 HDR |
| 装备 `packed_ramp_mixer/Math → Mix*` | `sat(round(Index.G))`；0 选 B，1 选 A；0.5 选 A，不使用 HLSL 银行家 round | 显式 `Index.G>=0.5` 选行，参数与颜色混合分开 |
| `meddle character.shpk/Mix, Math, Mix.002` | 常规 BaseColor=色表；Compatibility 才乘 Diffuse；Metallic=所选色表值；Roughness=`sat(table.Roughness×Mask.G)` | BaseColor、Metallic、Roughness |
| `tile_select/ApplyUVScale → Map Range` | UV 乘 U/V Repeat、fmod 1；64 行纵向数组，floor(TileIndex)；装备使用色表 UU、VV，未连接的 UV/VU 不启用 | 显式 Tile UV 节点，最近邻采样 |
| `chara_detail_blend/Darkening, Normal_Fix → Vector Math → Node` | Color×sat(Orb.B)；Normal+DetailNormal×Norm.A×TileAlpha | 装备/皮肤最终 BaseColor 与 Normal；皮肤 Decal 在 Detail 之前，装备在其之后 |
| `meddle decaluv/Mapping → Vector Math` | UV1.x×FacePaintUVMultiplier+FacePaintUVOffset，Y 不变 | 皮肤 Decal UV；装备直接 UV1 |

## 色表源数据

旧色表每行 32 字节，即 4 个 RGBA half texel，已修正原来的 8×16 声明为 4×16。现代色表从 `ColorTable.ColorTable.Rows` 读取已解码字段，`MaterialColorTable` 校验 32 行及所用字段；不读取预览图，不把 ShaderId/SphereIndex 等原始整数位字段解释为 half 颜色。

用于计算的 Diffuse、Emissive、Metallic、Roughness、TileIndex、TileAlpha、UU、VV 以常量色带接入图，并另存可编辑 `T_*_ColorTableFloat`（4×32 RGBA32F，线性、最近邻、Clamp）。它保存的是上述已解码属性，不声称是完整原始 64 字节行的备份。`TextureWriter.WriteFloat` 使用 TSF_RGBA32F / TC_HDR_F32，不经过半浮点中转。

## 验证方法

正常构建和导出不依赖 Blender 或 UE。重新提取证据时使用 Blender 后台执行 `snapshot_shaders.py`；测试期 `formula_reference.py` 递归解释固定快照中的连接，独立于生产 C#/HLSL。`make_formula_cases.py` 生成合成纹理、UV、顶点色、常量和期望属性。

```powershell
dotnet test Meddle/Meddle.Utils.Tests -c Release
dotnet build Meddle.sln -c Release
./tools/NativeAssetProbe/validate.ps1 -EngineRoot 'D:/UE/Source/UnrealEngine-5.8.3-release'
./tools/NativeAssetProbe/validate_formulas.ps1 -EngineRoot 'D:/UE/Source/UnrealEngine-5.8.3-release'
```

资产探针检查加载、输出连接、纹理设置和重新保存。数值探针使用 D3D12/SM6 离屏渲染，将每个属性公式接到 Unlit Emissive 输出，再读取线性浮点 RenderTarget；法线以 0.5×N+0.5 编码后还原。测试注入明确的 UV/顶点色，但保留生产 TextureSample 和 HLSL。此方法验证属性算术，避免用光照后的像素反推材质参数；不验证 Default Lit 的 BRDF 或游戏画面。输出的 `formula_results.json` 列出属性数量和所有不一致。

`validate_blender_reference.py` 另用实际 Blender 节点组执行头发、皮肤、虹膜的代表性属性，读取 EXR 验证独立求值器；并不将 Blender 加入正常构建依赖。重新提取快照前应核对固定提交及 blend SHA256。

内联 source bulk 的回归测试还检查最终文件中的实际像素位置与记录的绝对偏移相等：本次修复了原写入器在计算偏移后才增加 import 位表导致的 4 字节错位。这同时影响 BGRA8 与 RGBA32F；应重新导出旧纹理资产。

实际执行结果见文末验证记录。

## 疑点、当前处理与补齐条件

| 项目 | 证据和当前处理 | 补齐条件 |
| --- | --- | --- |
| legacy | 2026-11-12 已按 SHPK 补充当前 32 行色表的 characterlegacy 属性适配，不使用此 Blender legacy 公式；旧 16 行 `LegacyColorTable` 仍未转换 | [SHPK 逆向记录与验证](UE_SHPK_REVERSE.md) |
| shader 别名 | 2026-11-12 已独立实现 tattoo、occlusion 的 SHPK 属性适配，不再把它们当普通材质别名；Stockings 等仍未验证 | [SHPK 适配差异](UE_SHPK_REVERSE.md)，其余变体继续基础回退 |
| 上游未连接输出/TODO | hair 的 Specular IOR Level 未接 BSDF；skin 的 GetMaterialValueBody 无插槽；GetDecalColorAlpha 不参与输出；character 的 IS_TRANSPARENCY、Diffuse Alpha、Sphere/Sheen 支路不构成已实施属性 | 上游明确连接并更新固定版本后重新提取，不凭名字实现 |
| 切线基 | Normal_Fix 在 Blender 中返回世界法线；当前使用正交 UV0 切线基下的代数等价和 V 反射 | 实际网格在 Blender/UE 中的切线基数值对照，尤其镜像 UV、退化 UV、非正交插值 |
| 透明混合 | 上游公式值独立保留；IsTransparent 使用 Translucent，其余已核实 Alpha 的材质使用 Masked（UE Clip=0.5），虹膜/回退默认 Opaque | 若需等价覆盖率，另行定义引擎混合和抗锯齿策略；不把模式适配称为公式等价 |
| 颜色管理 | 以线性 Rec.709 为工作空间；Blender Color→Value 使用 Rec.709 亮度系数 | 非默认 Blender/UE 工作色域需显式颜色矩阵，不在本实现内猜测 |
| 纹理边界与采样 | Index/Tile 最近邻、普通贴图线性；无 mip；Decal CLIP 保留半 texel 的透明边界插值 | 不同引擎在 texel 边界过滤足迹、导数、各向异性和压缩误差仍可能不同 |
| 资源缺失 | Tile 确实影响最终 Color/Normal，不补中性图；多候选数组不猜选 | 导出缓存提供唯一完整资源后重新导出 |
| BRDF/SSS/IOR | 非确定性颜色属性换算问题；未移植闭包 | 需要独立的 UE 光照模型研究，不以本次数值通过宣称视觉一致 |

## 验证记录

本机执行日期：2026-11-11。引擎 UE 5.8.3，Blender 5.2.3 LTS。

- **数值通过**：119 组合成输入，714 个 UE 属性 GPU 读回，0 不一致。包含全部 16 对色表行、行边界、混合 0/0.5/1 与 0.5 前侧、HDR、法线 B/A、双 UV、左右眼及相等比较、两类 Alpha 的零阈值与相等值、Tile 切片、Decal 边界。普通属性容差 `max(0.002, |参考值|×0.002)`。
- **Blender 参考通过**：实际节点执行 24 个代表性属性读回；另 19 个 CLIP/Alpha 读回通过。CLIP 测试证实边缘半个 texel 的透明黑插值，已在 UE 中实现。
- **资产通过**：四类标准 Material 加载、输出连接、重新保存；另在 D3D12/SM6 中编译全部属性同时连接的四类 Default Lit 材质，并检查实际像素着色器指令统计；线性压缩、RGBA32F、最近邻、Clamp、sRGB 设置检查通过。原有网格、骨骼、动画探针同时通过。浮点源值 `4 / 0.1234567 / 65535` 在保存及另一个 UE 进程重载后均通过严格精度检查（分别 1e-6、1e-7、精确相等），不使用上述宽松属性容差。
- **托管检查通过**：164 个单元测试；Release 解决方案构建 0 警告、0 错误；包含旧色表全部行列和最终文件 bulk 偏移回归测试。
- **未覆盖**：真实角色在游戏内的完整导出；真实网格上 Blender/UE 的切线基数值对照；非 Rec.709 工作空间；复杂纹理的各向异性/mip/压缩过滤差异；legacy 数据接入及 shader 别名；BRDF、SSS、IOR、视觉一致性。数值测试通过不表示这些项目通过。

数值结果：`MeddleFormulaValidation-846c56fa571b4ec8b5e0f680265e5dbb/formula_results.json`（系统临时目录）；资产日志：`MeddleValidation-72818f049fd541cda3fc32bf575482a5/validation.log`。仓库 `artifacts/ue-material-*.log` 保留本次托管与 Blender 检查日志。

## 背景颜色与实例自发光逆向

背景材质使用独立的 `BackgroundMaterial`，不沿用上文角色的 sRGB、tint 或法线公式。覆盖 `bg.shpk`、`terrain.shpk`、`bgskin.shpk` 的默认采样分支、`bgprop.shpk` 与 `bgcolorchange.shpk` 的干燥延迟表面属性。

### 颜色公式

令 C0/C1 为关闭硬件 sRGB 解码的颜色采样，D0/D1 为 g_DiffuseColor/g_MultiDiffuseColor，V 为原始 COLOR_0。

- 单层 Q=C0.rgb×D0。
- GetMultiValues：w=saturate(V.a)，Q=lerp(C0.rgb×D0,C1.rgb×D1,w)。
- GetAlphaMultiValues：a=V.a−0.5，w=saturate(lerp(N0.a,N1.a,AlphaMulti.y)+saturate(abs(2a)−AlphaMulti.x)×AlphaMulti.w×sign(a))。
- ApplyVertexColorOn：Q=max(Q+V.rgb−0.5,0)；Off 不使用顶点 RGB。游戏写入 GBuffer 后平方解码，UE BaseColor=Q²。
- bgcolorchange：BaseColor=C0.rgb²×lerp(1,StainLinear,C0.a)。不乘 g_DiffuseColor。优先使用运行时染色缓冲区的线性值；只有染料表数据时，StainLinear=StainColor.rgb²。
- 普通贴图 UV：UV×NormalUVScale，再分别乘 ColorUVScale/SpecularUVScale。第二层使用 UV1 和参数 zw。bgcolorchange 直接使用 UV0。
- 灰度高光：UE Specular=0.5×SpecularMap.r×saturate(SpecularColor.r)，Roughness=SpecularMap.g。彩色高光无法由 Default Lit 的标量 Specular 完整表达，报告明确记录红通道近似。
- 单层法线先计算 xy=N.rg−0.5，再 normalize(float3(xy×NormalScale,sqrt(max(0.25−dot(xy,xy),1e−6))))。已有 FFXIVBinormal 基转换继续适用。

已检查导出的这七类背景 SHPK 顶点签名，没有发现 COLOR_1 输入；本次验证公式使用 COLOR_0。UE 仍将其存成 RGBA8，不宣称保留任意浮点顶点色精度。

### 运行时数据来源

本地客户端 SHA256：`52042B60631CF2C48851B3AAEE36E9EC4FEDD6D7F2C68F0E9A8C3BEFB0AF9F98`。以下 VA 仅为该版本的证据，不用于插件硬编码调用：

1. BgPartsLayoutInstance vtable slot 45 跳至 `0x14073F5E0`。GetColor 返回 RGB=BgObject[0xD0..D2]×BgObject[0xCE]/255，A=BgObject[0xD3]/255。
2. `0x140453B43` 把 BgObject[0xCD] 放入绘制描述符 0x18；0xCE 放入 0x19；0xD0 的 RGBA 放入 0x14。
3. `0x140287B41` 起将描述符复制到渲染项的 0x52/0x53/0x48。
4. `0x14028B1D2` 起归一化 0x52，写入 m_EmissivePower；`0x14028B207` 起对颜色字节乘强度、除 255、平方，写入 m_EmissiveColor，Alpha/255 写入 m_EmissiveBlend。
5. slot 58 的 `0x14073F330` 验证染色缓冲区 0x04 的 RGB 为字节颜色归一化后的平方，与 [FFXIVClientStructs BgObjectStainBuffer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Graphics/Scene/BgObject.cs) 一致。

因此独立实例颜色是自发光调制，不是通用漫反射 tint。仅当 sum(g_EmissiveColor)>0 时，主层 E=lerp(g_EmissiveColor,LayoutColor.rgb²,LayoutColor.a)，否则 E=0。单层 Emissive=C0.rgb²×E×SpecularMap.a×BgEmissivePower；染色材质以染色后的 BaseColor 代替 C0.rgb²。双层先分别计算 C0²×E 与 C1²×g_MultiEmissiveColor，再按 w 混合，最后乘实例强度。

捕获在框架线程完成，快照保存数值；材料 extras 保存 BgStainLinear、LayoutColor、BgEmissivePower。材质缓存和重复资源分组都包含这些字段，防止同模型不同颜色串用。

### 验证与范围

工具 `tools/ShpkProbe/make_background_cases.py` 直接执行提取的 SM5 指令，独立于生产 HLSL；再用 `tools/NativeAssetProbe` 写入正式 UE 图并 GPU 读回。主参考：bg PS3–10、PS1927；terrain/bgskin PS3–8；bgprop PS3；bgcolorchange PS3。对照记录存于 `artifacts/ue-background-numerics.json`。

bg SHPK SHA256：`9617282FF8A947CEECD11846A73FC1D5EC7782CF19FE22EC363CC6D3195375BE`；bgcolorchange：`8B48A2487CFC29AA42D2AD38E7579520B9CEFAB66D6EFE95D8937961E9EAF667`。

未验证的程序化 detail/noise、bguvscroll、bgcrestchange、透明前向分支明确返回 Gap。双层法线目前共用导出的切线基，独立旋转第二层切线仍有近似；游戏光照、湿润和动画未复现。glTF/GLB 继续保存原始颜色和上述参数，标准查看器不会自动执行这些 SHPK 公式。本次数值测试不是游戏内完整地图截图验收，也未运行游戏内快照采集。

验证结果：UE 5.8.3 / D3D12 / SM6，116 组输入、601 项 GPU 属性读回，0 不一致；包含完整 Default Lit 材质编译、染色 Alpha 0/0.35/1、实例自发光混合和强度、零/负自发光门控、染料表回退与 FFXIVBinormal 转换。202 项单元测试通过，插件 Release 构建 0 警告、0 错误。扩充 SM5 谓词解释后，既有 68 组皮肤 oracle 的 263 项预期数值保持完全相同。

## lightshaft.shpk 光束

SM5 SHPK SHA256：`BEF224EBD302F5101E4E29C9DBA86192DF1A01C7366CF0CBCA7893F6C27DF6D0`。
`com_lightshaft_0a.mtrl` 的 SqPack 索引哈希为 `D4CB89194767D51C`；Type1，IsTransparent=false，g_Color=(1.2,1.2,1.2)，g_AngleClip=0.025，g_TexU=(0,0,-0.25)，g_TexV=(0,-0.25,0)。可用 `ShpkProbe <game> <out> --material-name com_lightshaft_0a.mtrl` 提取，再用 `--materials` 读取参数。

PS0：`RGB = Sampler0(UV0).rgb * interpolatedColor * Sampler1(projectedUV).rgb`，输出 Alpha 恒为 0。纹理 Alpha 不参与覆盖率。导出为 UE Unlit / Additive，EmissiveColor 接 RGB 乘积，Opacity=1（UE 的 Additive 会再次乘 Opacity）；不能按 IsTransparent=false 导出 Opaque，也不能把 PS 的零 Alpha 直接接 UE Opacity。两张纹理按原始数值采样，关闭硬件 sRGB 转换。

VS0 世界投影为 `dot(rotate * g_TexU, worldPosition)`、`dot(rotate * g_TexV, worldPosition)`，再加 `round_ne(g_TexAnim) * g_Parameter[0].w`。旋转不含缩放。导出将 XIV (x,y,z) 转换成 UE (-z,x,y)，世界位置厘米转米；通过 VertexInterpolator 在顶点阶段调用 UE 的实例变换，避免像素阶段 LocalToWorld 只含组件变换而忽略 ISM 的旋转。角度权重在顶点阶段计算：`vertexAlpha * ((viewForward·normal)^2 * (1-g_AngleClip) + g_AngleClip)`，然后插值。

这是静态 authored-ray 适配，不是完整游戏光束渲染：动画相位固定为零，实例 tint、near-clip fade、游戏曝光系数取 1。Type1 的太阳方向驱动顶点变形、法线旋转、太阳颜色和方向衰减依赖额外运行时常量缓冲，尚未导出。formula.json 保存原始 Metadata 与 Adaptation，避免将这些缺失项隐藏。当前没有把 crystal、water 或 bguvscroll 标记为已还原。

`tools/ShpkProbe/make_lightshaft_cases.py` 直接执行 PS0 反汇编生成 8 组预期值，覆盖 Type0/Type1、纹理 Alpha=0/1、零/非零顶点衰减。`NativeAssetProbe` 编译完整生产图，并用固定 VS 插值量单独验证 PS 输出：UE 5.8.3 / D3D12 / SM6，16 项 GPU 读回无差异。此测试验证像素算术及完整材质编译，不等于 Type1 运行时光照或世界投影的像素级游戏对照。
