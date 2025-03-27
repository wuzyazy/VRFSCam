using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using HarmonyLib;
using Il2Cpp;
using UnityEngine.Networking;
using Il2CppTMPro;
using UnityEngine.UI;
using System.Linq;
using System;
using System.Collections.Generic;
using DiscordRPC;
using DiscordRPC.Logging;
using Il2CppPhoton.Pun;
using UnityEngine.SceneManagement;
using Il2CppSteamworks;


[assembly: MelonInfo(typeof(VRFSCam.Core), "VRFSCam+", "0.0.2", "seby", null)]
[assembly: MelonGame("VRFS", "Camera")]

namespace VRFSCam
{
    public class Core : MelonMod
    {
        public static TextMeshProUGUI textMeshPro;
        public static Ruleset ruleset;
        public const string version = "0.0.3";
        private DiscordRpcClient client;
        private Camera mainCamera;
        public static float defaultFOV = 60f;
        private float targetFOV;
        private float zoomSpeed = 40f;
        private float lerpSpeed = 10f;
        private float minFOV = 1f;
        private float maxFOV = 120f;
        public static PCMove instance;
        private Vector3 posbeforemain; 
        private Quaternion rotbeforemain; 

        private GameObject canvasObject;
        public static Keyboard keyboard;
        public static bool ballFollowingMode = false;
        private GameObject ball;


        private GameObject textObject;

        private HarmonyLib.Harmony harmony;

        public static int blueScore;
        public static int redScore;

        public static float remainingMatchTime;
        public float rotationSpeed = 5f;
        public float baseOffsetDistance = 5f;
        public float offsetHeight = 2f;
        public float maxOffsetChange = 2f;
        public float velocityInfluence = 1f;
        private Vector3 currentOffset;
        private Vector3 ballLastPosition;
        private bool foundBall = false;

        private static MelonPreferences_Category configCategory;
        private static MelonPreferences_Entry<float> maxZoomdis;
        private static MelonPreferences_Entry<float> zoomfactors;


        public static bool mainMode = false; 

        private bool cameraPositionSet = false; 
        private bool cameraRotationSet = false; 


        public float zoomFactor { get { return zoomfactors.Value; } }
        public float maxZoomDistance { get { return maxZoomdis.Value; } }

        public float baseFOV = 60f;         

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg($"VRFSCam+ {version} by Seby");
            client = new DiscordRpcClient("1354484678748409966"); // main rpc init
            client.Logger = new ConsoleLogger() { Level = LogLevel.Warning };

            client.OnReady += (sender, e) =>
            {
                LoggerInstance.Msg($"[VRFSCam+] Connected to RPC Discord as {e.User.DisplayName}");
            };

            client.Initialize();
            UpdateRichPresence("Singleplayer", "VRFSCam+");


            // For dynamic sensivity adjustment
            harmony = new HarmonyLib.Harmony("com.immersification.vrfs");
            harmony.PatchAll();

            configCategory = MelonPreferences.CreateCategory("VRFSCam+");
            zoomfactors = configCategory.CreateEntry("Zoomfactor", 0.9f, "Zoom Factor", "Zoom multiplier (default 0.9)");
            maxZoomdis = configCategory.CreateEntry("maxZooomdistance", 110f, "Max Zoom Distance", "Distance at which zoom is maximum (default 110)");
        }

        public override void OnApplicationQuit()
        {
            client.Dispose();
        }
        private void UpdateRichPresence(string state, string details)
        {
            client.SetPresence(new RichPresence()
            {
                
                Details = details,
                State = state,
                Assets = new Assets()
                {
                    LargeImageKey = null,
                    LargeImageText = "VRFSCam+"
                }
            });
        }

        [HarmonyPatch(typeof(PCMove), "Update")]
        public static class disableinput
        {
            public static bool Prefix(PCMove __instance)
            {
                instance = __instance;
                if (mainMode)
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
        }

        // For dynamic sensivity adjustment
        [HarmonyPatch(typeof(PCMove), "Update")]
        class dynamicfov
        {
            static void Postfix(PCMove __instance)
            {
                float currentFOV = Camera.main.fieldOfView;


                __instance.sensivity = 5f * (currentFOV / defaultFOV);
            }
        }

        [HarmonyPatch(typeof(Debug))]
        [HarmonyPatch("Log", new Type[] { typeof(Il2CppSystem.Object) })]
        class DebugPatch
        {
            static void Postfix(Il2CppSystem.Object message)
            {
                MelonLogger.Msg($"[UnityLogs] {message?.ToString()}");
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(PhotonConnector), "CreateRoomWithParameters")]
        class Patch_PhotonConnector_CreateRoomWithParameters
        {
            [HarmonyLib.HarmonyPrefix]
            static void Prefix(ref byte maxPlayers, ref int arenaID, ref int isItLobby, ref int pinCode, ref int isRankRoom, [System.Runtime.InteropServices.Optional] ref IncentiveMatchPreset incentiveMatchPreset)
            {
                pinCode = 123456;
                maxPlayers = 200;

                // you can delete this whole class - its only for me to make custom pincodes
            }
        }
        public override void OnLateInitializeMelon()
        {
            keyboard = Keyboard.current;
            currentOffset = new Vector3(0, offsetHeight, -baseOffsetDistance);


        }




        private void CheckText()
        {
            if (textObject == null)
            {
                MelonLogger.Msg("[VRFSCam+] Attempting text ");
               CreateText();
             
            }
        }

