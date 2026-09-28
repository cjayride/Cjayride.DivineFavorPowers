using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
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
        public const string PluginVersion = "1.0.0";

        private const float HoldThreshold = 0.8f;
        private const float CycleInterval = 0.8f;
        private const string CycleFlag = "warcaller_divine_favor_cycle";
        private const string EquippedKey = "PassivePowers GuardianPowers";

        private static MethodInfo _hasTalent;
        private static MethodInfo _activeEnabled;
        private static bool _armed;
        private static int _cycles;

        private void Awake()
        {
            bool talentTree = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("M2Valheim.TalentTree");
            bool passivePowers = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.passivepowers");
            if (!talentTree || !passivePowers)
            {
                Logger.LogWarning("DivineFavorPowers idle. Needs TalentTree and Passive Powers.");
                return;
            }

            RemoveTalentTreeCycle();
            new Harmony(PluginGUID).PatchAll(typeof(CyclePatch));
            Logger.LogInfo("DivineFavorPowers " + PluginVersion + " loaded. Rank 3 cycles equipped Passive Powers.");
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

        private static bool CanCycle(Player player)
        {
            if (_hasTalent == null)
            {
                Type service = AccessTools.TypeByName("Core.TalentRuntimeService");
                _hasTalent = AccessTools.Method(service, "HasTalent", new[] { typeof(Player), typeof(string) });
            }

            if (_hasTalent == null || player == null)
            {
                return false;
            }

            return (bool)_hasTalent.Invoke(null, new object[] { player, CycleFlag });
        }

        private static bool ModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        private static bool ShouldDefer(Player player)
        {
            return player == Player.m_localPlayer
                && ZInput.GetButtonDown("GP")
                && player.TakeInput()
                && !Hud.InRadial()
                && !Hud.IsPieceSelectionVisible()
                && !ModifierHeld()
                && CanCycle(player);
        }

        private static bool ActivePowerEnabled(string power)
        {
            if (_activeEnabled == null)
            {
                Type utils = AccessTools.TypeByName("PassivePowers.Utils");
                _activeEnabled = AccessTools.Method(utils, "ActivePowerEnabled", new[] { typeof(string) });
            }

            if (_activeEnabled == null)
            {
                return false;
            }

            return (bool)_activeEnabled.Invoke(null, new object[] { power });
        }

        private static List<string> EquippedPowers(Player player)
        {
            string list = player.m_guardianPower;
            if (string.IsNullOrWhiteSpace(list))
            {
                return new List<string>();
            }

            return new List<string>(list.Split(','));
        }

        private static void WriteEquipped(Player player, List<string> powers)
        {
            string joined = string.Join(",", powers);
            player.m_guardianPower = joined;
            player.m_nview?.GetZDO()?.Set(EquippedKey, joined);
        }

        private static void ActivateFirst(Player player)
        {
            List<string> powers = EquippedPowers(player);
            if (powers.Count == 0 || string.IsNullOrEmpty(powers[0]))
            {
                return;
            }

            string power = powers[0];
            if (!ActivePowerEnabled(power))
            {
                return;
            }

            StatusEffect vanilla = ObjectDB.instance.GetStatusEffect(power.GetStableHashCode());
            StatusEffect passive = ObjectDB.instance.GetStatusEffect(("PassivePowers " + power).GetStableHashCode());
            if (vanilla == null || passive == null)
            {
                return;
            }

            player.m_guardianSE = vanilla;
            player.StartGuardianPower();
            player.m_guardianSE = passive;
        }

        private static void Rotate(Player player)
        {
            List<string> powers = EquippedPowers(player);
            if (powers.Count == 0)
            {
                return;
            }

            if (powers.Count > 1)
            {
                string first = powers[0];
                powers.RemoveAt(0);
                powers.Add(first);
                WriteEquipped(player, powers);
            }

            StatusEffect shown = ObjectDB.instance.GetStatusEffect(powers[0].GetStableHashCode());
            if (shown != null)
            {
                player.Message(MessageHud.MessageType.Center, shown.m_name, 0, shown.m_icon);
            }
        }

        [HarmonyPatch]
        private static class CyclePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
            private static bool BlockPlainPress(Player __instance)
            {
                return !ShouldDefer(__instance);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Player), nameof(Player.Update))]
            private static void TrackHold(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                {
                    return;
                }

                if (ZInput.GetButtonDown("GP"))
                {
                    _armed = ShouldDefer(__instance);
                    _cycles = 0;
                    return;
                }

                if (!_armed)
                {
                    return;
                }

                if (!ZInput.GetButton("GP"))
                {
                    _armed = false;
                    if (_cycles == 0)
                    {
                        ActivateFirst(__instance);
                    }

                    return;
                }

                if (!__instance.TakeInput())
                {
                    _armed = false;
                    return;
                }

                if (ZInput.GetButtonPressedTimer("GP") >= HoldThreshold + _cycles * CycleInterval)
                {
                    _cycles++;
                    Rotate(__instance);
                }
            }
        }
    }
}
