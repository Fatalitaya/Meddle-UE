# 场景灯光导出

Territory 导出解析 LVB/LGB/SGB 的类型 3 灯光，沿用层、节庆与分支过滤；嵌套 SGB 的灯光应用父级变换。当前区域的已加载灯光使用运行时参数和世界变换，未加载实例使用文件参数。住房补采包含灯光，并按已有实例身份去重。四种分组方式均保留灯光，灯光单独成组也可以导出。

## 标准化映射

| FFXIV 类型 | glTF / GLB | UE5 |
| --- | --- | --- |
| 1 Directional | directional | DirectionalLight |
| 2 Point | point | PointLight |
| 3 Spot | spot | SpotLight |
| 4 Plane | point 近似 | PointLight 近似 |
| 5 Line | point 近似 | PointLight 近似 |
| 6 Specular | directional 近似 | DirectionalLight 近似 |

FFXIV 的类型 1 具有全局照明语义，类型 6 仅作用于高光；目标格式的方向光不是完全等价表示。平面光具有斜切投影与截断范围，也不能仅凭名称等价为 UE RectLight。原始形状、投影纹理路径、阴影参数及 V700 扩展保留在元数据中；这些差异和投影纹理未转换会记入 Territory 报告。

glTF 使用 `KHR_lights_punctual`。UE 使用标准 Light Actor 和默认 `LightComponent0` 子对象，光源设为 Movable，无须 Lightmass 烘焙即可用于 Lumen。局部光使用 Candelas 和逆平方衰减，方向光使用引擎的原生强度；输入并无物理标定，因此保留的是相对亮度而非经过校准的坎德拉或照度。范围由米转换为厘米，聚光内外锥角由弧度转换为度，颜色以线性 RGB 转为 UE 的 sRGB FColor。阴影启用与高光开关映射到 UE 标准属性，不模拟游戏专有阴影规则。

完整 UE 子关卡包含模型和灯光。按组烘焙网格或 Layout 原生资产导出另存 `_Lights.umap`，保留世界位置。地图旁 `.lights.json` 保留转换结果及原始 extras；glTF 保留在节点 extras。OBJ 无法承载灯光。黑色或零强度灯光保留为零强度光源；方向光不因 Range 为零而丢失。

## 文件格式与当前客户端核对

参考 [SaintCoinach LgbLightEntry](https://github.com/lsl1225/SaintCoinach/blob/master/SaintCoinach/Graphics/Lgb/LgbLightEntry.cs) 和 [Lumina LayerCommon](https://github.com/NotAdam/Lumina/blob/master/src/Lumina/Data/Parsing/Layer/LayerCommon.cs)。SaintCoinach 大多字段仍为未知，Lumina 定义用于确定基础布局，没有引入这两个库作为新依赖。

使用本机 `2026.11.03.0000.0000` 的 `ffxiv_dx11.exe` 静态反汇编核对。SHA256：`52042b60631cf2c48851b3aaee36e9ec4fedd6d7f2c68f0e9a8c3befb0af9f98`。下列地址是该版本的 VA，不是跨版本签名。

- `0x140799730`：灯光文件参数应用到渲染光源；RGB 为 `(byte / 255 × HDRIntensity)²`，渲染强度乘数初始化为 1。
- `0x140799845`：Attenuation 按 `<=1.2`、`<=2.2`、`>2.2` 映射线性、平方、立方衰减。RangeRate 取至少 1 后写入 FalloffFactor，不能把它直接当作光照半径。
- `0x14026AC30`：`v = max(linearRGB) × multiplier / (FalloffFactor × 5/255)`；范围分别为 `v`、`sqrt(v)`、`pow(v,1/3)`。类型 1、6 使用 10000 的游戏范围。
- `0x140799934`：文件 `0x40` 对应角度衰减，`0x44` 对应聚光角度。导出内锥为聚光角，外锥为两者之和，并限制到目标格式允许范围。
- `0x140741102`、`0x1407999DA`：检查实例 `0x8C` 的 `0x56373030`（V700）标记。该版本 `0x68` 为 LightSelect，合并组移动到 `0x8A`；扩展包括阴影深度、偏置、渐隐等。旧格式仍读取 `0x68` 的 ushort 合并组。

glTF 强度分离采用 `peak=max(linearRGB)`，颜色为 `linearRGB/peak`，强度为 `peak×multiplier`，保留原始线性辐射量；不再使用 Reinhard 色调映射后乘向量长度。游戏朝向为局部 +Z，glTF 为 -Z，UE Light 为 +X；转换对齐光照方向，不把模型缩放再次乘入灯光范围。

## 验证

### 房屋灯光强度

当前室内快照读取 `IndoorTerritory.InvertedBrightness`，界面档位为 `5 - InvertedBrightness`。
六档的稳定倍率依次为 `0、0.2、0.4、0.6、0.8、1`；实际导出使用
`ActiveLayout.IndoorAreaData + 0x80` 的已应用倍率，以保留切换过程中的亮度。
不使用 `BrightnessCurrent/Target` 作为静止亮度：客户端直接设置亮度时会清零这些过渡字段。

运行时 `RenderLight.Color.Intensity` 已含房屋调光，导出保持原值，不再乘一次。
Territory 文件回退仅对客户端受控共享组及其子组内的灯光应用倍率，并按原衰减公式重新计算范围；
其他灯具不因身处房屋而统一调暗。受控组来自室内布局的 `OutdoorAreaData + 0x6F58`，
四个 vector 的步长为 `0xD0`。没有当前室内快照或受控组归属的纯文件导出保留资源默认值。
房屋档位与实际布局倍率保存在 `territory-report.json` 及灯光 extras 的 `HousingLighting` 中；
该元数据描述房屋状态，不表示所有灯光都受其控制。

本机客户端核对链：`0x140E1ECD0`（反向档位）、`0x140E38760`（六档查表与过渡）、
`0x1406F4030`（布局倍率）、`0x1406F94F0`（受控组）、`0x1406FE970`（子灯光递归）、
`0x140741440`（写入 RenderLight + 0x34）。受控组先传递 `1 - multiplier`，灯光 setter
再计算 `1 - 参数`，最终强度为倍率本身。详见 `artifacts/housing-light-audit.txt`。

### 检查命令与覆盖范围

本机全区域扫描遍历 19,340 个场景资源、58,421 个层，识别 98,637 个 V700 灯光，灯光类型分布：1=3、2=58,033、3=25,585、4=712、5=14,300、6=4；解析错误为 0。扫描发现两个缺失的 `bgcommon/world/kor/shared/for_bg/sgbg_w_kor_004_01{a,b}.sgb` 引用，未把它们计为成功解析。

复现检查：

```powershell
dotnet test Meddle/Meddle.Utils.Tests -c Release --filter 'FullyQualifiedName~SceneLight|FullyQualifiedName~Territory|FullyQualifiedName~LevelTransform|FullyQualifiedName~Localization'
dotnet run --project tools/TerritoryProbe -c Release -- <FFXIV安装根目录>
dotnet run --project tools/TerritoryExportProbe -c Release -p:DalamudLibPath=<Dalamud开发库目录>
tools/LevelAssetProbe/validate.ps1 -EngineRoot <UE5.8.3根目录>
```

验证覆盖旧版和 V700 字段、线性颜色与范围、GLB 往返、光源方向、UE 单位转换、零范围方向光、纯灯光原生资产导出、分组、运行时覆盖、住房激活状态，以及 UE 实际加载、保存重开和子关卡变换。不将这些结构验证视为与游戏画面逐像素一致的证明。
