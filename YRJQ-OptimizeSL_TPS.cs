using System;
using System.ComponentModel;
#if !CONTROLLER_ONLY
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using CommandSystem;
using HarmonyLib;
using InventorySystem.Items.Pickups;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;
#endif

namespace YRJQ.OptimizeSL
{
    public sealed class Config
    {
        public bool IsEnabled { get; set; } = true;
        [Description("启用动态TPS上限。")]
        public bool DynamicTps { get; set; } = true;
        [Description("TPS上限；默认60。")]
        public int MaximumTps { get; set; } = 60;
        [Description("持续过载时的最低保护上限。")]
        public int MinimumTps { get; set; } = 10;
        [Description("低于当前上限持续多少秒后降档。")]
        public double OverloadSeconds { get; set; } = 8;
        [Description("稳定多少秒后尝试提升5 TPS。")]
        public double StableSeconds { get; set; } = 30;
        [Description("采样窗口秒数；使用真实时间，不受timeScale影响。")]
        public double SampleSeconds { get; set; } = 1;
        [Description("忽略低于上限的常规测量误差，单位TPS。")]
        public double TpsTolerance { get; set; } = 2;
        [Description("允许的短暂波动秒数；超过后才清除稳定状态。")]
        public double BounceToleranceSeconds { get; set; } = 2;
        [Description("地图生成、开局、退出空服休眠后的采样宽限期。")]
        public double WarmupSeconds { get; set; } = 10;
        [Description("合并原版掉落物网络状态检查；不改变物理模拟、伤害或角色刷新率。")]
        public bool BatchPickupUpdates { get; set; } = true;
        [Description("掉落物网络状态检查频率；默认20Hz，原版移动同步冷却仍为0.25秒。")]
        public int PickupChecksPerSecond { get; set; } = 20;
        [Description("通过生命周期缓存替换原版掉落物全场查找。")]
        public bool CachePickupQueries { get; set; } = true;
        [Description("跳过专用服务器无客户端时的本地雾效、后处理和渲染循环。")]
        public bool SkipServerVisuals { get; set; } = true;
        public bool LogTpsChanges { get; set; } = true;

        internal void Normalize()
        {
            MaximumTps = Math.Max(1, Math.Min(1000, MaximumTps));
            MinimumTps = Math.Max(1, Math.Min(MaximumTps, MinimumTps));
            SampleSeconds = Clamp(SampleSeconds, 0.5, 10, 1);
            OverloadSeconds = Clamp(OverloadSeconds, SampleSeconds * 2, 600, 8);
            StableSeconds = Clamp(StableSeconds, SampleSeconds * 2, 3600, 30);
            BounceToleranceSeconds = Clamp(BounceToleranceSeconds, 0, Math.Min(OverloadSeconds, StableSeconds) / 2, 2);
            TpsTolerance = Clamp(TpsTolerance, 0.1, Math.Max(0.1, MaximumTps * 0.25), 2);
            WarmupSeconds = Clamp(WarmupSeconds, 0, 600, 10);
            PickupChecksPerSecond = Math.Max(4, Math.Min(120, PickupChecksPerSecond));
        }

        private static double Clamp(double value, double min, double max, double fallback)
            => Math.Max(min, Math.Min(max, double.IsNaN(value) || double.IsInfinity(value) ? fallback : value));
    }

    // Pure state machine: samples are counts of actual Updates per wall-clock window.
    public sealed class TpsController
    {
        private readonly Config config;
        private double lowSeconds, stableSeconds, unstableSeconds;
        private double lowest = double.PositiveInfinity;
        public int Target { get; private set; }
        public double LastSample { get; private set; }
        public string LastReason { get; private set; } = "initial";

        public TpsController(Config config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            config.Normalize();
            Reset();
        }

        public void Reset()
        {
            Target = config.MaximumTps;
            LastSample = 0;
            LastReason = "round/reset";
            ClearHistory();
        }

        public void ClearHistory()
        {
            lowSeconds = stableSeconds = unstableSeconds = 0;
            lowest = double.PositiveInfinity;
        }

