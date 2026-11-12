# UE 5.8.3 标准资产导出

此版本直接在 Meddle 内生成 UE 5.8.3 可编辑资产，替代旧 ABI v4 JSON/原始资源 Bundle。生产导出不启动 UE，不依赖 FFXIVImporter，也不通过外部 FBX/glTF 导入器生成资产。内部复用角色 Composer 的内存场景，以保持现有蒙皮、种族变形和附件处理。

## 使用

- **Territory → Export UE Assets → UE 子关卡（保留原点与实例）**：直接离线生成 `Content/Meddle/Territory_<ID>/Territory_<ID>.umap`，无需 UE 安装或编辑器进程。每个分组一个 Actor，按资源/外观分成 ISM 组件，网格保留局部原点，实例保存相对变换。组件材质覆盖支持同一几何的不同染色；镜像实例单独分组件。关闭该选项恢复烘焙合并网格。完整复制 Content/Meddle 后可打开地图，或作为子关卡/Level Instance 源使用。此入口只包含已采集静态模型，SGB 已展开，不重建原始父子层级或 NPC。

- **Live Character → Export UE Assets**：弹出与 Export All 同样风格的设置菜单，选择“模型组合”，再点击 Export。导出 Skeleton、SkeletalMesh（包含形态目标）、Texture2D 和带属性计算图的标准 Material。
- 角色页的 UE 入口适用于可解析的 CharacterBase，包括人形 NPC、怪物、坐骑、宠物及单独的武器/附件，不再仅显示在人形模型分支。
- **Layout → 单个/选中/筛选实例的 Export → Export UE Assets**：支持角色、背景物件、家具/共享组和地形板块。每个所选顶层实例独立输出，父组与子实例同时选中时去重；保留场景位置，使用同样的“全部合并 / 按部位合并 / 不合并”选项。只有全部合并使用实例名，其他模式使用部位或源模型名；重名添加数字后缀。
- 无蒙皮物件（包括 Territory 地形、建筑和普通场景物件）输出 `SM_` StaticMesh，不生成骨架。带蒙皮的角色等实例输出 `SK_` SkeletalMesh。混合组按是否带蒙皮分别合并，不把静态场景绑定到虚拟根骨骼。灯光、相机、投影贴花不在 UE 模型导出范围；未支持的 SHPK 使用基础材质并在 `.formula.json` 中说明 Gap。
- **Animation → Start Capture / Stop Capture → Export → UE 5.8.3 assets**：捕获游戏最终骨骼姿态，在插件内重采样为 30 或 60 fps AnimSequence。采集使用单调时钟；游戏帧率决定实际观测频率，插值不会补出未观测的物理运动。
- **Animation → Bake PAP to UE assets**：输入本地 PAP 和匹配 SKLB 的完整文件路径，选择 30/60 fps、原地或根运动，烘焙全部命名片段。此处“离线”指不播放片段；仍须在运行中的 FFXIV/Dalamud 内使用游戏 Havok 解码器。
- 将输出目录中的 `Content/Meddle` 整体复制到 UE 项目的 `Content/Meddle`，然后打开编辑器。保留相对路径；如需移动资产，在 UE 内容浏览器中移动以维护引用。

资产包路径固定在 `/Game/Meddle`，每个角色或动画组按以下目录组织：

```text
Content/Meddle/<CharacterOrClipGroup>/
  Meshes/SK_<Name>.uasset
  Meshes/SM_<Name>.uasset
  Skeletons/SKEL_<sha256>.uasset
  Materials/M_<index>_<material>.uasset
  Textures/T_<index>_<sampler>.uasset
  Animations/A_<Character>_<clip>_<indices>.uasset
```

模型组合与命名规则（下表为蒙皮模型；静态模型使用相同粒度，前缀为 `SM_`）：

| 选项 | 输出粒度 | 模型资产名称 |
| --- | --- | --- |
| 全部合并（默认） | 角色及附件合为一个 SkeletalMesh，保留材质槽 | `SK_<实例名>` |
| 按部位合并 | 同一角色按模型槽位合并；附件按所属实例分别分组 | `SK_Top`、`SK_Face`、`SK_Hair` 等；附件如 `SK_Weapon_Attach1` |
| 不合并 | 每个原始子网格分别输出 | `SK_<原始模型/子网格名>` |

只有全部合并时，模型资产使用实例名；另两种模式不加实例名前缀。同名资产添加数字后缀区分。导出容器目录仍按实例组织，实例名遵循现有名字覆盖/隐私设置。中文及其他 Unicode 字母保留，仅替换路径和资产名不允许的字符，不再将中文变成下划线。

全部合并会统一骨骼索引，对不同骨架中的重名骨骼增加后缀；按部位/不合并模式按实际骨架签名复用 Skeleton。网格、法线、切线及形态目标统一使用蒙皮绑定位置转换，避免仅有绑定偏移的附件落到原点。旧导出不会自动改名或重新组合，请重新导出到新目录。

XIV 的角色实例不要求存在一个可直接导出的共享骨架。角色导出根据 Composer 组合后的骨骼层级和蒙皮绑定矩阵构建 Skeleton；签名只在本角色目录内对相同拓扑与参考姿态去重。不同角色目录、参考姿态或附件骨架使用独立 Skeleton。独立导出的 PAP/录制动画不会因此自动绑定到已导出的角色：PAP 的 SKLB 骨架与实例局部骨架之间仍需匹配、映射或使用 UE 标准重定向，不能仅依靠同名骨骼。

