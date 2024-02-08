# Arkshot-StaminaPlus
Allows changing the stamina costs of actions in Arkshot (Steam ID 468800). Requires BepinEx and Arkshot-MultiplayerSync. When hosting, your settings will be used by all other clients using the mod, though vanilla clients will not copy the settings. Will use vanilla settings when the host doesn't have this plugin.

# Installation
## Prerequisites
### BepInEx
Requires the 32-bit / x86 version of BepinEx. The plugin was developed with [5.4.21](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.21/), so if in doubt use that. Further instructions can be found [here](https://docs.bepinex.dev/articles/user_guide/installation/index.html). Once installed, run then close the game, and modify `BepInEx/config/BepInEx.cfg` so that [Preloader.Entrypoint], found at the bottom of the file, reads
```
[Preloader.Entrypoint]

## The local filename of the assembly to target.
# Setting type: String
# Default value: UnityEngine.dll
Assembly = Assembly-CSharp.dll

## The name of the type in the entrypoint assembly to search for the entrypoint method.
# Setting type: String
# Default value: Application
Type = MonoBehaviour

## The name of the method in the specified entrypoint assembly and type to hook and load Chainloader from.
# Setting type: String
# Default value: .cctor
Method = .cctor
```
### Arkshot-MultiplayerSync
[MultiplayerSync](https://github.com/Hypersycos/Arkshot-MultiplayerSync) must be installed in `Arkshot/BepInEx/plugins`, or the plugin will not load.

## Plugin
Download the [latest dll](https://github.com/Hypersycos/Arkshot-StaminaPlus/releases/latest/download/StaminaPlus.dll) and put it in `Arkshot/BepInEx/plugins`. After an initial run, the config file should be accessible in `Arkshot/BepInEx/config`.
