using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace EasyBIM2Rhino
{
    /// <summary>
    /// 把 Rhino 选中的 3D 实体导出到 EasyBIM 临时目录（rhino_to_eb.tmpData + marker）。
    /// 网格密度支持「预设 5 档」与「自定义 5 项参数」，由 MeshSettingsFactory 生成 MeshingParameters。
    /// 支持 Mesh/Brep/Extrusion/Surface/SubD，块实例递归展开；曲线/点/标注等非三维对象跳过。
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

            // 网格密度：预设 或 自定义
            string presetInput = "标准";
            var rc = Rhino.Input.RhinoGet.GetString(
                "密度预设：极简/较少/标准/较多/精细/自定义（Enter=标准）",
                true, ref presetInput);
            if (rc != Result.Success) return Result.Cancel;

            bool isCustom = IsCustom(presetInput);
            string densityLabel;
            MeshingParameters meshingParams;

            if (isCustom)
            {
                double angle, aspect, minEdge, maxEdge, tolerance;
                if (!PromptNumber("最大角度(度，默认20)", 20.0, out angle)) return Result.Cancel;
                if (!PromptNumber("最大长宽比(默认6)", 6.0, out aspect)) return Result.Cancel;
                if (!PromptNumber("最小边缘长度(mm，默认0)", 0.0, out minEdge)) return Result.Cancel;
                if (!PromptNumber("最大边缘长度(mm，0=不限)", 0.0, out maxEdge)) return Result.Cancel;
                if (!PromptNumber("边缘至曲面最大距离(mm，0=自动)", 0.0, out tolerance)) return Result.Cancel;

                meshingParams = MeshSettingsFactory.CreateCustom(angle, aspect, minEdge, maxEdge, tolerance);
                densityLabel = "自定义";
            }
            else
            {
                MeshPreset preset = ParsePreset(presetInput);
                meshingParams = MeshSettingsFactory.CreatePreset(preset);
                densityLabel = "预设(" + presetInput + ")";
            }

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
                RhinoApp.WriteLine("已按{0}导出 {1} 个网格到 EasyBIM 临时目录，请在 EasyBIM 中执行导入。", densityLabel, meshes.Count);
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("导出失败: {0}", ex.Message);
                return Result.Failure;
            }
        }

        private static bool IsCustom(string input)
        {
            return (input ?? string.Empty).Trim() == "自定义";
        }

        private static MeshPreset ParsePreset(string input)
        {
            input = (input ?? string.Empty).Trim();
            switch (input)
            {
                case "极简": return MeshPreset.Minimal;
                case "较少": return MeshPreset.Coarse;
                case "标准": return MeshPreset.Standard;
                case "较多": return MeshPreset.Smooth;
                case "精细": return MeshPreset.HighQuality;
                default: return MeshPreset.Standard;
            }
        }

        private static bool PromptNumber(string prompt, double defaultValue, out double value)
        {
            value = defaultValue;
            var gn = new GetNumber();
            gn.SetCommandPrompt(prompt);
            gn.SetDefaultNumber(defaultValue);
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
            if (geometry is Brep brep) return MergeMeshes(Mesh.CreateFromBrep(brep, mp));
            if (geometry is Extrusion extrusion) return MergeMeshes(Mesh.CreateFromBrep(extrusion.ToBrep(true), mp));
            if (geometry is SubD subD) return MergeMeshes(Mesh.CreateFromBrep(subD.ToBrep(), mp));
            if (geometry is Surface surface)
            {
                Brep surfaceBrep = Brep.CreateFromSurface(surface);
                return surfaceBrep != null ? MergeMeshes(Mesh.CreateFromBrep(surfaceBrep, mp)) : null;
            }

            // Curve / Point / PointSet / Annotation / TextDot / Hatch / Light / ClipPlane / ... 跳过
            return null;
        }

        /// <summary>
        /// CreateFromBrep(brep, mp) 返回的是 Mesh[]（每个面一个），合并成单个 Mesh
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
