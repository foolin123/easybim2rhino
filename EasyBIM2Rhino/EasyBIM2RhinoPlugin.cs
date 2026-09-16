using Rhino;


namespace EasyBIM2Rhino
{
    ///<summary>
    /// <para>Every RhinoCommon .rhp assembly must have one and only one PlugIn-derived
    /// class. DO NOT create instances of this class yourself. It is the
    /// responsibility of Rhino to create an instance of this class.</para>
    /// <para>To complete plug-in information, please also see all PlugInDescription
    /// attributes in AssemblyInfo.cs (you might need to click "Project" ->
    /// "Show All Files" to see it in the "Solution Explorer" window).</para>
    ///</summary>
    public class EasyBIM2RhinoPlugin : Rhino.PlugIns.PlugIn
    {
        public EasyBIM2RhinoPlugin()
        {
            Instance = this;
            PurgeStaleToolbarCollections();
        }

        ///<summary>Gets the only instance of the EasyBIM2RhinoPlugin plug-in.</summary>
        public static EasyBIM2RhinoPlugin Instance { get; private set; }

        /// <summary>
        /// 清理历史残留：关闭"路径含 EasyBIM2Rhino 且文件已不存在"的工具栏集合
        /// （旧版本反复 Open 在工作区留下的重复项 00/01/02）。只删失效项，不碰任何有效集合。
        /// 工具栏本体由随 .rhp 同目录的 EasyBIM2Rhino.rui 由 Rhino 自动加载，此处不做打开操作。
        /// </summary>
        private static void PurgeStaleToolbarCollections()
        {
            try
            {
                foreach (Rhino.UI.ToolbarFile tf in Rhino.RhinoApp.ToolbarFiles)
                {
                    if (string.IsNullOrEmpty(tf.Path)) continue;
                    if (tf.Path.IndexOf("EasyBIM2Rhino", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (System.IO.File.Exists(tf.Path)) continue;
                    try { tf.Close(false); } catch { }
                }
            }
            catch
            {
                // 清理失败不影响插件
            }
        }

        // 工具栏/图标按钮由随 .rhp 同目录输出的 EasyBIM2Rhino.rui 提供，
        // Rhino 启动时自动加载，无需在此做任何释放或打开操作。
    }
}
