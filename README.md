# Arkshot-StaminaPlus
Allows changing the stamina costs of actions in Arkshot (Steam ID 468800). Requires BepinEx and Arkshot-MultiplayerSync. When hosting, your settings will be used by all other clients using the mod, though vanilla clients will not copy the settings. Will use vanilla settings when the host doesn't have this plugin.

# Installation
## Prerequisites
### BepInEx
The plugin was developed with [5.4.21](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.21/), so if in doubt use that. Make sure to download the 32-bit / x86 edition. Further instructions can be found [here](https://docs.bepinex.dev/articles/user_guide/installation/index.html). Arkshot also requires the alternative endpoint, detailed [here](https://docs.bepinex.dev/articles/user_guide/troubleshooting.html#change-the-entry-point-1)
### Arkshot-MultiplayerSync
[MultiplayerSync](https://github.com/Hypersycos/Arkshot-MultiplayerSync) must be installed in the plugins folder, or the plugin will not load.

## Plugin
Download the [latest dll](https://github.com/Hypersycos/Arkshot-StaminaPlus/releases/latest/download/StaminaPlus.dll) and put it in Arkshot/BepInEx/plugins. After an initial run, the config file should be accessible in BepInEx/config.
