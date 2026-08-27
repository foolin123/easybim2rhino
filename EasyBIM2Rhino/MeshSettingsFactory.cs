using System;
using Rhino.Geometry;

namespace EasyBIM2Rhino
{
    /// <summary>
    /// 网格密度预设档位
    /// </summary>
    public enum MeshPreset
    {
        Minimal,      // 极简
        Coarse,       // 较少
        Standard,     // 标准
        Smooth,       // 较多
    }

    /// <summary>
    /// 自定义网格参数
    /// </summary>
    public class MeshDensitySettings
    {
        /// <summary>密度 0~1，默认 0.0</summary>
        public double Density = 0.0;

        /// <summary>网格阶段角度（度），默认 20</summary>
        public double GridAngle = 20.0;

        /// <summary>最大长宽比，默认 6</summary>
        public double AspectRatio = 6.0;

        /// <summary>细化阶段角度，越小越细（std=20°）</summary>
        public double RefineAngle = 20;

        /// <summary>细化开关</summary>
        public bool RefineGrid = true;

        /// <summary>平面简化</summary>
        public bool SimplePlanes = false;

        /// <summary>接缝不焊</summary>
        public bool JaggedSeams = false;

        /// <summary>最小边缘长度，默认 0.0001</summary>
        //public double MinEdgeLength = 0.0001;

        /// <summary>最大边缘长度，0 表示不限制</summary>
        //public double MaxEdgeLength = 0.0;

        /// <summary>边缘至曲面最大距离（显式），0 表示仅用密度公式</summary>
        //public double Tolerance = 0.0;
    }

    /// <summary>
    /// 网格设置工厂：预设对应 RhinoCommon 内置档位，用户 7 个字段统一覆盖最终参数。
    /// Density 直接对应 RelativeTolerance（0~1，越大越细），由 Rhino 内部按曲面尺寸换算容差。
    /// </summary>
    public static class MeshSettingsFactory
    {
        /// <summary>按预设档位生成网格参数</summary>
        public static MeshingParameters CreatePreset(MeshPreset preset)
        {
            switch (preset)
            {
                case MeshPreset.Minimal:
                    return MeshingParameters.Minimal;
                case MeshPreset.Coarse:
                    return MeshingParameters.FastRenderMesh;
                case MeshPreset.Standard:
                    return MeshingParameters.Default;
                case MeshPreset.Smooth:
                    return MeshingParameters.QualityRenderMesh;
                default:
                    return MeshingParameters.Default;
            }
        }

        /// <summary>
        /// 把用户 7 个字段覆盖到给定网格参数上（角度字段按「度」转弧度；密度直接对应 RelativeTolerance 0~1）。
        /// </summary>
        public static void ApplySettings(MeshingParameters mp, MeshDensitySettings s)
        {
            if (mp == null || s == null) return;

            mp.RelativeTolerance = Math.Max(0.0, Math.Min(1.0, s.Density));
            mp.GridAngle = s.GridAngle * Math.PI / 180.0;
            mp.GridAspectRatio = s.AspectRatio;
            mp.RefineAngle = s.RefineAngle * Math.PI / 180.0;
            mp.RefineGrid = s.RefineGrid;
            mp.SimplePlanes = s.SimplePlanes;
            mp.JaggedSeams = s.JaggedSeams;
        }
    }
}
