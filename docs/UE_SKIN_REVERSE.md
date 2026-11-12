# Skin SHPK / MTRL 属性公式（2026-11-12）

皮肤从 Blender 属性映射改为 `SkinMaterial` 的 SM5 字节码适配。范围是干燥皮肤的表面属性，输出 UE Default Lit；游戏次表面散射、湿润光照、Face2 动态皱纹权重和光照驱动的自发光增益仍未移植。

## 来源与分支

本机 `shader/sm5/shpk/skin.shpk`：384 个 PS、704 个节点，SHA256 `4E2F77DF0399DE59AF5E6383358CB7D071CA0C7734F2E161504B086239593E57`。下面的 PS 序号只适用于此版本。

| Material key | 属性 pass | 处理 |
| --- | --- | --- |
| GetMaterialValueFace | PS0 | 唇色、唇部粗糙度、UV1 Tile |
| GetMaterialValueBody | PS6 | Normal.A 额外控制 Tile 强度、UV0 Tile |
| GetMaterialValueBodyJJM | PS10 | 主色/挑染色、Mask.B=1 时关闭 Tile、UV0 Tile |
| GetMaterialValueFace2 | PS14 | 零皱纹权重的静态表面，与 Face 相同的唇部属性 |
| GetMaterialValueFaceEmissive | PS17 / PS19 | PS17 表面属性；PS19 的独立自发光输入项 |

检查的游戏 MTRL 包含 `mt_c1401f0003_fac_a/b`、`mt_c0201b0001_a`、`mt_c1401t0003_a`，也读取了本机模组的 Bibo MTRL。MTRL 参数覆盖 SHPK 默认值的既有解析顺序正确。缺项发生在属性映射层，而非浮点偏移解码。`g_LipRoughnessScale` 位于 byte 60，TileIndex/Alpha 位于 168/172，TextureMipBias 位于 256，TileMipBiasOffset 位于 304。

## 公式

`n/m/d` 分别表示 Normal/Mask/Diffuse，`v` 是游戏 VS 平方 RGB 后插值的顶点色（Alpha 不平方）。`saturate` 仅在字节码实际使用的位置应用。

Tile 强度为 `s = n.b * g_TileAlpha * v.a`；Body 额外乘 `n.a`；BodyJJM 在 `m.b >= 1` 时置零。ORB 以 `(1,0.5,1,1)` 为中性值，按 `saturate(s)` 混合。旧实现直接相加法线并只读取 ORB.B，遗漏了这些遮罩和 ORB.G/A。

基础法线强度为 `lerp(1, MuscleTone, v.r)`。RG 减去 0.5 后，用 `sqrt(max(0.25-dot(RG,RG),1e-6))` 重建 Z，然后才缩放 XY 并归一化。细节法线使用相同重建、强度 `s`，不读取 TileNormal.A。两者使用重定向混合：`b=base+(0,0,1)`、`t=detail*(-1,-1,1)`、`normalize(b*dot(b,t)/b.z-t)`。FFXIVBinormal 来源映射到 UE 为 `(Y,-X,Z)`。

Face/Face2 的 `lip=n.a*LipColor.a`，`r=m.g*lerp(1,g_LipRoughnessScale,lip)`；其他分支 `r=m.g`。令 `g=orb.g`，最终粗糙度为：

```text
g <= 0.5 : (1-2g)*r² + 2g*r
g >  0.5 : 2*(1-g)*r + (2g-1)*sqrt(r)
```

反射项是 `m.r*v.b*orb.a`，**它不是 F0**。进一步检查 SqPack hash `E3466A82CC18B55F` 的光照合成 PS0：GBuffer1.R 的标量有效区间通过 `255/254` 展开，干燥非金属的基础 F0 为 0.04，最后由反射项乘光照。UE 用 `Specular=0.5*saturate(m.r*v.b*orb.a*255/254)` 保留法向入射幅度；Metallic 为零。此转换不等价于游戏整个 Fresnel/BRDF，不能将 Mask.R 直接除以 UE 的 0.08。

Diffuse 和 `g_DiffuseColor` 均按游戏指令平方。角色 SkinColor/LipColor 等运行时颜色已是线性值，不再次平方。默认 tint 是 `lerp(1,SkinColor,n.b)`；Face 在乘 Diffuse 后混唇色；BodyJJM 按 PS10 顺序混主色和挑染。随后乘 ORB.B、使用 Decal.R×DecalColor.A 混贴花，最后乘 `g_DiffuseColor²`。取消旧的 Reinhard 转换和 Diffuse Alpha 对底色的混合。

FaceEmissive 的表面自发光输入是 `g_EmissiveColor²*n.a`。PS19 后续依赖场景光照的增益不属于静态材质属性。

Face Tile 使用 UV1，Body 使用 UV0；数组展平采样为 `(frac(U*scaleU),(trunc(index)+frac(V*scaleV))/64)`。不经过旧 Blender V 翻转。普通贴图与 Tile 的 MipBias 分开生成。现有数据纹理导出无 mip，因此此偏移只有在调用方提供 mip 时产生效果；当前数组最近邻采样不等价于游戏所有过滤情况。

## 导出与验证

`NativeAssetExporter` 将皮肤 Diffuse 保存为线性数据，由 HLSL 显式平方，避免先做 sRGB 解码。皮肤顶点 RGB 在网格导出时平方，Alpha 保持原值。皮肤 `.formula.json` 保存完整 Metadata，供以后追踪实际 MTRL 与角色参数；旧资产没有这些数据，需要重新导出才能获得精确运行时输入。

`make_skin_cases.py` 直接解释上述 PS 的算术指令作为参考，不复用生产 HLSL。覆盖五个分支、两种切线基、Tile.G 的 0/0.5/1、TileAlpha 的 0/2、MuscleTone 的 0/2、LipRoughnessScale=0、不同 UV/数组切片、BodyJJM 的 Mask.B=1 边界、Z 重建下限与自发光平方。

验证结果：68 组输入、263 个 UE D3D12/SM6 浮点属性读回，0 不一致；完整皮肤材质编译通过。原 Blender 数值生成器已排除皮肤，其他 shader 继续使用原参考。最终 198 个托管测试通过，Release 解决方案构建 0 警告、0 错误。结果摘要保存于 `artifacts/ue-skin-numerics.json`。

复现：

```powershell
dotnet run --project tools/ShpkProbe -c Release -- <game> <audit> skin
python tools/ShpkProbe/make_skin_cases.py <audit> <audit>/cases.json
dotnet run --project tools/NativeAssetProbe -c Release -- <probe> <audit>/cases.json
UnrealEditor-Cmd.exe <probe>/Probe.uproject -run=pythonscript -script=<repo>/tools/NativeAssetProbe/validate_formulas.py -d3d12 -sm6 -AllowCommandletRendering -RenderOffscreen -unattended -nosplash -nosound
```

`ShpkProbe <game> <output.json> --materials <paths.json>` 可读取明确指定的本地或游戏 MTRL；`--scan-lighting` 可按 SqPack hash 提取含 GBuffer 引用的 shader，复查上述反射项的使用。