新目录布局用于后续导出。已有导出中的文件和内部包引用不会自动迁移，请重新导出到新目录，或在 UE 内容浏览器中移动旧资产。

## 数据约定

头发法线与透明覆盖已依据 hair.shpk 单独修正，包含当前工程材质的备份位置、额外裁剪 pass 及 UE 近似边界，见 [头发逆向记录](UE_HAIR_REVERSE.md)。

- 坐标：输入米制 Y-up 转为 UE 厘米 Z-up，`(x,y,z) → (-z,x,y)*100`；同步转换骨骼、网格、法线、切线和三角形绕序。
- 骨骼：父节点先于子节点，增加 `Meddle_Root`，按深度和名称排序；保留非均匀缩放。捕获期间骨架拓扑改变会中止导出，需重新录制。
- 动画：标准 AnimSequence + AnimDataModel，保存可编辑逐骨骼 TRS 轨道。根运动放在 `Meddle_Root`；PAP 的提取运动使用 Havok reference frame，未提取时从根骨轨道生成。缺失骨骼轨道使用参考姿态。
- 网格：蒙皮模型使用标准 SkeletalMesh imported LOD0，最多 4 组 UV、12 个蒙皮影响，保留顶点颜色和 MorphTarget。静态模型使用标准 StaticMesh RawMesh 源数据，保留材质槽、法线、切线、顶点颜色和最多 4 组 UV；UE 首次加载时转换为 MeshDescription 并构建派生渲染数据，保存后使用当前源数据格式。
- 纹理：可编辑 BGRA8 / RGBA32F source，使用 UE 支持的非虚拟化内联 bulk 编码。颜色贴图 sRGB，打包法线、Mask、Index 等线性；Index/Tile 最近邻。UE 加载/保存时升级为当前源数据格式。
- 材质：按固定 MeddleTools 节点图移植装备、皮肤、头发、虹膜的属性算术。颜色因子在图内计算，法线保留 B/A 遮罩，头发双 UV、虹膜顶点色左右眼选择、现代色表及 Tile/Decal 运算见 [UE_MATERIAL_FORMULAS.md](UE_MATERIAL_FORMULAS.md)。32 行色表的 characterlegacy、tattoo、occlusion 已依据本机 SHPK 增加属性适配，见 [逆向记录](UE_SHPK_REVERSE.md)。现代装备的 MultiMaterial 不要求 Diffuse，Compatibility 仍要求。缺必要资源和未核实变体保留基础导出；材质旁 `.formula.json` 的 Gap 记录缺口、Adaptation 记录引擎适配限制。Blender/UE 的 BRDF、SSS、IOR 与透明混合并不等价，不宣称游戏画面一致。
- PAP 输出骨骼变换轨道；不转换游戏事件、音效、特效及浮点槽为 UE 通知或曲线。

## 构建

```powershell
git submodule update --init third_party/UAssetAPI
dotnet build Meddle.sln -c Release
dotnet test Meddle/Meddle.Utils.Tests -c Release
```

UAssetAPI 固定到 `05910ba8f6d278201120e10d87adbb06dfb3a097`（MIT），只负责包表和基础二进制写入；标准 Engine 资产的序列化实现在 `Meddle.Unreal`。包头模板是自建测试资产的空包元数据，不包含 UE 源码或游戏资源。发行包须包含 Meddle.Unreal、UAssetAPI 及其依赖。

## 验证

```powershell
./tools/NativeAssetProbe/validate.ps1 -EngineRoot 'D:/UE/Source/UnrealEngine-5.8.3-release'
./tools/NativeAssetProbe/validate_formulas.ps1 -EngineRoot 'D:/UE/Source/UnrealEngine-5.8.3-release'
```

该验证单独创建临时 UE 工程，先由 .NET 写入资产，再由 UE 加载并检查顶点数、形态目标、纹理尺寸、材质连接、30/60 fps 动画多个时刻的数值和重新保存。UE 仅是验证工具，不是生产导出依赖。

本机已通过资产探针及 170 项托管单元测试。实例扩展探针在 UE 中检查无骨架重复实例、父子层级变换、非均匀/镜像缩放、三种合并粒度，以及角色和刚性物件混合时的完整顶点数据。材质另有原有 119 组/714 属性检查，以及修正参照后的 SHPK 74 组/182 属性检查，见 [逆向记录](UE_SHPK_REVERSE.md)。最新四个 legacy 材质修补副本已通过 UE 编译保存，游戏内实例选择操作及最终材质外观仍需实测。

Havok ABI 静态核对使用游戏版本 `2026.11.03.0000.0000`，`ffxiv_dx11.exe` SHA256 为 `52042b60631cf2c48851b3aaee36e9ec4fedd6d7f2c68f0e9a8c3befb0af9f98`。该版本 `hkaAnimation::sampleTracks` 和 `hkaAnimatedReferenceFrame::getReferenceFrame` 均为虚表槽 4；槽 3 是删除对象的函数，不能沿用其他游戏的 Havok 槽位。游戏更新后需重新核对原生 ABI。
