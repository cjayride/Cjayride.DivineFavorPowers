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
        public const string PluginVersion = "1.0.6";

        private static ConfigEntry<KeyboardShortcut> _first;
        private static ConfigEntry<KeyboardShortcut> _second;
        private static MethodInfo _equipped;
        private static int _pending = -1;

        private void Awake()
        {
            _first = Config.Bind("Hotkeys", "First ability", new KeyboardShortcut(KeyCode.F),
                "Activates the first boss power equipped on the stones.");
            _second = Config.Bind("Hotkeys", "Second ability", new KeyboardShortcut(KeyCode.F, KeyCode.LeftShift),
                "Activates the second boss power equipped on the stones.");
            _first.SettingChanged += (_, _) => PushShortcuts();
            _second.SettingChanged += (_, _) => PushShortcuts();

            bool talentTree = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("M2Valheim.TalentTree");
            bool passivePowers = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.passivepowers");
            if (!talentTree || !passivePowers)
            {
                Logger.LogWarning("DivineFavorPowers idle. Needs TalentTree and Passive Powers.");
                return;
            }

            Type utils = AccessTools.TypeByName("PassivePowers.Utils");
            _equipped = AccessTools.Method(utils, "getPassivePowers", new[] { typeof(Player) });
            PushShortcuts();
            RemoveTalentTreeCycle();
            new Harmony(PluginGUID).PatchAll(typeof(FirePatch));
            Logger.LogInfo("DivineFavorPowers " + PluginVersion + " loaded. Stones set the powers. Hotkeys fire them.");
        }

        private static void PushShortcuts()
        {
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("org.bepinex.plugins.passivepowers", out var plugin))
                return;

            SetShortcut(plugin.Instance.Config, 1, _first.Value);
            SetShortcut(plugin.Instance.Config, 2, _second.Value);
        }

        private static void SetShortcut(ConfigFile config, int slot, KeyboardShortcut key)
        {
            if (config.TryGetEntry("2 - Active Powers", "Shortcut for boss power " + slot, out ConfigEntry<KeyboardShortcut> entry))
                entry.Value = key;
        }

        private void RemoveTalentTreeCycle()
        {
            Type cycle = AccessTools.TypeByName("Talents.Warcaller.Patches.DivineFavorCyclePatch");
            MethodInfo prefix = AccessTools.Method(cycle, "StartGuardianPowerPrefix");
            MethodInfo postfix = AccessTools.Method(cycle, "UpdatePostfix");
            if (prefix == null || postfix == null)
            {
                Logger.LogError("TalentTree Divine Favor cycle patches were not found. Cycling was not replaced.");
                return;
            }

            MethodInfo start = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.StartGuardianPower));
            MethodInfo update = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.Update));
            var talentHarmony = new Harmony("M2Valheim.TalentTree");
            talentHarmony.Unpatch(start, prefix);
            talentHarmony.Unpatch(update, postfix);
            Logger.LogInfo("Removed TalentTree Divine Favor cycle. Cooldown reduction is unchanged.");
        }

        private static bool ModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        private static bool Matches(KeyboardShortcut shortcut, bool gpDown)
        {
            if (shortcut.MainKey == KeyCode.None)
                return false;

            bool down = Input.GetKeyDown(shortcut.MainKey) || (shortcut.MainKey == KeyCode.F && gpDown);
            if (!down)
                return false;

            bool needsModifier = false;
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier == KeyCode.None)
                    continue;

                needsModifier = true;
                if (!Input.GetKey(modifier) && !SameSide(modifier))
                    return false;
            }

            return needsModifier || !ModifierHeld();
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
            if (_equipped == null || player != Player.m_localPlayer)
                return;

            var powers = _equipped.Invoke(null, new object[] { player }) as List<string>;
            if (powers == null || slot < 0 || slot >= powers.Count || string.IsNullOrEmpty(powers[slot]))
                return;

            string power = powers[slot];
            StatusEffect vanilla = ObjectDB.instance.GetStatusEffect(power.GetStableHashCode());
            StatusEffect passive = ObjectDB.instance.GetStatusEffect(("PassivePowers " + power).GetStableHashCode());
            if (vanilla == null || passive == null)
                return;

            player.m_guardianSE = vanilla;
            player.StartGuardianPower();
            player.m_guardianSE = passive;
        }

        [HarmonyPatch]
        private static class FirePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(Player), nameof(Player.Update))]
            private static void CatchPress(Player __instance)
            {
                _pending = -1;
                if (__instance != Player.m_localPlayer || !__instance.TakeInput() || Hud.InRadial() || Hud.IsPieceSelectionVisible())
                    return;

                bool gpDown = ZInput.GetButtonDown("GP");
                if (Matches(_second.Value, gpDown))
                    _pending = 1;
                else if (Matches(_first.Value, gpDown))
                    _pending = 0;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Player), nameof(Player.Update))]
            private static void Fire(Player __instance)
            {
                int slot = _pending;
                _pending = -1;
                if (slot >= 0)
                    Activate(__instance, slot);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
            private static bool BlockVanilla(Player __instance)
            {
                if (CalledFromUs())
                    return true;

                if (__instance == Player.m_localPlayer && (__instance.m_guardianPower?.Contains(",") ?? false))
                    return false;

                if (ModifierHeld() && ZInput.GetButton("GP"))
                    return false;

                return true;
            }

            private static bool CalledFromUs()
            {
                var trace = new System.Diagnostics.StackTrace();
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    string name = trace.GetFrame(i).GetMethod()?.DeclaringType?.FullName;
                    if (name != null && name.StartsWith("Cjayride.DivineFavorPowers"))
                        return true;
                }

                return false;
            }
        }
    }
}
