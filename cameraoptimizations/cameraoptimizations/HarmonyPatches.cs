using DiscordRPC;
using DiscordRPC.Logging;
using HarmonyLib;
using Il2Cpp;
using Il2CppHutongGames.PlayMaker.Actions;
using Il2CppInterop.Runtime;
using Il2CppPhoton.Pun;
using Il2CppSteamworks;
using Il2CppTMPro;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRFSCam
{
    public class HarmonyPatches
    {
        [HarmonyPatch(typeof(PCMove), "Update")]
        public static class DisableInputPatch
        {
            public static bool Prefix(PCMove __instance)
            {
                try
                {
                    Core.Instance = __instance;

                    if (Core.MainMode)
                    {
                        __instance.stopInput = true;
                        __instance.stopRotate = true;
                        return false;
                    }
                    else
                    {
                        __instance.stopInput = false;
                        __instance.stopRotate = false;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error in DisableInputPatch: {ex.Message}");
                    return true; // Default to allowing the original method to run
                }
            }



        }

        // Additional harmony patch to prevent the camera from moving when the settings are open
        [HarmonyPatch(typeof(PCMove), "Update")]
        public static class togglemenupatch
        {
            public static bool Prefix(PCMove __instance)
            {
                try
                {

                    if (Core.isMenuActive)
                    {
                        __instance.stopInput = true;
                        __instance.stopRotate = true;
                        return false;
                    }
                    else
                    {
                        __instance.stopInput = false;
                        __instance.stopRotate = false;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error in togglemenupatch: {ex.Message}");
                    return true; // Default to allowing the original method to run
                }
            }


        }

        [HarmonyPatch(typeof(PCMove), "Update")]
        public static class DynamicFOVPatch
        {
            static void Postfix(PCMove __instance)
            {
                try
                {
                    float currentFOV = Camera.main.fieldOfView;
                    __instance.sensivity = 5f * (currentFOV / Core.DefaultFOV);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error in DynamicFOVPatch: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Debug))]
        [HarmonyPatch("Log", new Type[] { typeof(Il2CppSystem.Object) })]
        public static class DebugPatch
        {
            static void Postfix(Il2CppSystem.Object message)
            {
                try
                {
                    MelonLogger.Msg($"[UnityLogs] {message?.ToString()}");
                }
                catch
                {
                    // Silently fail to avoid log spam
                }
            }
        }

        // PhotonConnectorPatch removed - cannot force code in camera
    }
}