        private void CreateText() // [Main UI Initialization]
        {
            if (canvasObject == null)
            {
                canvasObject = new GameObject("SebyCanvas");
                Canvas canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            if (textObject != null) GameObject.Destroy(textObject);
            textObject = new GameObject("SebyText");
            textMeshPro = textObject.AddComponent<TextMeshProUGUI>();
            textMeshPro.text = "VRFSCam+"; // how to add default vrfs font??
            textMeshPro.fontSize = 36;
            textMeshPro.color = new Color(1f, 1f, 1f, 0.2f);
            textMeshPro.alignment = TextAlignmentOptions.BottomLeft;
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rectTransform = textMeshPro.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = Vector2.zero;
            rectTransform.anchoredPosition = new Vector2(20, 20);
        }

        private GameObject FindBall()
        {

            int ballLayer = LayerMask.NameToLayer("Ball");
            if (ballLayer == -1)
            {
                LoggerInstance.Warning("ball layer not found what the sigma?");
                return null;
            }


            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.layer == ballLayer && obj.name.StartsWith("BallSingle(Clone)"))
                {
                    return obj;
                }
            }
            return null;
        }

 


        public void GetScores()
        {
            GameObject multiplayer = GameObject.Find("Multiplayer");
            if (multiplayer != null)
            {
                var matchManager = multiplayer.GetComponent<MatchManager>();
                if (matchManager != null)
                {
                    ruleset = matchManager.oau; // obfuscated ahh
                    remainingMatchTime = matchManager.bbgp; // obfuscated property

                    var scores = matchManager.obd.rhl;
                    if (scores != null && scores.Length >= 2)
                    {
                        blueScore = scores[0]; 
                        redScore = scores[1]; 
                    }
                }
            }
        }

        private string FormatTime(float timeInSeconds)
        {
            int minutes = Mathf.FloorToInt(timeInSeconds / 60);
            int seconds = Mathf.FloorToInt(timeInSeconds % 60);
            return $"{minutes:D2}:{seconds:D2}"; // ONG i dont even know if ts works
        }
        public override void OnUpdate()
        {
            CheckText();





            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    defaultFOV = mainCamera.fieldOfView;
                    targetFOV = defaultFOV;
                }
            }

            if (mainCamera == null || keyboard == null) return;

            if (!mainMode)
            {

                if (keyboard.bKey.isPressed)
                {
                    targetFOV = Mathf.Max(targetFOV - zoomSpeed * Time.deltaTime, minFOV);
                }

                if (keyboard.vKey.isPressed)
                {
                    targetFOV = Mathf.Min(targetFOV + zoomSpeed * Time.deltaTime, maxFOV);
                }


                if (keyboard.rKey.wasPressedThisFrame)
                {
                    targetFOV = defaultFOV;
                    LoggerInstance.Msg("FOV Reset to Default");
                }


                mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFOV, lerpSpeed * Time.deltaTime);
            }
            









            if (keyboard.mKey.wasPressedThisFrame)
            {
                mainMode = !mainMode;
                LoggerInstance.Msg($"Main Mode toggled: {mainMode}");


                if (mainMode && !cameraPositionSet)
                {
                    posbeforemain = mainCamera.transform.position;
                    rotbeforemain = mainCamera.transform.rotation;
                    cameraPositionSet = true;
                    cameraRotationSet = true;
                }
                else if (!mainMode)
                {
                    cameraPositionSet = false; 
                    cameraRotationSet = false;


                     mainCamera.transform.rotation = rotbeforemain;
                }
            }

            if (PhotonNetwork.IsConnected)
            {
                GetScores();
                int playerCount = PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;

                UpdateRichPresence($"Blue: {blueScore} | Red: {redScore} | {playerCount} players in lobby | Time Remaining: {FormatTime(remainingMatchTime)}", "In-Game | VRFSCam+ " + version);

            }
            else if (SceneManager.GetActiveScene().name == "SB")
            {
                UpdateRichPresence("Singleplayer", "Version " + version);
            }
            else if (SceneManager.GetActiveScene().name == "Intermediate")
            {
                UpdateRichPresence("Connecting to server", "Version " + version);
            }

            if (mainMode)
            {
                if (ball == null || !foundBall)
                {
                    ball = FindBall();
                    if (ball != null)
                    {

                        LoggerInstance.Msg($"[VRFSCam+] Ball found: {ball.name} on layer {LayerMask.LayerToName(ball.layer)}");
                        ballLastPosition = ball.transform.position;
                        foundBall = true;
                    }
                    else
                    {
                        LoggerInstance.Msg($"[VRFSCam+] Ball NOT found, looking...");
                    }
                }
            }


            if (ball != null && mainMode)
            {

                Quaternion targetRotation = Quaternion.LookRotation(ball.transform.position - mainCamera.transform.position);


                mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);


                mainCamera.transform.position = posbeforemain;


                float distance = Vector3.Distance(mainCamera.transform.position, ball.transform.position);


                float zoomAmount = Mathf.Clamp01(distance / maxZoomDistance); 
                float targetFOV = baseFOV - (zoomAmount * zoomFactor * baseFOV); 


                targetFOV = Mathf.Clamp(targetFOV, minFOV, baseFOV);


                mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFOV, Time.deltaTime * lerpSpeed);
            }
            else
            {
                if (!mainMode)
                {

                    mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFOV, Time.deltaTime * lerpSpeed);

                    cameraPositionSet = false;
                    cameraRotationSet = false;
                }
                foundBall = false; 
            }
        }
    }
}
