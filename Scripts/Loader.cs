using BepInEx;

namespace M3SUPER90_Plugin
{
    [BepInDependency("pl.szikaka.receiver_2_modding_kit")]
    [BepInPlugin("Ciarencew.M3S90", PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Loader : BaseUnityPlugin
    {
        public static readonly string folder_name = "MR223_Files";
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
    }
}