        public bool Observe(double tps, double seconds)
        {
            if (!config.DynamicTps || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)
                || tps <= 0 || double.IsNaN(tps) || double.IsInfinity(tps)) return false;
            LastSample = tps;
            double tolerance = Math.Min(config.TpsTolerance, Math.Max(0.25, Target * 0.1));
            if (tps < Target - tolerance)
            {
                lowSeconds += seconds;
                unstableSeconds += seconds;
                lowest = Math.Min(lowest, tps);
                if (unstableSeconds > config.BounceToleranceSeconds) stableSeconds = 0;
                if (lowSeconds >= config.OverloadSeconds && Target > config.MinimumTps)
                {
                    Target = Math.Max(config.MinimumTps, Math.Min(Target - 1, (int)Math.Floor(lowest)));
                    LastReason = "sustained overload / lowest window";
                    ClearHistory();
                    return true;
                }
                return false;
            }

            unstableSeconds = 0;
            stableSeconds += seconds;
            lowSeconds = 0;
            lowest = double.PositiveInfinity;
            if (stableSeconds >= config.StableSeconds && Target < config.MaximumTps)
            {
                Target = Math.Min(config.MaximumTps, Target + 5);
                LastReason = "stable recovery / +5";
                ClearHistory();
                return true;
            }
            return false;
        }
    }

