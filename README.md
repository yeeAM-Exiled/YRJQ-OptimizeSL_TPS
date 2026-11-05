# YRJQ-OptimizeSL_TPS

给 SCP: SL 服务端用的 LabAPI 优化插件。

缓存掉落物实例，合并物品网络状态检查，跳过服务端用不上的雾效、后处理。顺便带个动态 TPS。

默认上限 60。持续掉 TPS 8 秒就降到这段时间的最低采样值，稳定 30 秒再往上加 5，加到上限为止。短暂波动会忽略，上限可以改。

安装就去 [Releases](https://github.com/yeeAM-Exiled/YRJQ-OptimizeSL_TPS/releases) 下 DLL，丢进 `LabAPI/plugins/你的端口/`，重启服务端。

环境：SL 14.2.9、LabAPI 1.1.7，依赖目录要有 `0Harmony.dll`（2.2.2）。

配置在 `LabAPI/configs/你的端口/YRJQ-OptimizeSL_TPS/config.yml`，主要改 `maximum_tps` 就行，其他参数看 [配置示例](config.example.yml)。

控制台或 RA 输 `optimizetps` 看状态。

效果看机器和负载。更细的东西放在 [使用说明](使用说明.txt) 里了。
