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
        HighQuality   // 精细
    }

    /// <summary>
    /// 网格设置工厂：以工厂模式提供「预设」与「自定义」两种方式生成 MeshingParameters。
    /// 预设对应 RhinoCommon 内置的 MeshingParameters 档位；自定义基于 Default 逐项覆盖。
    /// </summary>
    public static class MeshSettingsFactory
    {
        /// <summary>
        /// 按预设档位生成网格参数
        /// </summary>
        public static MeshingParameters CreatePreset(MeshPreset preset)
        {
            switch (preset)
            {
                case MeshPreset.Minimal:
                    return new MeshingParameters(MeshingParameters.Minimal);
                case MeshPreset.Coarse:
                    return new MeshingParameters(MeshingParameters.Coarse);
                case MeshPreset.Standard:
                    return new MeshingParameters(MeshingParameters.Default);
                case MeshPreset.Smooth:
                    return new MeshingParameters(MeshingParameters.Smooth);
                case MeshPreset.HighQuality:
                    return new MeshingParameters(MeshingParameters.QualityRenderMesh);
                default:
                    return new MeshingParameters(MeshingParameters.Default);
            }
        }

        /// <summary>
        /// 按自定义参数生成网格参数（基于 Default 覆盖）
        /// </summary>
        /// <param name="maxAngleDegrees">最大角度(度)，0 表示不覆盖</param>
        /// <param name="aspectRatio">最大长宽比，0 表示不覆盖</param>
        /// <param name="minEdgeLength">最小边缘长度</param>
        /// <param name="maxEdgeLength">最大边缘长度，0 表示不限制</param>
        /// <param name="tolerance">边缘至曲面的最大距离，0 表示自动</param>
        public static MeshingParameters CreateCustom(
            double maxAngleDegrees,
            double aspectRatio,
            double minEdgeLength,
            double maxEdgeLength,
            double tolerance)
        {
            var mp = new MeshingParameters(MeshingParameters.Default);

            if (maxAngleDegrees > 0)
            {
                mp.GridAngle = maxAngleDegrees * Math.PI / 180.0;
            }
            if (aspectRatio > 0)
            {
                mp.GridAspectRatio = aspectRatio;
            }
            mp.MinimumEdgeLength = minEdgeLength;
            mp.MaximumEdgeLength = maxEdgeLength;
            mp.Tolerance = tolerance;

            return mp;
        }
    }
}
