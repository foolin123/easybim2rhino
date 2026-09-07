using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EasyBIM2Rhino
{
    /// <summary>
    /// 读取 EasyBIM 导出的临时文件并转换为 Rhino 网格与材质。
    /// 路径/签名/版本约定必须与 EB 侧 EBPlugIn.RhinoPlugIn.RhinoPlugIn 保持一致：
    ///   目录: %LocalAppData%\EasyBIM\RhinoPlugIn
    ///   数据: eb_to_rhino.tmpData
    ///     [4B "EBRH"][1B major][1B minor]
    ///     minor>=1: [int materialCount]{[string name][byte R][byte G][byte B][byte A][float opacity][string texName][int texDataLen][byte[] texData]}
    ///     [int meshCount]{ [int materialIndex(-1=无)] [int vc][float x,y,z*vc][int tc][int a,b,c*tc] }
    ///   握手: eb_to_rhino.marker  "1"=有数据待导入；"0"=已消费
    /// 文件内坐标为EB模型坐标(mm)；opacity 为不透明度，此处转 transparency=1-opacity
    /// 贴图以压缩原始字节(PNG/JPG)写到 .3dm 所在目录的 EasyBIM_Textures 子目录并引用（文件小、操作流畅）
    /// </summary>
    public class EBMeshImportCommand : Command
    {
        public override string EnglishName => "ImportEBModel";

        private const string FILE_SIGNATURE = "EBRH";
        private const byte CURRENT_MAJOR_VERSION = 1;

        private static string DataDir => Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "EasyBIM", "RhinoPlugIn");

        private static string DataPath => Path.Combine(DataDir, "eb_to_rhino.tmpData");

        private static string MarkerPath => Path.Combine(DataDir, "eb_to_rhino.marker");

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!File.Exists(MarkerPath) || !File.Exists(DataPath))
            {
                RhinoApp.WriteLine("未找到EasyBIM导出的数据，请先在EasyBIM中执行导出。");
                return Result.Cancel;
            }

            string marker = File.ReadAllText(MarkerPath).Trim();
            if (marker != "1")
            {
                string prompt = "EasyBIM导出的数据已导入过，是否再次导入？ [Enter=确认 Esc=取消]";
                string input = string.Empty;
                var rc = Rhino.Input.RhinoGet.GetString(prompt, true, ref input);
                if (rc != Result.Nothing)
                {
                    RhinoApp.WriteLine("已取消导入。");
                    return Result.Cancel;
                }
            }

            try
            {
                int imported = 0;
                using (var fs = new FileStream(DataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                using (var reader = new BinaryReader(fs, Encoding.UTF8))
                {
                    string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (signature != FILE_SIGNATURE)
                    {
                        RhinoApp.WriteLine("数据文件签名错误: {0}", signature);
                        return Result.Failure;
                    }

                    byte major = reader.ReadByte();
                    byte minor = reader.ReadByte();
                    if (major != CURRENT_MAJOR_VERSION)
                    {
                        RhinoApp.WriteLine("不支持的数据版本: {0}.{1}", major, minor);
                        return Result.Failure;
                    }

                    // 材质表：文件内材料索引 → Rhino 文档材料索引
                    List<int> materialIndices = ReadMaterials(reader, doc, minor);

                    int meshCount = reader.ReadInt32();
                    for (int m = 0; m < meshCount; m++)
                    {
                        int materialIndex = minor >= 1 ? reader.ReadInt32() : -1;

                        var mesh = new Rhino.Geometry.Mesh();

                        int vertexCount = reader.ReadInt32();
                        for (int i = 0; i < vertexCount; i++)
                        {
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            mesh.Vertices.Add(x, y, z);
                        }

                        int triCount = reader.ReadInt32();
                        for (int t = 0; t < triCount; t++)
                        {
                            int a = reader.ReadInt32();
                            int b = reader.ReadInt32();
                            int c = reader.ReadInt32();
                            // 跳过错面：索引越界，或退化面（两个及以上顶点相同）
                            if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount || c < 0 || c >= vertexCount)
                                continue;
                            if (a == b || b == c || a == c)
                                continue;
                            mesh.Faces.AddFace(a, b, c);
                        }

                        // 确保封闭网格法线朝外，避免反面
                        mesh.UnifyNormals();
                        if (mesh.IsClosed)
                        {
                            var vmp = VolumeMassProperties.Compute(mesh);
                            if (vmp != null && vmp.Volume < 0)
                            {
                                mesh.Flip(true, true, true);
                            }
                        }

                        mesh.Normals.ComputeNormals();
                        mesh.Compact();

                        var attributes = new ObjectAttributes();
                        if (materialIndex >= 0 && materialIndex < materialIndices.Count)
                        {
                            attributes.MaterialIndex = materialIndices[materialIndex];
                            attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
                        }

                        doc.Objects.AddMesh(mesh, attributes);
                        imported++;
                    }
                }

                doc.Views.Redraw();
                File.WriteAllText(MarkerPath, "0"); // 消费标记
                RhinoApp.WriteLine("已从EasyBIM导入 {0} 个网格。", imported);
                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("导入EasyBIM数据失败: {0}", ex.Message);
                return Result.Failure;
            }
        }

        /// <summary>
        /// 读取材料表并加入 Rhino 文档，返回 文件材料索引 → 文档材料索引 的映射。
        /// 贴图以压缩原始字节写到 .3dm 所在目录的 EasyBIM_Textures 子目录并引用（不内嵌，避免 DIB 膨胀）。
        /// </summary>
        private static List<int> ReadMaterials(BinaryReader reader, RhinoDoc doc, byte minor)
        {
            var materialIndices = new List<int>();
            if (minor < 1) return materialIndices;

            int materialCount = reader.ReadInt32();
            for (int i = 0; i < materialCount; i++)
            {
                string name = reader.ReadString();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                byte a = reader.ReadByte();
                float opacity = reader.ReadSingle();
                string textureName = reader.ReadString();
                int textureDataLength = reader.ReadInt32();
                byte[] textureData = null;
                if (textureDataLength > 0)
                {
                    textureData = reader.ReadBytes(textureDataLength);
                }

                var material = new Rhino.DocObjects.Material();
                material.Name = string.IsNullOrEmpty(name) ? ("EB_" + i) : name;
                material.DiffuseColor = System.Drawing.Color.FromArgb(a, r, g, b);
                // EB 存不透明度(1=不透明)，Rhino transparency 为 0=不透明/1=全透明
                material.Transparency = 1.0 - opacity;

                if (textureData != null && textureData.Length > 0)
                {
                    string dir = TextureOutputDir(doc);
                    string safeName = string.IsNullOrEmpty(textureName)
                        ? ("EB_tex_" + i)
                        : textureName.Replace('/', '_').Replace('\\', '_');
                    string texturePath = Path.Combine(dir, safeName);
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(texturePath, textureData);
                        material.SetBitmapTexture(texturePath);
                    }
                    catch
                    {
                        // 写贴图失败时保留纯色材质
                    }
                }

                materialIndices.Add(doc.Materials.Add(material));
            }

            return materialIndices;
        }

        /// <summary>
        /// 贴图输出目录：优先 .3dm 所在目录的 EasyBIM_Textures 子目录；文档未保存则退回插件缓存目录
        /// </summary>
        private static string TextureOutputDir(RhinoDoc doc)
        {
            string docPath = doc?.Path;
            if (!string.IsNullOrEmpty(docPath))
            {
                string docDir = Path.GetDirectoryName(docPath);
                if (!string.IsNullOrEmpty(docDir))
                {
                    return Path.Combine(docDir, "EasyBIM_Textures");
                }
            }
            return Path.Combine(DataDir, "Textures");
        }
    }
}