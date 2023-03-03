using BepInEx;

namespace StaminaChange
{
    [BepInPlugin("hypersycos.plugins.arkshot.staminaplus", "Stamina Plus", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
    }
}
