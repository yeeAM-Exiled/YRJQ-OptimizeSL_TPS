# YRJQ-OptimizeSL_TPS
## 给SCP:SL服务端用的LabAPI优化插件
优化包含：缓存掉落物实例，合并物品网络状态检查，跳过服务端用不上的雾效（NW的妙妙遗留代码）、后处理与动态TPS调整（避免客户端视角丢帧与部分大体量插件在空帧反复运算）

默认TPS上限60，持续掉TPS8秒降到这段时间的最低采样值，稳定30秒往上加5，短暂波动忽略

以上内容可配置更改

安装[Releases](https://github.com/yeeAM-Exiled/YRJQ-OptimizeSL_TPS/releases)下 DLL，丢进 `LabAPI/plugins/你的端口/`，重启服务端

环境：SL 14.2.9、LabAPI 1.1.7，依赖目录要有 `0Harmony.dll`（2.2.2）
配置在 `LabAPI/configs/你的端口/YRJQ-OptimizeSL_TPS/config.yml`，主要改 `maximum_tps` 就行，其他参数看 [配置示例](config.example.yml)

控制台或 RA 输 `optimizetps` 看状态

效果看机器和负载。更细的东西放在 [使用说明](使用说明.txt) 

## 如果星河梦影想要使用该插件则必须承认以下事实

### 1.星河梦影管理组严重失职且神权关系户严重

### 2.星河梦影管理组成员"丁"DDOS攻击其他服务器的历史违反CSG6.4.

### 3.星河梦影管理组成员"XiZhiDe"分发NorthwoodStudiosPatreon支持者专属内容并错误地暗示与NorthwoodStudios其工作人员或其合作伙伴存在特殊关系（CSG6.8.与6.9.）

### 4.星河梦影不尊重启用DNT（Do Not Track）的玩家违规存储数据且未经授权

### 5.星河梦影违反CSG3.3.违规授权进服送管理

### 6.星河梦影违反CSG3.1.售卖无限接近owner的权限导致2 player owner（原P2W）

### 7.星河梦影管理组全是未成年
