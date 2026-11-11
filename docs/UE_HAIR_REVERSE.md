# Hair SHPK：法线与剔除检查（2026-11-12）

检查对象是用户当前 `MeddleUE/Content/Meddle/已屏蔽角色` 中的 `M_6_mt_c0201h0156_hir_a_hair_shpk` 和 `M_10_mt_c1401f0003_etc_a_hair_shpk`。从正在运行的 UE 编辑器读取实际图与材质属性，原件均为 Translucent；前者双面，后者单面。工程没有保存运行时原始 MTRL，因此参数核对使用同名游戏模板；颜色与 AlphaThreshold 从当前 UE 图提取保留，NormalScale 和 MipBias 使用同名模板值。

本机 SqPack `shader/sm5/shpk/hair.shpk`：200 个 PS、1664 个节点，SHA256 `ED0C9953E6AA6DB7822A205E051C0628E2AD31BD87FB38E6B28FF042570A015F`。可用 `dotnet run --project tools/ShpkProbe -c Release -- <game> <output> hair` 提取。以下 PS 编号只适用于这个文件版本；不同 Scene/Material keys 会选用不同 pass，不代表每帧同时执行全部列出的 pass。

## MTRL 与法线

| 模板 | Flags | g_AlphaThreshold | g_NormalScale | g_TextureMipBias |
| --- | ---: | ---: | ---: | ---: |
| c0201h0156_hir_a v0001 | 28 / 0x1C | .75 | .5 | 1 |
| c1401f0003_etc_a | 29 / 0x1D | 1 | .5 | 0 |

Flags bit 0 控制隐藏背面，bit 4 开启透明相关路径。它不意味着应该把整个 UE 材质直接设为 Translucent。

PS1 / PS7 的法线为 `normalize(float3((RG-.5)*g_NormalScale, sqrt(max(.25-dot(RG-.5,RG-.5),1e-6))))`。先用未缩放的 RG 重建 Z，然后缩放 XY；旧导出忽略 NormalScale、固定 Z=1。

VS2 把解码后的顶点副切线 B 输出到 TEXCOORD3，用 `cross(B,N)*sign` 重建 T。现有 MeshBuilder 将 B 放入 glTF tangent，原生导出再作坐标反射并转换 handedness，最终 UE 切线基底是 `(B,-T,N)`。因此 hair 法线在这个已导出基底中要写成 `(ny,-nx,nz)`；仅翻转绿色通道不能纠正轴向。MaterialComposer 新增 `SourceTangentBasis=FFXIVBinormal`，hair 只在该来源标记下作此转换，合成标准切线输入仍用原有约定。没有修改网格拓扑或其他 shader 的切线解释。

检查的 hair shader 没有 SV_IsFrontFace 法线翻转；UE MaterialTemplate.ush 的双面材质会在切线转世界空间后乘 TwoSidedSign。hair Normal 输出额外乘同一个符号以抵消自动翻转。保留原 MTRL 对应的双面/单面状态。

## 游戏里还有哪些裁剪

| 代表 pass | 观察到的条件 |
| --- | --- |
| PS1 / PS7 主体 | `A * lerp(vertex.A,1,param_AD94E254) < g_AlphaThreshold` 时 discard；头发 A 取 UV1，Face 取 UV0 |
| PS2 等透明 GBuffer | 低于约 16/255 丢弃；正阈值下输出 `saturate(A/threshold)` |
| PS4 等透明边缘 | Alpha 大于等于主体阈值的区域丢弃，低于约 1/255 的区域也丢弃 |
| PS5 等前向路径 | 采样场景深度并与当前像素深度比较，另有约 1/255 的 Alpha 丢弃 |
| PS0 / PS6 深度相关路径 | 以 UV0 Alpha × 顶点 Alpha 对比 `max(g_AlphaThreshold,.5)`；不能仅凭存在 g_ShadowAlphaThreshold 参数断言该 pass 使用它 |
| PS31 等淡出变体 | 先按像素位置采样 4×4 周期的 g_SamplerDither，与实例参数比较，再进行材质 Alpha 裁剪 |

主体/边缘使用单独的 Alpha 采样，bias 为 `g_TextureMipBias + 全局bias - 4`，法线/颜色采样没有这个 -4。导出新增独立采样及 MipBias 连线，全局运行时 bias 按 0 处理。MTRL 参数 AD94E254 在 h0156 为 1；Face 模板缺省取 SHPK 默认 0。

## UE 修正及边界

透明 hair 采用 Masked + DitherOpacityMask，覆盖率为 `A<1/255 ? 0 : (threshold>0 ? saturate(A/threshold) : saturate(A))`。饱和区域稳定写深度，边缘通过 UE 抖动覆盖近似透明；未启用透明的 hair 保留二值阈值裁剪。这是 UE 的单材质适配，不是游戏主体/边缘/GBuffer 多 pass、场景深度合成或运行时屏幕淡出的完整实现。颜色和 Roughness 保留原有 Blender 映射，本次不宣称完整 hair BRDF 一致。

两个实际材质已通过编辑器 Python API 更新并保存。原图备份位于 `/Game/MeddleHairAudit/Before/`，验证副本位于 `/Game/MeddleHairAudit/Candidates/`；原 `.formula.json` 旁保留 `.json.before-hair-repair`。生产图来自 `CharacterMaterial.Build` 的 JSON 描述，`tools/ShpkProbe/apply_hair_graphs.py` 先编译副本，再备份并修改原图，不直接覆盖打开中的 uasset 文件。

## 验证

- `make_hair_cases.py` 生成 136 组 UE GPU 读回：Normal 对照 SM5 指令解释器，覆盖 RG 四种输入、四个 NormalScale、两种切线来源及 Hair/Face 分支；Opacity 检查主体与边缘边界、顶点 Alpha 参数。
- 平坦法线固定期望 +Z；独立几何测试从游戏 `cross(B,N)*sign` 推导世界法线，核对导出 `(B,-T,N)` 的转换，测试正负 handedness。
- 136 个属性读回无差异，结果 `artifacts/ue-hair-numerics.json`。完整 Masked 材质也检查 DitherOpacityMask 并编译保存。旧 Blender hair 参照不再验证已被 SHPK 实现替代的 Normal/Opacity，只保留未改动的颜色等输出检查。
- 当前工程的两个材质编译成功，像素指令分别为 216 / 207；没有把这些检查当作游戏与 UE 最终画面的视觉一致性验收。

```powershell
python tools/ShpkProbe/make_hair_cases.py "$env:TEMP/MeddleShpkAudit" "$env:TEMP/MeddleShpkAudit/hair-cases.json"
dotnet run --project tools/NativeAssetProbe -c Release -- "$env:TEMP/MeddleHairGpu" "$env:TEMP/MeddleShpkAudit/hair-cases.json"
# 在该临时工程中运行 tools/NativeAssetProbe/validate_formulas.py（D3D12 / SM6 / AllowCommandletRendering）。
```
