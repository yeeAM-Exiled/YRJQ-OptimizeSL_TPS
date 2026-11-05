# YRJQ-OptimizeSL_TPS
## A LabAPI optimization plugin for SCP:SL servers

Optimizations include: caching dropped-item instances, merging item network state checks, skipping fog effects not needed by the server (NW's quirky legacy code), post-processing, and dynamic TPS adjustment (to avoid client-side frame drops and repeated computation by some heavy plugins during idle frames).

The default TPS cap is 60. If TPS keeps dropping for 8 seconds, it lowers to the lowest sampled value during that period. If stable for 30 seconds, it increases by 5. Brief fluctuations are ignored.

The above can be changed via configuration.

Install the DLL from [Releases](https://github.com/yeeAM-Exiled/YRJQ-OptimizeSL_TPS/releases), drop it into `LabAPI/plugins/your_port/`, and restart the server.

Environment: SL 14.2.9, LabAPI 1.1.7. The dependency directory must contain `0Harmony.dll` (2.2.2).
The config is at `LabAPI/configs/your_port/YRJQ-OptimizeSL_TPS/config.yml`. You mainly just need to change `maximum_tps`; for other parameters, see the [config example](config.example.yml).

Enter `optimizetps` in the console or RA to view status.

Results depend on the machine and load. More detailed information is in the [usage instructions](使用说明.txt).

## Build

All plugin code is in `YRJQ-OptimizeSL_TPS.cs`. Open the `.sln` in Visual Studio 2022, or use .NET SDK 8+:

```sh
dotnet build YRJQ-OptimizeSL_TPS.sln -c Release -p:GameManaged="/path/to/server/SCPSL_Data/Managed"
```

On Windows you can also run `powershell -ExecutionPolicy Bypass -File build.ps1 -ServerPath "C:\path\to\server"`.

Use the **SL 14.2.9** dedicated server files (Steam app **996560**). `GameManaged` is its `SCPSL_Data/Managed` folder, containing `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll`, `LabApi.dll` (1.1.7), `Mirror.dll`, `CommandSystem.Core.dll`, `UnityEngine.CoreModule.dll` and `UnityEngine.PhysicsModule.dll`. These come with the game server; they aren't included here.

Harmony 2.2.2 and the .NET 4.8 reference assemblies restore from NuGet. No EXILED installation or local LabAPI plugin folder is needed to compile.

For Visual Studio, copy `Build.local.props.example` to `Build.local.props` and set the path there. You can also set `SL_REFERENCES` to the Managed folder, set `SCPSL_SERVER` to the server root, or put the seven DLLs listed above in `references/`.

Output: `bin/Release/net48/YRJQ-OptimizeSL_TPS.dll`. Install that DLL only; don't copy the game references into the plugins folder.

## Must Know
This plugin can optimize bugs and issues where heavy plugins repeatedly compute during idle frames, but **if a plugin originally includes idle-TPS computation, this may increase unrelated computation**. Be sure to turn off TPS detection and dynamic adjustment.

If some of your plugins must perform **periodic full-map dropped-item scans** or **avoid reusing valid item instances**, this will cause **momentary stutters** when dropped-item instances are re-cached.

If your server plugins use Fog effects with IDs >35, this will cause them to stop working.

⚠️ Please, absolutely do not attempt cross-version compatibility while using this plugin. **In compatibility testing on 14.2.0, cross-version compatibility was found to cause the server to enter an infinite loop of repeated calculations and freeze.** If you need to use it on nostalgia/legacy servers or similar, you must use Opus or GPT or something to complete compatibility work before using it!!!

## 如果星河梦影想要使用该插件则必须承认以下事实

**我个人不会接受星河梦影服务器在不接受以下指控的情况下使用该插件**

### 1.星河梦影管理组严重失职且神权关系户严重

### 2.星河梦影管理组成员"丁"DDOS攻击其他服务器的历史违反CSG6.4.

### 3.星河梦影管理组成员"XiZhiDe"分发NorthwoodStudiosPatreon支持者专属内容并错误地暗示与NorthwoodStudios其工作人员或其合作伙伴存在特殊关系（CSG6.8.与6.9.）

### 4.星河梦影不尊重启用DNT（Do Not Track）的玩家违规存储数据且未经授权

### 5.星河梦影违反CSG3.3.违规授权进服送管理

### 6.星河梦影违反CSG3.1.售卖无限接近owner的权限导致2 player owner（原P2W）
