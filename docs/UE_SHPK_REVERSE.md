# 当前角色 SHPK 补全（2026-11-12）

## Dalamud-VFXEditor 资产解析对照补充

固定参考提交：[49012104303caae373ac4b37b385a3affedca472](https://github.com/0ceal0t/Dalamud-VFXEditor/tree/49012104303caae373ac4b37b385a3affedca472)。本轮只处理游戏资产解析、数据编码公式和导出元数据；不采用其预览 shader 或预览光照模型替换游戏公式。已有工作区的材质实现继续保留。

### 已补入的格式处理

| 项目 | 上游证据 | 本仓库改动 |
| --- | --- | --- |
| SHPK 额外阶段 | [ShpkFile.cs](https://github.com/0ceal0t/Dalamud-VFXEditor/blob/49012104303caae373ac4b37b385a3affedca472/VFXEditor/Formats/ShpkFormat/ShpkFile.cs) 在 VS、PS 后顺序读取 HS、DS、GS | 原来仅读取数量，现补齐对应记录和 blob；避免非零数量时后续 MaterialParams 错位。只有 VS 去除 DX9 的 4 字节 / DX11 的 8 字节额外头 |
| SHPK 0x0E01 alias cluster | 同文件及 [ShpkNodeAliasSubCluster.cs](https://github.com/0ceal0t/Dalamud-VFXEditor/blob/49012104303caae373ac4b37b385a3affedca472/VFXEditor/Formats/ShpkFormat/Nodes/ShpkNodeAliasSubCluster.cs) | 将原 `Unk141A` 通过 `NodeAliasClusterCount` 暴露；解析 cluster、subcluster、alias 和未解释的尾部数据。保留原字段兼容性，不推测未知字的语义 |
| MTRL 色表尺寸 | [MtrlFile.cs](https://github.com/0ceal0t/Dalamud-VFXEditor/blob/49012104303caae373ac4b37b385a3affedca472/VFXEditor/Formats/MtrlFormat/MtrlFile.cs) 的 `ReadTable` | 正确屏蔽尺寸之外的 flags；支持未编码尺寸的现代色表；未知尺寸保留 DataSet，不再按旧表解释。显式 0x42 且附染色表的情况也保留原数据 |
| MTRL Sampler | [MtrlSampler.cs](https://github.com/0ceal0t/Dalamud-VFXEditor/blob/49012104303caae373ac4b37b385a3affedca472/VFXEditor/Formats/MtrlFormat/Shader/MtrlSampler.cs) | 新增 U/V AddressMode、LodBias、MinLod、UnknownFlags；导出到 `<usage>_Sampler` 元数据，并在 ShpkProbe 中记录。保持原 12 字节二进制布局 |

### 解码与参数寻址公式

令 `F` 为小端 AdditionalData flags：

- `logs = (F >> 4) & 255`，`width = 2^(logs & 15)`，`height = 2^(logs >> 4)`；宽度单位是 RGBA half texel，每 texel 8 字节。`0x53 → 8×32 → 2048` 字节；`0x42 → 4×16 → 512` 字节。`logs=0` 是未显式编码，按 DataSet 大小识别，不能套用为 1×1。
- `HasTable = (F & 4) != 0`，`HasDyeTable = (F & 8) != 0`。尺寸只占 bits 4..11，例如 `0xA534` 与 `0x0534` 都是现代色表。

令 `S` 为原始 sampler flags：

- `U = S & 3`，`V = (S >> 2) & 3`；0/1/2/3 分别为 Wrap/Mirror/Clamp/Border。
- `q = (S >> 10) & 1023`；`LodBias = (q >= 512 ? q-1024 : q)/64`，范围 `[-8, 7.984375]`，步长 `1/64`。不能将它作为无符号数解释。
- `MinLod = (S >> 20) & 15`；`UnknownFlags = S & ~0x00FFFC0F`。未知位原样保留。
- 这批值目前是解析和导出元数据；UE 属性公式仍使用原有经过验证的采样设置。本轮没有实现每个材质实例独立的寻址/LOD sampler，也不宣称由此完成游戏过滤行为还原。

SHPK `MaterialParam.ByteOffset/ByteSize` 与 MTRL `Constant.ValueOffset/ValueSize` 都以字节计。float 索引为 `offset/4`，元素数为 `size/4`；前者指向包默认参数缓冲，后者指向材质自己的 ShaderValues，不能将两个 offset 混用。合并顺序仍为包默认值，再按参数 ID 覆盖材质值。现代 TileIndex 的既有解码 `int(halfValue*64)` 与上游一致；写入中心值为 `(index+0.5)/64`。

0x0E01 cluster 的字段顺序为 `SubView2, SubView1, subClusterCount, unknown`（各 uint32）。每个 subcluster 是 `int16 OwnIndex + uint16 aliasCount + 97 个 uint32`；一个 alias 占 2 个 uint32，尾部保留 `97-2*aliasCount` 个字。因此每个 subcluster 固定 392 字节，最多容纳 48 个 alias。

### 不直接迁入的差异

- 上游 `ShpkNode` 将两个未知 uint 放在 pass 列表之后；本机 0x0E01 的现有解析在 key 数组之前读取它们，解析出的场景键、材质键与包定义对应。以 tattoo 首节点为例，9 个 SceneKeys 与默认值逐项相等，MaterialKeys 为 `4242B842/DFE74BAC`，两个 subview 值为 `B18FE63D/F43B2F35`。本轮保留本机数据支持的布局，未将上游顺序机械替换进来。
- 上游 `ParsedHalfShort` 将 Shader/Sphere 字段解释为 half 数值整数；本仓库目前保留原始 ushort 位值。两者有语义冲突，单凭编辑器命名不足以修改游戏公式，因此保留现状，等待对应采样字段的字节码及非零实物值验证。
- SHCD、AVFX、TMB 等是上游额外支持的资产类型；本次未引入它们的编辑器、播放或渲染逻辑。这里补齐的是已有 MTRL/SHPK 数据链路。

### 本轮验证

新增 `AssetParsingTests` 的 15 个用例，覆盖 0x0C01/0x0D01/0x0E01、非零 HS/DS/GS、VS 头部剥离、后续参数偏移、cluster 尾部、尺寸位屏蔽、未知色表和 LOD 正负边界。全套托管测试 **185 通过**；Release 解决方案构建 **0 警告、0 错误**。测试编译时另出现已有 `Names.cs` 未使用本地函数的 CS8321 提示。

本机 SqPack 再次读取 character、characterlegacy、charactertattoo、characterocclusion、hair，共 5 个 SHPK、2990 个 PS 和 15 个材质版本，现有属性图均生成成功。五个包都是 0x0E01，cluster 数分别为 4/3/1/1/3；本批真实包的 HS/DS/GS 数均为 0，非零额外阶段由合成二进制测试覆盖。输出在系统临时目录 `MeddleVfxComparison`，未提交原始游戏 shader。此轮验证格式解析和元数据；未重新执行 GPU 属性数值验证或真实角色视觉对比。

本次针对 `Character-已屏蔽角色-2026-11-12-01-09-55` 中的七个材质缺口，直接读取本机游戏 SqPack 的 `shader/sm5/shpk`，用仓库已有 `ShpkFile` 和 `D3DDisassemble` 提取资源绑定、常量偏移、键值、节点及汇编。工具为 `tools/ShpkProbe`；原始游戏资源与完整反汇编仅写入指定的本地输出目录，不提交仓库。

## 提取证据

| SHPK | Pixel shader 数量 | SHA256 |
| --- | ---: | --- |
| character | 1038 | `C2453552CE2527F1F92A9F62D04D2C5FBED67226306629826EDC0C1D9C646ABE` |
| characterlegacy | 1740 | `5F95AE36F1CFBF603F85817D6872190A56DDD323FB44ADE17779BBAF3A28049E` |
| charactertattoo | 8 | `827371207BD540546919D0E708DAAA2941DDAE77472AFAE993B9C3694A6233B7` |
| characterocclusion | 4 | `198AADDE1662E61390A827B2C2D6E71DE738923834A09F97D0E71F78C6F085E4` |

鞋子 `mt_c0101e0903_sho_a` 的原始 MTRL 只有 Normal、Mask、Index，默认 `GetValuesMultiMaterial`。Diffuse 只在 Compatibility 分支需要；之前的统一资源检查错误地将其判为缺图。

当前四个 legacy 材质（e5048_met、e0199_top、e0199_dwn、w1026）都使用 **32 行现代色表**。`characterlegacy.shpk` 与旧 16 行色表不是同一个概念。后者的自动转换仍不启用。

## 实现与对应指令

新增 `ShpkMaterial`；已有 skin/hair/iris 与现代 character 继续沿用原有 MeddleTools 公式，现代 character 仅修正 Diffuse 依赖。

- Legacy：PS130 / PS135 / PS151 / PS172 / PS214，默认顶点色分支另核对 PS1。Index 行号来自 `int((R*255+8)/17)`，G 在同对两行之间混合；读取两行各自的 TileIndex、TileAlpha 和完整 2×2 UV 矩阵，分别采样 Tile 后混合。法线按实际 `sqrt(max(.25-dot(rg-.5,rg-.5),1e-6))`、归一化及有符号混合法计算。基础颜色使用色表 × Mask.B²（Compatibility 使用 Diffuse.RGB²）× 顶点色 × TileOrb.B；Gloss 的输出为 `exp2(-GlossStrength/15)`。RGBA Decal 权重为 Decal.A × DecalColor.A，Alpha Decal 使用 Decal.G；权重同时衰减 Tile 强度。顶点色 Emissive 分支使用 G 作为漫反射权重、B 作为高光权重，Off 分支使用 RGB 着色。
- Tattoo：PS0 / PS1。颜色在顶点 RGB 与 OptionColor 之间按 Normal.B 混合，再乘 g_DiffuseColor²；覆盖率为 Normal.A × 顶点 A，小于 .001 丢弃。Decal 分支使用 UV1 的面绘变换、Decal.R、DecalColor.A，以及 OptionColor 的亮度权重。游戏输出的 sqrt 是 GBuffer 编码，UE BaseColor 接编码之前的线性值。
- Occlusion：PS0。颜色乘数为 `1+(vertex.G-1)*Normal.A*vertex.A`；低覆盖率返回中性乘数 1。游戏另写入高光遮蔽目标，UE 以 Unlit Modulate 表面材质适配颜色遮蔽，并将透明通道置于 BeforeDOF。参见 [Epic 的材质混合模式说明](https://dev.epicgames.com/documentation/unreal-engine/material-blend-modes-in-unreal-engine)。
- 上述三类 VS 都在顶点阶段将 RGB 平方、保留 Alpha。导出网格时按材质执行此变换；不能在像素阶段对已插值的颜色平方。UE 当前网格颜色存为 8 位，因此存在量化误差。
- Legacy Diffuse 以线性数据纹理导出，保留 shader 的显式平方，避免先 sRGB 解码后再次平方。

`.formula.json` 增加 `Unlit` 和 `Adaptation`；新分支的 `Reference` 标明 SHPK 来源。`FormulaEnabled=true` 表示已生成属性图，不代表与游戏渲染闭包等价。

## 明确的引擎适配

Legacy 的彩色高光以 Rec.709 亮度转换为 UE 标量 F0（除以 .08），Metallic 保持 0；它不是游戏高光模型的完整移植。Tattoo 在游戏中叠加 GBuffer，UE 使用 Translucent 表面；Occlusion 的 UE Modulate 不能独立改写已有表面的高光遮蔽。运行时消隐、投影、全局光照、Sphere 反射与游戏 BRDF 不在此次静态材质属性移植范围。未核实的 GetValues、SpecularType、VertexColorMode 分支继续报告 Gap。

## 验证与复现

`sm5_reference.py` 是独立的直线 SM5 指令解释器，直接执行提取到的汇编，不读取生产 HLSL。未知指令立即失败。采样器由合成纹理夹具提供；采用 float64 求值与 UE float32 结果比较，沿用属性容差 `max(.002, abs(expected)*.002)`。

`make_cases.py` 生成 74 组用例：16 对色表的三个混合权重、不同切片与非对角 UV 变换、Compatibility、两类 Decal、顶点色 Off、Alpha 边界、Tattoo / Occlusion 的低覆盖率和正常覆盖率，以及不依赖解释器计算期望值的平坦法线检查。比较线性 BaseColor、法线、Gloss 输出、Opacity 或遮蔽乘数；不把标量高光适配或最终光照称为游戏公式等价。

```powershell
dotnet run --project tools/ShpkProbe -c Release -- 'F:\FFXIV\FF14\game' "$env:TEMP/MeddleShpkAudit"
python tools/ShpkProbe/make_cases.py "$env:TEMP/MeddleShpkAudit" "$env:TEMP/MeddleShpkAudit/cases.json"
dotnet run --project tools/NativeAssetProbe -c Release -- "$env:TEMP/MeddleShpkGpu" "$env:TEMP/MeddleShpkAudit/cases.json"
& 'D:/UE/Source/UnrealEngine-5.8.3-release/Engine/Binaries/Win64/UnrealEditor-Cmd.exe' "$env:TEMP/MeddleShpkGpu/Probe.uproject" -run=pythonscript "-script=$PWD/tools/NativeAssetProbe/validate_formulas.py" -d3d12 -sm6 -AllowCommandletRendering -RenderOffscreen -unattended -nosplash -nosound
```

探针同时读取上述七个游戏材质模板（13 个存在的版本），合并 SHPK 默认键值、MTRL 覆盖及原始色表，检查图生成。公共 Tile/Decal 用绑定占位符提供；这项检查不代替含 MOD、染色及角色自定义参数的游戏内重新导出。

缺少新增节点、legacy 公共 Tile 和顶点色转换的旧导出，需加载新插件后重新导出整个角色。已有完整节点、但包含下述法线表达式错误的导出，可用 `repair_legacy_normals.py <导出目录>` 修补；工具只替换完全匹配的等长表达式，并在 Content 外保存资产与报告备份。

### 01:53 导出的法线修正

最新 `Character-已屏蔽角色-2026-11-12-01-53-13` 的 17 个材质没有公式缺图或回退，但四个 legacy 材质法线错误。[Microsoft 的 swapc 定义](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/swapc--sm5---asm-)是条件非零时交换两个源；生产 HLSL 和原参照解释器均把交换方向理解反了。正确混合为 `(1-abs(b))*(detail-sign(b))+sign(b)`，随后归一化并翻转 UE 的 Y 分量。错误公式会将平坦法线算成 `(0,0,-1)`，修正后为 `(0,0,1)`。

解释器的两个目标寄存器也改为读取所有源的快照后再写入，避免源/目标别名影响第二个结果。增加两项指令真值表测试及独立平坦法线 GPU 用例。之前的 901 属性通过结果使用了错误参照，其 legacy 法线结论已作废，不能作为此次修正依据。

上述最新导出的四个资产已原地修补，原件位于导出目录的 `normal-swapc-v1-backup`。在临时工程中验证修补资产的全部纹理引用、法线节点、UE 编译和保存通过；没有改写用户已打开工程中的副本。

### 本机验证结果

- Debug / Release 解决方案构建：0 警告、0 错误；170 项托管测试通过（含实例导出扩展）。
- 修正后 UE 5.8.3 / D3D12 / SM6：SHPK **74 组、182 个属性读回，0 不一致**，含独立平坦法线检查。
- Legacy、Compatibility、RGBA Decal、Tattoo、Occlusion 五类完整材质另行编译并保存，像素 shader 指令统计非零；属性代理之外也验证了实际 BlendMode / Unlit 的组合。
- 当前七个游戏材质模板共 13 个存在版本的图生成检查通过；未进行带当前角色 MOD、染色、自定义参数的重新导出与视觉验收。
- 数值结果：`artifacts/ue-shpk-numerics.json`；完整本地日志：`%TEMP%/MeddleShpkGpuV2/formulas.log`；实际导出修补验证：`%TEMP%/MeddleInstancesV2/repair.log`。
