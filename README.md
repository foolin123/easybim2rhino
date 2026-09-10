# EasyBIM2Rhino

EasyBIM 与 Rhino 之间的双向模型交换插件。Rhino 端为 RhinoCommon 插件（`.rhp`），EB 端为 EasyBIM 插件模块，两侧通过本地临时文件交换数据。

支持 **Rhino 7** 与 **Rhino 8**（Windows）。

---

## 目录

- [功能概览](#功能概览)
- [数据交换机制](#数据交换机制)
- [Rhino 端命令](#rhino-端命令)
  - [EBModelExport](#ebmodelexportrhino--eb)
  - [ImportEBModel](#importebmodeleb--rhino)
- [EB 端命令](#eb-端命令)
- [二进制文件格式](#二进制文件格式)
- [材质与贴图](#材质与贴图)
- [单位处理](#单位处理)
- [编译与调试](#编译与调试)
- [Rhino 7 / 8 差异](#rhino-7--8-差异)
- [项目结构](#项目结构)
- [图标](#图标)
- [常见问题](#常见问题)

---

## 功能概览

| 位置 | 命令 | 方向 | 说明 |
|---|---|---|---|
| 🏗️ EB 端 | 选择导出 | EB → Rhino | 将选中构件导出到 Rhino |
| 🏗️ EB 端 | 全楼导出 | EB → Rhino | 将整楼模型导出到 Rhino |
| 🏗️ EB 端 | 导入 Rhino | Rhino → EB | 读取 Rhino 导出的数据 |
| 🦏 Rhino 端 | `EBModelExport` | Rhino → EB | 将选中对象导出到 EB |
| 🦏 Rhino 端 | `ImportEBModel` | EB → Rhino | 导入 EB 导出的数据 |

---

## 数据交换机制

两个方向不直接通信，而是通过本地临时目录下的文件 + 握手标记交换数据。

**目录：** `%LocalAppData%\EasyBIM\RhinoPlugIn\`

| 文件 | 方向 | 用途 |
|---|---|---|
| `rhino_to_eb.tmpData` | Rhino → EB | 网格与材质数据 |
| `rhino_to_eb.marker` | Rhino → EB | 握手标记，`1` 表示待导入 |
| `eb_to_rhino.tmpData` | EB → Rhino | 网格与材质数据 |
| `eb_to_rhino.marker` | EB → Rhino | 握手标记，`1` 待导入，`0` 已消费 |

**流程：**

```
导出方写入 tmpData → 写入 marker="1"
                          ↓
导入方检查 marker → 读取 tmpData → 写入 marker="0"
```

---

## Rhino 端命令

### `EBModelExport`（Rhino → EB）

把 Rhino 中选中的三维实体网格化后导出，供 EasyBIM 导入。

**使用步骤：**

1. 在 Rhino 中选中要导出的对象（Brep / 曲面 / 网格 / 拉伸体 / SubD）
2. 执行 `EBModelExport`
3. 命令行交互配置网格参数
4. 按 `Enter` 导出

**交互界面：**

```
网格选项  预设(P)=标准  密度(D)=0.65 最大角度(A)=20 最大长宽比(R)=0
网格细分(G)=是 平面最简化(S)=是 不对齐接缝(J)=否 [Enter=导出]
```

**按键说明：**

| 按键 | 参数 | 说明 |
|---|---|---|
| `P` | 预设 | 进入子菜单选择 [1]粗糙 / [2]标准 / [3]平滑 |
| `D` | 密度 | 0~1，越大网格越细 |
| `A` | 最大角度 | 网格阶段角度（度），0=默认 |
| `R` | 最大长宽比 | 0=不设限 |
| `G` | 网格细分 | 开关，切换是/否 |
| `S` | 平面最简化 | 开关，切换是/否 |
| `J` | 不对齐接缝 | 开关，切换是/否 |
| `Enter` | — | 确认导出 |
| `Esc` | — | 取消 |

**预设对照表：**

| 预设 | 密度 | 最大角度 | 长宽比 | 细分 | 平面简化 | 接缝 |
|---|---|---|---|---|---|---|
| 粗糙 | 0.50 | 0° | 0 | 是 | 是 | 否 |
| 标准 | 0.65 | 20° | 0 | 是 | 否 | 否 |
| 平滑 | 0.80 | 0° | 0 | 是 | 是 | 否 |

**支持的几何类型：** Mesh、Brep、Extrusion、SubD、Surface
**不支持（静默跳过）：** 曲线、点、标注、文字、剖面线

**块处理：** 块实例（Block/Instance）会被递归展开，累积变换后逐个网格化导出。

---

### `ImportEBModel`（EB → Rhino）

读取 EasyBIM 导出的数据，在 Rhino 中生成网格与材质。

**使用步骤：**

1. 在 EasyBIM 中执行"选择导出"或"全楼导出"
2. 在 Rhino 中执行 `ImportEBModel`
3. 自动导入所有网格与材质

**重复导入保护：** 已导入过的数据会弹出确认：

```
EasyBIM导出的数据已导入过，是否再次导入？ [Enter=确认 Esc=取消]
```

**容错处理：**

| 情况 | 处理 |
|---|---|
| 退化面（两个及以上顶点重合） | 自动跳过该面 |
| 面索引越界 | 自动跳过该面 |
| 贴图写入失败 | 保留纯色材质 |

---

## EB 端命令

### 选择导出

将 EB 中选中的构件网格与材质导出到 Rhino。遍历选中实体，递归处理链接三维（SolidRef）与打组实体（SolidGroup）。

### 全楼导出

将整楼模型导出到 Rhino。遍历范围：

- 主标准层（StanNum > 0）
- 标准层分身（StanNum < 0）
- 非标准层 / 其他视图（StanNum == 0）
- 空间层（SpaceView）
- 外部参照（EBRefData → SolidRef → 递归）

### 导入 Rhino

读取 Rhino 通过 `EBModelExport` 导出的 `rhino_to_eb.tmpData`，在 EB 中生成模型。

---

## 二进制文件格式

两个方向共用同一格式（签名 `EBRH`，版本 `1.1`）：

```
[4B ASCII "EBRH"] [1B major=1] [1B minor=1]

── 材料表 ──
[int materialCount]
每个材料:
  [string name]
  [byte R][byte G][byte B][byte A]
  [float opacity]          ← 不透明度，1=不透明
  [string textureName]
  [int texDataLen]
  [byte[] texData]         ← 原始图片字节（PNG/JPG），无贴图为 0

── 网格表 ──
[int meshCount]
每个网格:
  [int materialIndex]      ← 材料表索引，-1=无材质
  [int vertexCount]
  [float x][float y][float z] × vertexCount
  [int triCount]
  [int a][int b][int c] × triCount   ← 0 基顶点索引
```

**约定：**

- 文件内坐标为 EB 模型坐标（mm）
- BMeshOld 的 Y 为屏幕镜像值，写入时取反还原
- `opacity` 为不透明度，Rhino 侧转为 `transparency = 1 - opacity`

---

## 材质与贴图

**导出（Rhino → EB）：**

- 取对象材质（无则回退图层材质）
- 相同颜色+透明度+贴图的材质自动去重
- 贴图优先读外部文件；内嵌/程序纹理渲染为 PNG

**导入（EB → Rhino）：**

- 贴图以原始压缩字节写到 `.3dm` 所在目录的 `EasyBIM_Textures` 子目录
- 文档未保存时退回插件缓存目录 `%LocalAppData%\EasyBIM\RhinoPlugIn\Textures`
- 不内嵌 DIB，避免文件膨胀

---

## 单位处理

| 方向 | 逻辑 |
|---|---|
| Rhino → EB | 导出时检测文档单位，非毫米自动缩放至 mm，并提示缩放系数 |
| EB → Rhino | 导入时从 mm 自动缩放至 Rhino 当前文件单位 |

Rhino 文件本身为毫米时缩放系数为 1.0，不做任何变换。

---

## 编译与调试

### 编译配置

项目支持四套编译配置，通过条件编译符号区分 Rhino 版本：

| 配置 | 编译符号 | RhinoCommon |
|---|---|---|
| `Debug-Rhino7` | `RHINO_7` | 7.x |
| `Release-Rhino7` | `RHINO_7` | 7.x |
| `Debug-Rhino8` | `RHINO_8` | 8.x |
| `Release-Rhino8` | `RHINO_8` | 8.x |

**命令行编译：**

```bash
# Rhino 7
dotnet build -c Debug-Rhino7
dotnet build -c Release-Rhino7

# Rhino 8
dotnet build -c Debug-Rhino8
dotnet build -c Release-Rhino8
```

**Visual Studio：** 顶部工具栏配置下拉框选择对应配置。

### 调试

`Properties/launchSettings.json` 提供调试配置（本地文件，不提交 git）。将 `executablePath` 改为你本机的 Rhino 安装路径：

```json
{
  "profiles": {
    "Rhino 7": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\Rhino 7\\System\\Rhino.exe",
      "commandLineArgs": "/netfx"
    },
    "Rhino 8": {
      "commandName": "Executable",
      "executablePath": "C:\\Program Files\\Rhino 8\\System\\Rhino.exe",
      "commandLineArgs": "/netfx"
    }
  }
}
```

> 编译时若提示 `.rhp` 被占用，先关闭正在运行的 Rhino 再编译。

---

## Rhino 7 / 8 差异

通过 `#if RHINO_7` / `#if RHINO_8` 条件编译处理 API 差异：

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
├── EasyBIM2RhinoPlugin.cs     # 插件入口（PlugIn 派生类）
├── EBModelExportCommand.cs    # Rhino → EB 导出命令
├── EBMeshImportCommand.cs     # EB → Rhino 导入命令
├── MeshSettingsFactory.cs     # 网格参数预设与工厂
├── EmbeddedResources/
│   └── plugin-utility.ico     # 插件图标
├── Icons/                     # 功能图标（见下）
├── Properties/
│   ├── AssemblyInfo.cs        # 插件元信息
│   └── launchSettings.json    # 调试配置（本地，不提交）
├── gen_icons.py               # 图标生成脚本
├── .gitignore
├── README.md                  # 本文档
└── 插件.md                     # 精简版说明
```

---

## 图标

`Icons/` 目录提供五个功能图标，风格分别参考 EB（技术图纸风）与 Rhino（现代扁平风）：

| 文件 | 用途 | 风格 |
|---|---|---|
| `eb_select_export.png` | EB 选择导出 | 黑底 + 黄框 + 绿箭头 |
| `eb_overall_export.png` | EB 全楼导出 | 黑底 + 三层黄框 + 绿箭头 |
| `eb_import_rhino.png` | EB 导入 Rhino | 黑底 + 绿箭头 + 黄框 |
| `rhino_export.png` | Rhino `EBModelExport` | 浅底 + 深灰框 + 蓝箭头 |
| `rhino_import.png` | Rhino `ImportEBModel` | 浅底 + 蓝箭头 + 深灰框 |

需要调整时修改 `gen_icons.py` 后重新运行：

```bash
python gen_icons.py
```

---

## 常见问题

**Q: 导入后某个构件丢失？**
A: 早期版本 EB 端会导出退化面（顶点重合），Rhino 拒绝添加导致丢面。现已在导入端自动过滤退化面。如仍复现，请检查 EB 端导出日志。

**Q: 编译提示 `.rhp` 被占用？**
A: Rhino 正在运行并锁定了插件文件。关闭 Rhino 后重新编译。

**Q: 导入时提示"已导入过"？**
A: 数据已被消费（marker=0）。如需重复导入，按 `Enter` 确认即可；或在 EB 端重新导出。

**Q: 贴图没有显示？**
A: 检查 `.3dm` 所在目录的 `EasyBIM_Textures` 子目录是否存在贴图文件。文档未保存时贴图在插件缓存目录。

---

## 依赖

- .NET Framework 4.8
- RhinoCommon 7.x / 8.x（NuGet，编译期引用）
- Rhino 7 / Rhino 8（运行时）