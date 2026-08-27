using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace EasyBIM2Rhino
{
    /// <summary>
    /// 把 Rhino 选中的 3D 实体导出到 EasyBIM 临时目录（rhino_to_eb.tmpData + marker）。
    /// 网格参数统一由 MeshDensitySettings（7 字段）驱动：预设仅加载初始值，可逐项修改；
    /// Density 直接对应 RelativeTolerance（0~1，越大越细）。
    /// </summary>
    public class EBModelExportCommand : Command
    {
        public override string EnglishName => "EBModelExport";

        private const string FILE_SIGNATURE = "EBRH";
        private const byte CURRENT_MAJOR_VERSION = 1;
        private const byte CURRENT_MINOR_VERSION = 1;

        private static string DataDir => Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "EasyBIM", "RhinoPlugIn");

        private static string DataPath => Path.Combine(DataDir, "rhino_to_eb.tmpData");

        private static string MarkerPath => Path.Combine(DataDir, "rhino_to_eb.marker");

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var selected = doc.Objects.GetSelectedObjects(false, false).ToList();
            if (selected == null || selected.Count == 0)
            {
                RhinoApp.WriteLine("请先选择要导出的三维对象（Brep/曲面/网格等）。");
                return Result.Cancel;
            }

            // 文本菜单循环：一行显示当前设置，Enter=执行，字母进入对应子项修改
            MeshPreset currentPreset = MeshPreset.Standard;
            var settings = new MeshDensitySettings();

            while (true)
            {
                string prompt = string.Format(
                    "网格选项 Preset(P)={0} Density(D)={1} GridAngle(A)={2} AspectRatio(R)={3} RefineAngle(F)={4} RefineGrid(F)={5} SimplePlanes(S)={6} JaggedSeams(J)={7}",
                    currentPreset, settings.Density, settings.GridAngle, settings.AspectRatio,
                    settings.RefineAngle, settings.RefineGrid ? "on" : "off",
                    settings.SimplePlanes ? "on" : "off", settings.JaggedSeams ? "on" : "off");

                string input = string.Empty;
                var rc = Rhino.Input.RhinoGet.GetString(prompt, true, ref input);
                if (rc == Result.Nothing) break;
                if (rc != Result.Success) return Result.Cancel;

                string cmd = (input ?? string.Empty).Trim().ToLowerInvariant();
                if (cmd.Length == 0) break;

                switch (cmd[0])
                {
                    case 'p':
                        string p = string.Empty;
                        rc = Rhino.Input.RhinoGet.GetString("Preset: [1]Minimal [2]Coarse [3]Standard [4]Smooth ", true, ref p);
                        if (rc == Result.Nothing) break;
                        if (rc != Result.Success) return Result.Cancel;
                        currentPreset = ParsePresetKey(currentPreset, p, settings);
                        break;
                    case 'd':
                        if (!PromptNum("Density (0~1)", ref settings.Density)) return Result.Cancel;
                        settings.Density = Math.Max(0.0, Math.Min(1.0, settings.Density));
                        break;
                    case 'a': if (!PromptNum("GridAngle (deg, 0=off)", ref settings.GridAngle)) return Result.Cancel; break;
                    case 'r': if (!PromptNum("AspectRatio (0=unlimited)", ref settings.AspectRatio)) return Result.Cancel; break;
                    case 'f': if (!PromptNum("RefineAngle (deg)", ref settings.RefineAngle)) return Result.Cancel; break;
                    case 'g':
                        settings.RefineGrid = !settings.RefineGrid;
                        RhinoApp.WriteLine("RefineGrid: {0}", settings.RefineGrid ? "on" : "off");
                        break;
                    case 's':
                        settings.SimplePlanes = !settings.SimplePlanes;
                        RhinoApp.WriteLine("SimplePlanes: {0}", settings.SimplePlanes ? "on" : "off");
                        break;
                    case 'j':
                        settings.JaggedSeams = !settings.JaggedSeams;
                        RhinoApp.WriteLine("JaggedSeams: {0}", settings.JaggedSeams ? "on" : "off");
                        break;
                }
            }

            MeshingParameters meshingParams = MeshSettingsFactory.CreatePreset(currentPreset);
            MeshSettingsFactory.ApplySettings(meshingParams, settings);

            var meshes = new List<Mesh>();
            var materialIndexPerMesh = new List<int>();
            var materials = new List<MaterialEntry>();
            var materialMap = new Dictionary<string, int>();

            foreach (var obj in selected)
            {
                CollectObject(obj, Transform.Identity, meshingParams, meshes, materialIndexPerMesh, materials, materialMap);
            }

            if (meshes.Count == 0)
            {
                RhinoApp.WriteLine("所选对象中没有可导出的三维几何（仅支持 Mesh/Brep/Extrusion/Surface/SubD）。");
                return Result.Cancel;
            }

            try
            {
                Directory.CreateDirectory(DataDir);
                using (var fs = new FileStream(DataPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8))
                {
                    bw.Write(Encoding.ASCII.GetBytes(FILE_SIGNATURE));
                    bw.Write(CURRENT_MAJOR_VERSION);
                    bw.Write(CURRENT_MINOR_VERSION);

                    // 材料表
                    bw.Write(materials.Count);
                    foreach (var m in materials)
                    {
                        bw.Write(m.Name ?? string.Empty);
                        bw.Write(m.R);
                        bw.Write(m.G);
                        bw.Write(m.B);
                        bw.Write(m.A);
                        bw.Write(m.Opacity);
                        bw.Write(string.Empty); // textureName（本方向暂不传贴图）
                        bw.Write(0);            // textureData 长度 0
                    }

                    // 网格表
                    bw.Write(meshes.Count);
                    for (int i = 0; i < meshes.Count; i++)
                    {
                        bw.Write(materialIndexPerMesh[i]);
                        WriteMesh(bw, meshes[i]);
                    }
                }

                File.WriteAllText(MarkerPath, "1");
                RhinoApp.WriteLine("已导出 {0} 个网格到 EasyBIM 临时目录，请在 EasyBIM 中执行导入。", meshes.Count);
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("导出失败: {0}", ex.Message);
                return Result.Failure;
            }
        }

        private static MeshPreset ParsePresetKey(MeshPreset currentPreset, string input, MeshDensitySettings settings)
        {
            string s = (input ?? string.Empty).Trim().ToLowerInvariant();

            switch (s)
            {
                case "1":
                case "minimal":
                    settings.Density = 0.0;
                    settings.GridAngle = 0.0;
                    settings.AspectRatio = 6.0;
                    settings.RefineAngle = 0.0;
                    settings.RefineGrid = false;
                    settings.SimplePlanes = false;
                    settings.JaggedSeams = true;
                    return MeshPreset.Minimal;

                case "2":
                case "coarse":
                    settings.Density = 0.65;
                    settings.GridAngle = 0.0;
                    settings.AspectRatio = 0.0;
                    settings.RefineAngle = 0.0;
                    settings.RefineGrid = true;
                    settings.SimplePlanes = true;
                    settings.JaggedSeams = false;
                    return MeshPreset.Coarse;

                case "3":
                case "standard":
                    settings.Density = 0.0;
                    settings.GridAngle = 20.0;
                    settings.AspectRatio = 6.0;
                    settings.RefineAngle = 20.0;
                    settings.RefineGrid = true;
                    settings.SimplePlanes = false;
                    settings.JaggedSeams = false;
                    return MeshPreset.Standard;

                case "4":
                case "smooth":
                    settings.Density = 0.8;
                    settings.GridAngle = 0.0;
                    settings.AspectRatio = 0.0;
                    settings.RefineAngle = 20.0;
                    settings.RefineGrid = true;
                    settings.SimplePlanes = true;
                    settings.JaggedSeams = false;
                    return MeshPreset.Smooth;

                default:
                    return currentPreset;
            }
        }

        private static bool PromptNum(string prompt, ref double value)
        {
            var gn = new GetNumber();
            gn.SetCommandPrompt(prompt);
            gn.SetDefaultNumber(value);
            gn.SetLowerLimit(0, false);
            gn.AcceptNothing(true);
            gn.Get();
            if (gn.CommandResult() == Result.Cancel)
            {
                return false;
            }
            if (gn.CommandResult() == Result.Success)
            {
                value = gn.Number();
            }
            return true;
        }

        private static void WriteMesh(BinaryWriter bw, Mesh mesh)
        {
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();

            int vertexCount = mesh.Vertices.Count;
            bw.Write(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                Point3f p = mesh.Vertices[i];
                bw.Write(p.X);
                bw.Write(p.Y);
                bw.Write(p.Z);
            }

            var tris = new List<int>();
            foreach (var face in mesh.Faces)
            {
                if (face.IsTriangle)
                {
                    tris.Add(face.A); tris.Add(face.B); tris.Add(face.C);
                }
                else
                {
                    tris.Add(face.A); tris.Add(face.B); tris.Add(face.C);
                    tris.Add(face.A); tris.Add(face.C); tris.Add(face.D);
                }
            }

            bw.Write(tris.Count / 3);
            foreach (int idx in tris) bw.Write(idx);
        }

        /// <summary>
        /// 递归收集对象：块实例递归展开（累积变换），其余转网格
        /// </summary>
        private static void CollectObject(RhinoObject obj, Transform xform, MeshingParameters mp,
            List<Mesh> meshes, List<int> materialIndexPerMesh, List<MaterialEntry> materials, Dictionary<string, int> materialMap)
        {
            if (obj == null) return;

            // 块实例：递归展开其定义内的对象，并累积变换
            if (obj is InstanceObject instanceObject)
            {
                Transform worldXform = xform * instanceObject.InstanceXform;
                InstanceDefinition idef = instanceObject.InstanceDefinition;
                if (idef != null)
                {
                    foreach (var child in idef.GetObjects())
                    {
                        CollectObject(child, worldXform, mp, meshes, materialIndexPerMesh, materials, materialMap);
                    }
                }
                return;
            }

            Mesh mesh = ToMesh(obj.Geometry, mp);
            if (mesh == null) return;

            if (xform != Transform.Identity)
            {
                mesh.Transform(xform);
            }

            meshes.Add(mesh);
            materialIndexPerMesh.Add(GetMaterialIndex(obj, materials, materialMap));
        }

        /// <summary>
        /// 几何 → Mesh；曲线/点/标注/文字/剖面线等返回 null（跳过）
        /// </summary>
        private static Mesh ToMesh(GeometryBase geometry, MeshingParameters mp)
        {
            if (geometry == null) return null;

            if (geometry is Mesh mesh) return mesh;
            if (geometry is Brep brep) return MeshBrep(brep, mp);
            if (geometry is Extrusion extrusion) return MeshBrep(extrusion.ToBrep(true), mp);
            if (geometry is SubD subD) return MeshBrep(subD.ToBrep(), mp);
            if (geometry is Surface surface) return MeshSurface(surface, mp);

            return null;
        }

        /// <summary>
        /// Brep → Mesh：整块网格化（密度/角度等由 mp 统一控制，Rhino 内部按面换算容差）
        /// </summary>
        private static Mesh MeshBrep(Brep brep, MeshingParameters mp)
        {
            return MergeMeshes(Mesh.CreateFromBrep(brep, mp));
        }

        /// <summary>
        /// 单曲面 → Mesh：直接按 mp 网格化
        /// </summary>
        private static Mesh MeshSurface(Surface surface, MeshingParameters mp)
        {
            return Mesh.CreateFromSurface(surface, mp);
        }

        /// <summary>
        /// CreateFromBrep(brep, mp) 返回 Mesh[]（每个面一个），合并成单个 Mesh
        /// </summary>
        private static Mesh MergeMeshes(Mesh[] meshes)
        {
            if (meshes == null || meshes.Length == 0) return null;
            var result = new Mesh();
            foreach (var m in meshes) result.Append(m);
            return result;
        }

        /// <summary>
        /// 取对象有效材质（对象材质，无则回退图层），去重后返回材料表索引
        /// </summary>
        private static int GetMaterialIndex(RhinoObject obj, List<MaterialEntry> materials, Dictionary<string, int> materialMap)
        {
            Rhino.DocObjects.Material mat = obj.GetMaterial(true);
            string name = string.IsNullOrEmpty(mat?.Name) ? ("RhinoMat_" + materials.Count) : mat.Name;
            System.Drawing.Color color = mat != null ? mat.DiffuseColor : System.Drawing.Color.Gray;
            float opacity = mat != null ? (float)(1.0 - mat.Transparency) : 1.0f;

            string key = name + "|" + color.ToArgb().ToString() + "|" + opacity.ToString("F3");
            if (materialMap.TryGetValue(key, out int existing)) return existing;

            int index = materials.Count;
            materialMap[key] = index;
            materials.Add(new MaterialEntry
            {
                Name = name,
                R = color.R,
                G = color.G,
                B = color.B,
                A = color.A,
                Opacity = opacity
            });
            return index;
        }

        private class MaterialEntry
        {
            public string Name;
            public byte R, G, B, A;
            public float Opacity;
        }
    }
}
