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

## Must Know
This plugin can optimize bugs and issues where heavy plugins repeatedly compute during idle frames, but **if a plugin originally includes idle-TPS computation, this may increase unrelated computation**. Be sure to turn off TPS detection and dynamic adjustment.

If some of your plugins must perform **periodic full-map dropped-item scans** or **avoid reusing valid item instances**, this will cause **momentary stutters** when dropped-item instances are re-cached.

If your server plugins use Fog effects with IDs >35, this will cause them to stop working.

⚠️ Please, absolutely do not attempt cross-version compatibility while using this plugin. **In compatibility testing on 14.2.0, cross-version compatibility was found to cause the server to enter an infinite loop of repeated calculations and freeze.** If you need to use it on nostalgia/legacy servers or similar, you must use Opus or GPT or something to complete compatibility work before using it!!!