#if !CONTROLLER_ONLY
    public sealed class OptimizePlugin : Plugin<Config>
    {
        internal static OptimizePlugin Instance;
        internal const string HarmonyId = "yrjq.optimizesl.tps";
        private readonly Stopwatch clock = new Stopwatch();
        private Harmony harmony;
        private TpsController controller;
        private double lastWindow, ignoreUntil;
        private int frames;
        private short previousCap;
        private bool running, suspended, generating;
        internal static bool Dedicated => ServerStatic.IsDedicated && !NetworkClient.active;
        public override string Name => "YRJQ-OptimizeSL_TPS";
        public override string Description => "Dedicated server pickup, rendering and adaptive TPS optimizer.";
        public override string Author => "聿日笺秋";
        public override Version Version => new Version(1, 0, 0);
        public override Version RequiredApiVersion => new Version(1, 1, 7);

        public override void LoadConfigs()
        {
            bool restart = running;
            if (restart) Disable();
            base.LoadConfigs();
            if (restart) Enable();
        }

        public override void Enable()
        {
            if (running) return;
            Config = Config ?? new Config();
            Config.Normalize();
            if (!Config.IsEnabled) return;
            Instance = this;
            previousCap = Server.MaxTps;
            controller = new TpsController(Config);
            harmony = new Harmony(HarmonyId);
            running = true;
            generating = suspended = false;
            clock.Restart();
            try
            {
                if (Config.CachePickupQueries || Config.BatchPickupUpdates) PickupIndex.Start();
                InstallPatches();
                if (PickupBatch.Available) PickupBatch.AttachExisting();
                ServerEvents.WaitingForPlayers += Waiting;
                ServerEvents.RoundStarted += RoundStarted;
                ServerEvents.RoundRestarted += Restarting;
                ServerEvents.MapGenerating += MapGenerating;
                ServerEvents.MapGenerated += MapGenerated;
                ServerEvents.Shutdown += Shutdown;
                StaticUnityMethods.OnUpdate += Update;
                ResetWindow(true);
                ApplyCap();
                Logger.Info($"[{Name}] Enabled. Max TPS={Config.MaximumTps}, pickup checks={Config.PickupChecksPerSecond}Hz.");
            }
            catch
            {
                Disable();
                throw;
            }
        }

        public override void Disable()
        {
            if (!running) return;
            running = false;
            StaticUnityMethods.OnUpdate -= Update;
            ServerEvents.WaitingForPlayers -= Waiting;
            ServerEvents.RoundStarted -= RoundStarted;
            ServerEvents.RoundRestarted -= Restarting;
            ServerEvents.MapGenerating -= MapGenerating;
            ServerEvents.MapGenerated -= MapGenerated;
            ServerEvents.Shutdown -= Shutdown;
            // Restore original callbacks before unpatching. No old-round closures survive.
            PickupBatch.Stop();
            harmony?.UnpatchAll(HarmonyId);
            PickupIndex.Stop();
            if (Config.DynamicTps && Server.MaxTps == controller.Target) SetCapPreservingIdle(previousCap);
            clock.Stop();
            Instance = null;
        }

        private void InstallPatches()
        {
            if (Config.CachePickupQueries || Config.BatchPickupUpdates)
            {
                Patch(AccessTools.Method(typeof(ItemPickupBase), "Awake"), null, nameof(Patches.PickupAwake));
                Patch(AccessTools.Method(typeof(ItemPickupBase), "OnDestroy"), nameof(Patches.PickupDestroy));
            }
            if (Config.CachePickupQueries)
            {
                var cleanup = typeof(ServerStatic).Assembly.GetType("CommandSystem.Commands.RemoteAdmin.Cleanup.ItemsCommand");
                Patch(AccessTools.Method(cleanup, "Execute"), transpiler: nameof(Patches.PickupQuery));
            }
            if (Config.BatchPickupUpdates)
            {
                var ctor = AccessTools.Constructor(typeof(PickupStandardPhysics),
                    new[] { typeof(ItemPickupBase), typeof(PickupStandardPhysics.FreezingMode) });
                var destroy = AccessTools.Method(typeof(PickupStandardPhysics), "DestroyModule");
                if (ctor != null && destroy != null && PickupBatch.Bind())
                {
                    bool removed = Patch(destroy, nameof(Patches.ModuleDestroyed));
                    PickupBatch.Available = removed && Patch(ctor, postfix: nameof(Patches.ModuleCreated));
                }
            }
            if (Config.SkipServerVisuals)
            {
                var game = typeof(ServerStatic).Assembly;
                Patch(AccessTools.Method(game.GetType("CustomRendering.FogController"), "Update"), nameof(Patches.Visual));
                Patch(AccessTools.Method(game.GetType("ZonePostProcessing"), "UpdateWeights"), nameof(Patches.Visual));
                // Managed SRP entry point only; colliders, lights' server state and network objects remain live.
                var pipeline = typeof(Object).Assembly.GetType("UnityEngine.Rendering.RenderPipelineManager");
                Patch(AccessTools.Method(pipeline, "DoRenderLoop_Internal"), nameof(Patches.Visual));
            }
        }

        private bool Patch(MethodBase target, string prefix = null, string postfix = null, string transpiler = null)
        {
            if (target == null)
            {
                Logger.Warn($"[{Name}] Optional optimization method absent; kept vanilla implementation.");
                return false;
            }
            try
            {
                harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix),
                    transpiler == null ? null : new HarmonyMethod(typeof(Patches), transpiler));
                Logger.Info($"[{Name}] Optimized {target.DeclaringType?.FullName}.{target.Name}");
                return true;
            }
            catch (Exception error)
            {
                Logger.Warn($"[{Name}] Kept vanilla {target.Name}: {error.Message}");
                return false;
            }
        }

        private void Waiting()
        {
            generating = false;
            PickupIndex.Refresh();
            if (PickupBatch.Available) PickupBatch.AttachExisting();
            ResetWindow(true);
            ApplyCap();
            // Exactly four lines, including the complete literal name.
            Logger.Info("+---------------------------------------------------+\n" +
                "| Y R J Q   --   O P T I M I Z E S L                 |\n" +
                "|                  YRJQ-OptimizeSL                  |\n" +
                "+---------------------------------------------------+");
        }

        private void RoundStarted() { generating = false; ResetWindow(true); ApplyCap(); }
        private void Restarting() { generating = true; ResetWindow(true); }
        private void MapGenerating(MapGeneratingEventArgs _) { generating = true; ResetWindow(true); }
        private void MapGenerated(MapGeneratedEventArgs _) { generating = false; ResetWindow(false); }
        private void Shutdown() => Disable();

        private void ResetWindow(bool resetCap)
        {
            if (resetCap) controller.Reset(); else controller.ClearHistory();
            lastWindow = clock.Elapsed.TotalSeconds;
            ignoreUntil = lastWindow + Config.WarmupSeconds;
            frames = 0;
        }

        private void ApplyCap()
        {
            if (Config.DynamicTps && Dedicated && Server.MaxTps != controller.Target)
                SetCapPreservingIdle((short)controller.Target);
        }

        private static void SetCapPreservingIdle(short cap)
        {
            int idleRate = Application.targetFrameRate;
            bool idle = IdleMode.IdleModeActive;
            Server.MaxTps = cap;
            if (idle) Application.targetFrameRate = idleRate;
        }

        private void Update()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (!Dedicated || !NetworkServer.active || IdleMode.IdleModeActive || generating)
            {
                if (!suspended) { controller.ClearHistory(); suspended = true; }
                frames = 0;
                lastWindow = now;
                return;
            }
            PickupBatch.Tick(now, 1.0 / Config.PickupChecksPerSecond);
            if (suspended)
            {
                suspended = false;
                ResetWindow(false);
                ApplyCap();
            }
            if (now < ignoreUntil) { frames = 0; lastWindow = now; return; }
            frames++;
            double elapsed = now - lastWindow;
            if (elapsed < Config.SampleSeconds) return;
            double tps = frames / elapsed;
            frames = 0;
            lastWindow = now;
            int old = controller.Target;
            if (!controller.Observe(tps, elapsed)) return;
            ApplyCap();
            if (Config.LogTpsChanges)
                Logger.Info($"[{Name}] TPS cap {old} -> {controller.Target}; measured {tps:F1}; {controller.LastReason}");
        }

        internal string Status => $"{Name} {Version} | TPS {controller.LastSample:F1}, cap {controller.Target}/{Config.MaximumTps}"
            + $" | pickups {PickupIndex.Count}, batched {PickupBatch.Count} | idle {IdleMode.IdleModeActive}"
            + $" | cached queries {PickupIndex.Queries}, batched passes {PickupBatch.Passes}";
    }

    // Raw Unity instances are tracked by identity, including serial=0 locker items.
    // No polling of scenes or periodic Pickup.List.ToArray() is needed.
    internal static class PickupIndex
    {
        private static readonly Dictionary<int, ItemPickupBase> entries = new Dictionary<int, ItemPickupBase>();
        internal static int Count => entries.Count;
        internal static IEnumerable<ItemPickupBase> Values => entries.Values;
        internal static long Queries;
        internal static bool Ready;

        internal static void Start()
        {
            entries.Clear();
            Queries = 0;
            ItemPickupBase.OnPickupAdded += Add;
            ItemPickupBase.OnPickupDestroyed += Remove;
            // One bootstrap scan also captures inactive pickups and hot-loaded games.
            foreach (var pickup in Resources.FindObjectsOfTypeAll<ItemPickupBase>()) Add(pickup);
            Ready = true;
        }

        internal static void Stop()
        {
            Ready = false;
            ItemPickupBase.OnPickupAdded -= Add;
            ItemPickupBase.OnPickupDestroyed -= Remove;
            entries.Clear();
        }

        internal static void Add(ItemPickupBase pickup)
        {
            if (pickup == null || !pickup.gameObject.scene.IsValid()) return;
            entries[pickup.GetInstanceID()] = pickup;
        }

        internal static void Remove(ItemPickupBase pickup)
        {
            if (ReferenceEquals(pickup, null)) return;
            entries.Remove(pickup.GetInstanceID());
        }

        internal static void Refresh()
        {
            var stale = new List<int>();
            foreach (var pair in entries) if (pair.Value == null) stale.Add(pair.Key);
            foreach (int key in stale) entries.Remove(key);
            foreach (var pickup in Pickup.List) Add(pickup.Base);
        }

        internal static ItemPickupBase[] FindActive()
        {
            if (!Ready || !OptimizePlugin.Dedicated)
                return Object.FindObjectsByType<ItemPickupBase>(FindObjectsSortMode.InstanceID);
            Queries++;
            var result = new List<ItemPickupBase>(entries.Count);
            foreach (var item in entries.Values)
                if (item != null && item.gameObject.activeInHierarchy && (item.hideFlags & HideFlags.DontSave) == 0)
                    result.Add(item);
            // Preserve FindObjectsOfType instance-ID order and return a private snapshot:
            // cleanup destroys these instances while iterating, so sharing an array is unsafe.
            result.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            return result.ToArray();
        }
    }

    internal static class PickupBatch
    {
        private sealed class Entry
        {
            internal PickupStandardPhysics Module;
            internal Action Original;
            internal Action ServerUpdate;
            internal int Index;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly Dictionary<PickupStandardPhysics, Entry> lookup = new Dictionary<PickupStandardPhysics, Entry>();
        private static MethodInfo onUpdate, updateServer;
        private static double nextPass;
        internal static bool Available;
        internal static long Passes;
        internal static int Count => entries.Count;

        internal static bool Bind()
        {
            onUpdate = AccessTools.Method(typeof(PickupStandardPhysics), "OnUpdate");
            updateServer = AccessTools.Method(typeof(PickupStandardPhysics), "UpdateServer");
            return onUpdate != null && updateServer != null;
        }

        internal static void AttachExisting()
        {
            foreach (var pickup in PickupIndex.Values)
                if (pickup != null && pickup.PhysicsModule is PickupStandardPhysics module) Attach(module);
        }

        internal static void Attach(PickupStandardPhysics module)
        {
            if (!Available || !OptimizePlugin.Dedicated || lookup.ContainsKey(module) || module.Rb == null) return;
            Entry entry;
            try
            {
                entry = new Entry
                {
                    Module = module,
                    Original = (Action)Delegate.CreateDelegate(typeof(Action), module, onUpdate),
                    ServerUpdate = (Action)Delegate.CreateDelegate(typeof(Action), module, updateServer),
                    Index = entries.Count,
                };
            }
            catch (Exception error)
            {
                Logger.Warn($"[YRJQ-OptimizeSL_TPS] Kept vanilla pickup callback: {error.Message}");
                return;
            }
            lookup.Add(module, entry);
            entries.Add(entry);
            StaticUnityMethods.OnUpdate -= entry.Original;
        }

        internal static void Remove(PickupStandardPhysics module, bool restore = false)
        {
            if (!lookup.TryGetValue(module, out var entry)) return;
            int last = entries.Count - 1;
            if (entry.Index != last)
            {
                entries[entry.Index] = entries[last];
                entries[entry.Index].Index = entry.Index;
            }
            entries.RemoveAt(last);
            lookup.Remove(module);
            if (restore && module.Rb != null) StaticUnityMethods.OnUpdate += entry.Original;
        }

        internal static void Tick(double now, double interval)
        {
            if (!Available || now < nextPass) return;
            nextPass = now + interval;
            Passes++;
            int index = 0;
            while (index < entries.Count)
            {
                var entry = entries[index];
                if (entry.Module.Rb == null) Remove(entry.Module);
                else
                {
                    try { entry.ServerUpdate(); }
                    catch (Exception error)
                    {
                        Remove(entry.Module, true);
                        Logger.Warn($"[YRJQ-OptimizeSL_TPS] Restored vanilla pickup callback: {error.Message}");
                    }
                }
                // A vanilla update may destroy an item and swap the last entry here.
                if (index < entries.Count && ReferenceEquals(entries[index], entry)) index++;
            }
        }

        internal static void Stop()
        {
            Available = false;
            foreach (var entry in entries)
                if (entry.Module.Rb != null) StaticUnityMethods.OnUpdate += entry.Original;
            entries.Clear();
            lookup.Clear();
            nextPass = 0;
            Passes = 0;
        }
    }

    internal static class Patches
    {
        internal static bool Visual() => !OptimizePlugin.Dedicated;
        internal static void PickupAwake(ItemPickupBase __instance) => PickupIndex.Add(__instance);
        internal static void PickupDestroy(ItemPickupBase __instance) => PickupIndex.Remove(__instance);
        internal static void ModuleCreated(PickupStandardPhysics __instance) => PickupBatch.Attach(__instance);
        internal static void ModuleDestroyed(PickupStandardPhysics __instance) => PickupBatch.Remove(__instance);

        internal static IEnumerable<CodeInstruction> PickupQuery(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Object)
                    && method.Name == "FindObjectsOfType" && method.IsGenericMethod
                    && method.GetGenericArguments()[0] == typeof(ItemPickupBase) && method.GetParameters().Length == 0)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(PickupIndex), nameof(PickupIndex.FindActive));
                }
                yield return instruction;
            }
        }
    }

    [CommandHandler(typeof(GameConsoleCommandHandler))]
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public sealed class StatusCommand : ICommand
    {
        public string Command => "optimizetps";
        public string[] Aliases => new[] { "yrjqtps" };
        public string Description => "Show adaptive TPS and pickup optimizer status.";
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            response = OptimizePlugin.Instance?.Status ?? "YRJQ-OptimizeSL_TPS is disabled.";
            return true;
        }
    }
#endif
}
