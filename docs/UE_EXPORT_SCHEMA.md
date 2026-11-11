# UE 5.8.3 标准资产导出

此版本直接在 Meddle 内生成 UE 5.8.3 可编辑资产，替代旧 ABI v4 JSON/原始资源 Bundle。生产导出不启动 UE，不依赖 FFXIVImporter，也不通过外部 FBX/glTF 导入器生成资产。内部复用角色 Composer 的内存场景，以保持现有蒙皮、种族变形和附件处理。

## 使用

- **Live Character → Export UE Assets**：导出 Skeleton、SkeletalMesh（包含形态目标）、Texture2D 和基础 PBR Material。
- **Animation → Start Capture / Stop Capture → Export → UE 5.8.3 assets**：捕获游戏最终骨骼姿态，在插件内重采样为 30 或 60 fps AnimSequence。采集使用单调时钟；游戏帧率决定实际观测频率，插值不会补出未观测的物理运动。
- **Animation → Bake PAP to UE assets**：输入本地 PAP 和匹配 SKLB 的完整文件路径，选择 30/60 fps、原地或根运动，烘焙全部命名片段。此处“离线”指不播放片段；仍须在运行中的 FFXIV/Dalamud 内使用游戏 Havok 解码器。
- 将输出目录中的 `Content/Meddle` 整体复制到 UE 项目的 `Content/Meddle`，然后打开编辑器。保留相对路径；如需移动资产，在 UE 内容浏览器中移动以维护引用。

资产包路径固定在 `/Game/Meddle`，每个角色或动画组按以下目录组织：

```text
Content/Meddle/<CharacterOrClipGroup>/
  Meshes/SK_<Character>_<index>.uasset
  Skeletons/SKEL_<sha256>.uasset
  Materials/M_<index>_<material>.uasset
  Textures/T_<index>_<sampler>.uasset
  Animations/A_<Character>_<clip>_<indices>.uasset
```

XIV 的角色实例不要求存在一个可直接导出的共享骨架。角色导出根据 Composer 组合后的骨骼层级和蒙皮绑定矩阵构建 Skeleton；签名只在本角色目录内对相同拓扑与参考姿态去重。不同角色目录、参考姿态或附件骨架使用独立 Skeleton。独立导出的 PAP/录制动画不会因此自动绑定到已导出的角色：PAP 的 SKLB 骨架与实例局部骨架之间仍需匹配、映射或使用 UE 标准重定向，不能仅依靠同名骨骼。

新目录布局用于后续导出。已有导出中的文件和内部包引用不会自动迁移，请重新导出到新目录，或在 UE 内容浏览器中移动旧资产。

## 数据约定

- 坐标：输入米制 Y-up 转为 UE 厘米 Z-up，`(x,y,z) → (-z,x,y)*100`；同步转换骨骼、网格、法线、切线和三角形绕序。
- 骨骼：父节点先于子节点，增加 `Meddle_Root`，按深度和名称排序；保留非均匀缩放。捕获期间骨架拓扑改变会中止导出，需重新录制。
- 动画：标准 AnimSequence + AnimDataModel，保存可编辑逐骨骼 TRS 轨道。根运动放在 `Meddle_Root`；PAP 的提取运动使用 Havok reference frame，未提取时从根骨轨道生成。缺失骨骼轨道使用参考姿态。
- 网格：标准 SkeletalMesh imported LOD0，最多 4 组 UV、12 个蒙皮影响，保留顶点颜色和 MorphTarget。UE 首次加载时构建自身派生渲染数据。
- 纹理：可编辑 BGRA8 source，使用 UE 支持的非虚拟化内联 bulk 编码。UE 加载/保存时升级为当前源数据格式。
- 材质：漫反射与法线纹理、基础肤色/发色/虹膜色、透明度和双面设置。粗糙度默认为 0.6，金属度默认为 0。其余 mask/index/color-table 纹理也作为标准 Texture2D 保留；此版本不复刻 XIV 的专用着色器、染色表采样、头发高光、眼球/皮肤专用光照。贴图颜色因子在线性空间烘入漫反射。
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
```

该验证单独创建临时 UE 工程，先由 .NET 写入资产，再由 UE 加载并检查顶点数、形态目标、纹理尺寸、材质连接、30/60 fps 动画多个时刻的数值和重新保存。UE 仅是验证工具，不是生产导出依赖。

本机已通过该验证及托管单元测试。尚未在游戏内实测完整角色导出与 PAP 原生采样，也未进行真实角色材质的视觉验收。

Havok ABI 静态核对使用游戏版本 `2026.11.03.0000.0000`，`ffxiv_dx11.exe` SHA256 为 `52042b60631cf2c48851b3aaee36e9ec4fedd6d7f2c68f0e9a8c3befb0af9f98`。该版本 `hkaAnimation::sampleTracks` 和 `hkaAnimatedReferenceFrame::getReferenceFrame` 均为虚表槽 4；槽 3 是删除对象的函数，不能沿用其他游戏的 Havok 槽位。游戏更新后需重新核对原生 ABI。
