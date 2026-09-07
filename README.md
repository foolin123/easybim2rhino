# EasyBIM2Rhino

EasyBIM 与 Rhino 之间的双向模型交换插件，支持 Rhino 7 和 Rhino 8。

## 功能概览

| 命令 | 方向 | 说明 |
|---|---|---|
| `EBModelExport` | Rhino → EB | 将 Rhino 中选中的三维实体导出到 EasyBIM |
| `ImportEBModel` | EB → Rhino | 将 EasyBIM 导出的模型导入 Rhino |

## 数据交换机制

两个方向通过 `%LocalAppData%\EasyBIM\RhinoPlugIn\` 目录下的临时文件进行握手：

| 文件 | 方向 | 用途 |
|---|---|---|
| `rhino_to_eb.tmpData` | Rhino → EB | Rhino 导出的网格数据 |
| `rhino_to_eb.marker` | Rhino → EB | 握手标记（`1`=待导入） |
| `eb_to_rhino.tmpData` | EB → Rhino | EB 导出的网格数据 |
| `eb_to_rhino.marker` | EB → Rhino | 握手标记（`1`=待导入，`0`=已消费） |

### 二进制文件格式

```
[4B ASCII "EBRH"] [1B major=1] [1B minor]

材料表 (minor >= 1):
  [int count]
  每个材料: [string name] [byte R] [byte G] [byte B] [byte A]
            [float opacity] [string texName] [int texDataLen] [byte[] texData]

网格表:
  [int count]
  每个网格: [int materialIndex] [int vertexCount]
            [float x,y,z × vertexCount]
            [int triCount] [int a,b,c × triCount]
```

- 坐标单位为 EB 模型坐标（mm）
- 材质透明度为不透明度（1=不透明），Rhino 侧转为 `transparency = 1 - opacity`
- 贴图以原始压缩字节（PNG/JPG）写入 `.3dm` 所在目录的 `EasyBIM_Textures` 子目录

---

## 命令详解

### Rhino → EB：`EBModelExport`

**使用步骤：**

1. 在 Rhino 中选中要导出的三维对象（Brep / 曲面 / 网格 / 拉伸体 / SubD）
2. 执行 `EBModelExport` 命令
3. 命令行交互配置网格参数：

```
网格选项 Preset(P)=Coarse Density(D)=0.65 GridAngle(A)=0 AspectRatio(R)=0
RefineAngle(F)=0 RefineGrid(G)=on SimplePlanes(S)=on JaggedSeams(J)=off [Enter=导出]
```

**交互按键：**

| 按键 | 功能 |
|---|---|
| `P` | 切换预设（Minimal / Coarse / Standard / Smooth） |
| `D` | 密度 0~1，越大越细 |
| `A` | 网格阶段角度（度），0=关闭 |
| `R` | 最大长宽比，0=不限制 |
| `F` | 细化阶段角度（度） |
| `G` | 细化开关 |
| `S` | 平面简化开关 |
| `J` | 接缝不焊开关 |
| `Enter` | 执行导出 |

**预设对照：**

| 预设 | Density | GridAngle | AspectRatio | RefineAngle | RefineGrid | SimplePlanes | JaggedSeams |
|---|---|---|---|---|---|---|---|
| Minimal | 0.0 | 0° | 6 | 0° | off | off | on |
| Coarse | 0.65 | 0° | 0 | 0° | on | on | off |
| Standard | 0.0 | 20° | 6 | 20° | on | off | off |
| Smooth | 0.8 | 0° | 0 | 20° | on | on | off |

**支持导出的几何类型：** Mesh、Brep、Extrusion、SubD、Surface

**不支持的类型：** 曲线、点、标注、文字、剖面线（静默跳过）

---

### EB → Rhino：`ImportEBModel`

**使用步骤：**

1. 在 EasyBIM 中执行导出（生成 `eb_to_rhino.tmpData` + `eb_to_rhino.marker`）
2. 在 Rhino 中执行 `ImportEBModel` 命令
3. 自动导入所有网格和材质

**重复导入保护：** 已导入过的数据会弹出确认提示：

```
EasyBIM导出的数据已导入过，是否再次导入？ [Enter=确认 Esc=取消]
```

**容错处理：**

- 退化面（两个及以上顶点指向同一坐标）自动跳过
- 索引越界的面自动跳过
- 贴图写入失败时保留纯色材质

**贴图输出：** 优先写到 `.3dm` 所在目录的 `EasyBIM_Textures` 子目录；文档未保存则退回插件缓存目录。

---

## 编译配置

项目支持 Rhino 7 和 Rhino 8 两套编译配置，通过条件编译符号区分：

| 配置 | 编译符号 | RhinoCommon 版本 |
|---|---|---|
| `Debug-Rhino7` | `RHINO_7` | 7.x |
| `Release-Rhino7` | `RHINO_7` | 7.x |
| `Debug-Rhino8` | `RHINO_8` | 8.x |
| `Release-Rhino8` | `RHINO_8` | 8.x |

```bash
# Rhino 7
dotnet build -c Debug-Rhino7
dotnet build -c Release-Rhino7

# Rhino 8
dotnet build -c Debug-Rhino8
dotnet build -c Release-Rhino8
```

### Rhino 7 vs Rhino 8 差异

| 功能 | Rhino 7 | Rhino 8 |
|---|---|---|
| PBR 材质槽位 | 不支持 | 支持 `PbrBaseColor` |
| 贴图渲染 | `TextureEvaluator` 逐像素采样 | `WriteToByteArray2` 直接导出 PNG |
| 退化面处理 | 导入时过滤 | 导入时过滤 |

---

## 项目结构

```
EasyBIM2Rhino/
├── EasyBIM2Rhino.csproj       # 项目文件（多配置条件编译）
├── EasyBIM2RhinoPlugin.cs     # 插件入口
├── EBModelExportCommand.cs    # Rhino → EB 导出命令
├── EBMeshImportCommand.cs     # EB → Rhino 导入命令
├── MeshSettingsFactory.cs     # 网格参数预设与工厂
├── Properties/
│   ├── AssemblyInfo.cs        # 插件元信息
│   └── launchSettings.json    # 调试配置（不提交 git）
└── .gitignore
```

## 依赖

- .NET Framework 4.8
- RhinoCommon 7.x / 8.x（NuGet）
- Rhino 7 / Rhino 8（运行时）