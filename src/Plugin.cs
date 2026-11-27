using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Cjayride.DivineFavorPowers
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("M2Valheim.TalentTree", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.passivepowers", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "cjayride.divinefavorpowers";
        public const string PluginName = "DivineFavorPowers";
        public const string PluginVersion = "1.0.9";

        private const string TalentTreeGuid = "M2Valheim.TalentTree";
        private const string PassivePowersGuid = "org.bepinex.plugins.passivepowers";

        private static ConfigEntry<KeyboardShortcut> _first;
        private static ConfigEntry<KeyboardShortcut> _second;
        private static Func<Player, List<string>> _getPowers;
        private static int _pending = -1;
        private static bool _activating;

        private static KeyCode _firstMain;
        private static KeyCode[] _firstMods = Array.Empty<KeyCode>();
        private static bool _firstNeedsMod;
        private static KeyCode _secondMain;
        private static KeyCode[] _secondMods = Array.Empty<KeyCode>();
        private static bool _secondNeedsMod;

        private static ObjectDB _cachedDb;
        private static readonly Dictionary<string, CachedEffects> _effectCache = new Dictionary<string, CachedEffects>(8);

        private struct CachedEffects
        {
            public StatusEffect Vanilla;
            public StatusEffect Passive;
        }

        private void Awake()
        {
            _first = Config.Bind("Hotkeys", "First ability", new KeyboardShortcut(KeyCode.F),
                "Activates the first boss power equipped on the stones.");
            _second = Config.Bind("Hotkeys", "Second ability", new KeyboardShortcut(KeyCode.F, KeyCode.LeftShift),
                "Activates the second boss power equipped on the stones.");
            _first.SettingChanged += (_, _) => PushShortcuts();
            _second.SettingChanged += (_, _) => PushShortcuts();
            CacheShortcuts();

            if (Application.isBatchMode)
                return;

            var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
            if (!plugins.TryGetValue(TalentTreeGuid, out var talentPlugin)
                || !plugins.TryGetValue(PassivePowersGuid, out var passivePlugin))
            {
                Logger.LogWarning("DivineFavorPowers idle. Needs TalentTree and Passive Powers.");
                return;
            }

            Type utils = passivePlugin.Instance.GetType().Assembly.GetType("PassivePowers.Utils");
            MethodInfo equipped = AccessTools.Method(utils, "getPassivePowers", new[] { typeof(Player) });
            if (equipped != null)
                _getPowers = AccessTools.MethodDelegate<Func<Player, List<string>>>(equipped);

            PushShortcuts();
            RemoveTalentTreeCycle(talentPlugin.Instance.GetType().Assembly);
            new Harmony(PluginGUID).PatchAll(typeof(FirePatch));
            Logger.LogInfo("DivineFavorPowers " + PluginVersion + " loaded. Stones set the powers. Hotkeys fire them.");
        }

        private static void PushShortcuts()
        {
            CacheShortcuts();
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(PassivePowersGuid, out var plugin))
                return;

            SetShortcut(plugin.Instance.Config, 1, _first.Value);
            SetShortcut(plugin.Instance.Config, 2, _second.Value);
        }

        private static void CacheShortcuts()
        {
            CacheOne(_first.Value, out _firstMain, out _firstMods, out _firstNeedsMod);
            CacheOne(_second.Value, out _secondMain, out _secondMods, out _secondNeedsMod);
        }

        private static void CacheOne(KeyboardShortcut shortcut, out KeyCode main, out KeyCode[] mods, out bool needsMod)
        {
            main = shortcut.MainKey;
            var list = new List<KeyCode>(2);
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier != KeyCode.None)
                    list.Add(modifier);
            }

            mods = list.ToArray();
            needsMod = mods.Length > 0;
        }

        private static void SetShortcut(ConfigFile config, int slot, KeyboardShortcut key)
        {
            if (config.TryGetEntry("2 - Active Powers", "Shortcut for boss power " + slot, out ConfigEntry<KeyboardShortcut> entry))
                entry.Value = key;
        }

        private void RemoveTalentTreeCycle(Assembly talentAssembly)
        {
            Type cycle = talentAssembly.GetType("Talents.Warcaller.Patches.DivineFavorCyclePatch");
            if (cycle == null)
            {
                Logger.LogError("TalentTree Divine Favor cycle type was not found. Cycling was not replaced.");
                return;
            }

            MethodInfo startGuardianPrefix = FindStatic(cycle, "StartGuardianPowerPrefix");
            MethodInfo legacyUpdatePostfix = FindStatic(cycle, "UpdatePostfix");
            MethodInfo aggregatedUpdatePostfix = FindStatic(cycle, "OnPlayerUpdatePostfix");

            var talentHarmony = new Harmony(TalentTreeGuid);
            bool removedStart = false;
            bool removedCycle = false;

            if (startGuardianPrefix != null)
            {
                MethodInfo start = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.StartGuardianPower));
                talentHarmony.Unpatch(start, startGuardianPrefix);
                removedStart = true;
            }

            if (legacyUpdatePostfix != null)
            {
                MethodInfo update = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.Update));
                talentHarmony.Unpatch(update, legacyUpdatePostfix);
                removedCycle = true;
            }
            else if (aggregatedUpdatePostfix != null)
            {
                new Harmony(PluginGUID).Patch(
                    aggregatedUpdatePostfix,
                    prefix: new HarmonyMethod(typeof(TalentTreeCycleDisabler), nameof(TalentTreeCycleDisabler.Block)));
                removedCycle = true;
            }

            if (removedStart && removedCycle)
                Logger.LogInfo("Removed TalentTree Divine Favor cycle. Cooldown reduction is unchanged.");
            else if (!removedStart && !removedCycle)
                Logger.LogError("TalentTree Divine Favor cycle patches were not found. Cycling was not replaced.");
            else
                Logger.LogWarning(
                    "TalentTree Divine Favor cycle was only partly disabled (start="
                    + removedStart + ", hold-cycle=" + removedCycle + ").");
        }

        private static MethodInfo FindStatic(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static class TalentTreeCycleDisabler
        {
            internal static bool Block()
            {
                return false;
            }
        }

        private static bool ModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        private static bool Matches(KeyCode main, KeyCode[] modifiers, bool needsMod, bool gpDown)
        {
            if (main == KeyCode.None)
                return false;

            if (!(Input.GetKeyDown(main) || (main == KeyCode.F && gpDown)))
                return false;

            for (int i = 0; i < modifiers.Length; i++)
            {
                if (!Input.GetKey(modifiers[i]) && !SameSide(modifiers[i]))
                    return false;
            }

            return needsMod || !ModifierHeld();
        }

        private static bool SameSide(KeyCode modifier)
        {
            if (modifier == KeyCode.LeftShift || modifier == KeyCode.RightShift)
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (modifier == KeyCode.LeftControl || modifier == KeyCode.RightControl)
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (modifier == KeyCode.LeftAlt || modifier == KeyCode.RightAlt)
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            return false;
        }

        private static void Activate(Player player, int slot)
        {
            if (_getPowers == null || player != Player.m_localPlayer)
                return;

            List<string> powers = _getPowers(player);
            if (powers == null || slot < 0 || slot >= powers.Count)
                return;

            string power = powers[slot];
            if (string.IsNullOrEmpty(power))
                return;

            ObjectDB db = ObjectDB.instance;
            if (db == null)
                return;

            if (!ReferenceEquals(_cachedDb, db))
            {
                _cachedDb = db;
                _effectCache.Clear();
            }

            if (!_effectCache.TryGetValue(power, out CachedEffects effects))
            {
                effects.Vanilla = db.GetStatusEffect(power.GetStableHashCode());
                effects.Passive = db.GetStatusEffect(("PassivePowers " + power).GetStableHashCode());
                _effectCache[power] = effects;
            }

            if (effects.Vanilla == null || effects.Passive == null)
                return;

            _activating = true;
            try
            {
                player.m_guardianSE = effects.Vanilla;
                player.StartGuardianPower();
                player.m_guardianSE = effects.Passive;
            }
            finally
            {
                _activating = false;
            }
        }

        [HarmonyPatch]
        private static class FirePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(Player), nameof(Player.Update))]
            private static void CatchPress(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                _pending = -1;
                if (!__instance.TakeInput() || Hud.InRadial() || Hud.IsPieceSelectionVisible())
                    return;

                bool gpDown = ZInput.GetButtonDown("GP");
                if (Matches(_secondMain, _secondMods, _secondNeedsMod, gpDown))
                    _pending = 1;
                else if (Matches(_firstMain, _firstMods, _firstNeedsMod, gpDown))
                    _pending = 0;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Player), nameof(Player.Update))]
            private static void Fire(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                int slot = _pending;
                _pending = -1;
                if (slot >= 0)
                    Activate(__instance, slot);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
            private static bool BlockVanilla(Player __instance)
            {
                if (_activating)
                    return true;

                if (__instance == Player.m_localPlayer && (__instance.m_guardianPower?.Contains(",") ?? false))
                    return false;

                if (ModifierHeld() && ZInput.GetButton("GP"))
                    return false;

                return true;
            }
        }
    }
}
