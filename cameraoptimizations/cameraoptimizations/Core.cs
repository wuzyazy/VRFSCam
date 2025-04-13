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
using System.Reflection;
using UniverseLib;

[assembly: MelonInfo(typeof(VRFSCam.Core), "VRFSCam+", "0.0.4", "seby", null)]
[assembly: MelonGame("VRFS", "Camera")]

namespace VRFSCam
{
    public class Core : MelonMod
    {
        #region Constants
        public const string Version = "0.0.4";
        private const string DiscordAppId = "1354484678748409966";
        private const string BallObjectPrefix = "BallSingle(Clone)";
        #endregion

        #region Private Fields
        // Camera Settings
        private Camera _mainCamera;
        private float _targetFOV;
        private float _zoomSpeed = 40f;
        private float _lerpSpeed = 10f;
        private float _minFOV = 1f;
        private float _maxFOV = 120f;
        private float _baseFOV = 60f;
        private float _zoomFactor = 0.9f; // Internal zoom factor
        private float _maxZoomDistance = 110f; // Internal max zoom distance

        // Camera Positioning
        private Vector3 _positionBeforeMainMode;
        private Quaternion _rotationBeforeMainMode;
        private bool _cameraPositionSet = false;

        // Ball Following
        private GameObject _ball;
        private Vector3 _currentOffset;
        private Vector3 _ballLastPosition;
        private bool _foundBall = false;
        private float _rotationSpeed = 5f;
        private float _baseOffsetDistance = 5f;
        private float _offsetHeight = 2f;

        // UI Elements
        private GameObject _canvasObject;
        private GameObject _textObject;
        private bool textHasBeenCreated = false;
        private bool _GUIInitialized = false;
        

        // Discord RPC
        private DiscordRpcClient _client;
        private bool _discordAvailable = false;

        // Harmony
        private HarmonyLib.Harmony _harmony;

        // Error tracking to prevent log spam
        private bool _ballLayerWarningShown = false;
        private bool _discordAssemblyErrorLogged = false; // Flag to track if assembly load error was logged
        #endregion

        #region Public Properties

        public static GameObject UIobj;

        public static Slider zoomfactorslider;

        public static Slider distancetomaxslider;

        public static TextMeshProUGUI distancetomaxvalue;

        public static TextMeshProUGUI zoomfactortext;

        public static bool isMenuActive = false;

        public static UnityEngine.UI.Button resetzoomfactorbtn;

        public static UnityEngine.UI.Button resetdistancebtn;
        public static TextMeshProUGUI TextMeshPro { get; private set; }
        public static Ruleset Ruleset { get; private set; }
        public static float DefaultFOV { get; private set; } = 60f;

        public static PCMove Instance;
        public static bool BallFollowingMode { get; set; } = false;
        public static bool MainMode { get; set; } = false;
        public static int BlueScore { get; private set; }
        public static int RedScore { get; private set; }
        public static float RemainingMatchTime { get; private set; }
        public static Keyboard Keyboard { get; private set; }

        public float ZoomFactor
        {
            get => _zoomFactor;
            set => _zoomFactor = value;
        }
        public float MaxZoomDistance
        {
            get => _maxZoomDistance;
            set => _maxZoomDistance = value;
        }
        #endregion

        #region Initialization
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg($"VRFSCam+ {Version} by Seby");

            try
            {
                InitializeDiscordRPC();
            }
            catch (System.IO.FileNotFoundException fnfEx) when (fnfEx.FileName != null && fnfEx.FileName.Contains("DiscordRPC"))
            {
                // Catch specific assembly load error during initial call if InitializeDiscordRPC fails immediately
                LogDiscordAssemblyErrorOnce($"Discord assembly not found during initial setup: {fnfEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (System.IO.FileLoadException flEx) when (flEx.FileName != null && flEx.FileName.Contains("DiscordRPC"))
            {
                LogDiscordAssemblyErrorOnce($"Failed to load Discord assembly during initial setup: {flEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (Exception ex)
            {
                // Catch any other general initialization errors
                LoggerInstance.Warning($"Discord RPC initialization failed: {ex.Message}. Rich presence will be disabled.");
                _discordAvailable = false;
                SafelyDisposeDiscordClient();
            }

            try
            {
                InitializeHarmony();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to initialize Harmony patches: {ex.Message}");
            }
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                Keyboard = Keyboard.current;
                if (Keyboard == null)
                {
                    LoggerInstance.Warning("Keyboard input not available. Some features may not work correctly.");
                }
                CheckText();
                _currentOffset = new Vector3(0, _offsetHeight, -_baseOffsetDistance);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error in late initialization: {ex.Message}");
            }
        }

        private void InitializeDiscordRPC()
        {
            try
            {
                // Check if the Discord DLL is available before trying to use it
                var discordAssembly = System.Reflection.Assembly.Load("DiscordRPC"); // This might throw
                if (discordAssembly == null)
                {
                    // This path might not be reachable if Load throws, but kept for safety
                    LogDiscordAssemblyErrorOnce("Discord RPC assembly could not be loaded (Load returned null). Rich presence will be disabled.");
                    return;
                }

                _client = new DiscordRpcClient(DiscordAppId);
                _client.Logger = new ConsoleLogger() { Level = LogLevel.Warning };

                _client.OnReady += (sender, e) =>
                {
                    LoggerInstance.Msg($"[VRFSCam+] Connected to Discord RPC as {e.User.DisplayName}");
                };

                _client.OnConnectionFailed += (sender, e) =>
                {
                    // Handle connection failure, distinct from assembly load failure
                    if (_discordAvailable)
                    {
                        LoggerInstance.Warning("Discord connection failed. Rich presence will be disabled.");
                        _discordAvailable = false;
                        SafelyDisposeDiscordClient();
                    }
                };

                // Set a connection timeout
                bool initialized = false;
                System.Threading.Tasks.Task.Run(() => {
                    try {
                        _client.Initialize();
                        initialized = true;
                    }
                    catch (Exception ex) {
                        // Catch errors during async initialization
                        if (_discordAvailable) // Check flag to avoid duplicate logs if assembly load failed earlier
                        {
                            LoggerInstance.Warning($"Discord RPC initialization failed in thread: {ex.Message}");
                            _discordAvailable = false;
                            SafelyDisposeDiscordClient();
                        }
                    }
                });

                // Wait for a short time, but don't block indefinitely
                System.Threading.Thread.Sleep(100);

                if (!initialized && _discordAvailable) // Only log timeout if not already disabled
                {
                    LoggerInstance.Warning("Discord RPC initialization timed out. Rich presence will be disabled.");
                    _discordAvailable = false;
                    SafelyDisposeDiscordClient();
                    return;
                }

                if (initialized) {
                    _discordAvailable = true; // Mark as available only if successfully initialized
                    UpdateRichPresence("Singleplayer", "VRFSCam+");
                }
            }
            catch (System.IO.FileNotFoundException fnfEx) when (fnfEx.FileName != null && fnfEx.FileName.Contains("DiscordRPC"))
            {
                LogDiscordAssemblyErrorOnce($"Discord assembly not found during initialization: {fnfEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (System.IO.FileLoadException flEx) when (flEx.FileName != null && flEx.FileName.Contains("DiscordRPC"))
            {
                LogDiscordAssemblyErrorOnce($"Failed to load Discord assembly during initialization: {flEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (Exception ex) // Catch other initialization errors
            {
                if (_discordAvailable) // Check flag to avoid duplicate logs
                {
                    LoggerInstance.Warning($"Discord RPC initialization failed: {ex.Message}. Rich presence will be disabled.");
                    _discordAvailable = false;
                    SafelyDisposeDiscordClient();
                }
            }
        }

        private void InitializeHarmony()
        {
            _harmony = new HarmonyLib.Harmony("com.immersification.vrfs");
            _harmony.PatchAll();
        }

        #endregion

        #region Discord RPC
        private void UpdateRichPresence(string state, string details)
        {
            if (!_discordAvailable || _client == null) return;

            try
            {
                // Add extra check for client initialization status before use
                if (!_client.IsInitialized)
                {
                    if (_discordAvailable) // Log only once if it becomes uninitialized
                    {
                        LoggerInstance.Warning("Discord client no longer initialized during update. Disabling rich presence.");
                        _discordAvailable = false;
                        SafelyDisposeDiscordClient();
                    }
                    return;
                }

                _client.SetPresence(new RichPresence()
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
            catch (Exception ex)
            {
                // Only log once to prevent spam
                if (_discordAvailable)
                {
                    LoggerInstance.Warning($"Failed to update Discord rich presence: {ex.Message}");
                    _discordAvailable = false;
                    SafelyDisposeDiscordClient(); // Dispose on failure
                }
            }
        }

        public override void OnApplicationQuit()
        {
            // Check flag before attempting disposal
            if (_discordAvailable && _client != null)
            {
                try
                {
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"Error during Discord client disposal: {ex.Message}");
                }
            }
            _client = null; // Ensure client is null after disposal attempt
            _discordAvailable = false; // Mark as unavailable on quit
        }
        #endregion

        #region UI Management
        private void CheckText()
        {
            if (_textObject == null)
            {
                try
                {
                    if (!textHasBeenCreated) // Only create text if it hasn't been created yet
                    {
                        textHasBeenCreated = true;
                        MelonLogger.Msg("[VRFSCam+] Initializing UI");
                        CreateText();
                        MelonCoroutines.Start(LoadUI()); // Start loading UI
                    }
                    
                }
                catch (Exception ex)
                {
                    LoggerInstance.Warning($"Failed to create UI text: {ex.Message}");
                }
            }
        }
        
        private IEnumerator LoadUI()
        {
            // Skip if UI is already initialized
            if (_GUIInitialized)
            {
                yield break;
            }

            // UnityWebRequest uwr = UnityWebRequest.Get("https://files.catbox.moe/742imh.asset"); Old UI v0.1
            //  UnityWebRequest uwr = UnityWebRequest.Get("https://files.catbox.moe/u8hqev.asset");  New UI v0.2
            UnityWebRequest uwr = UnityWebRequest.Get("https://files.catbox.moe/s3rj8y.asset"); // New UI v0.3 -- fixed panel size
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"failed to download AssetBundle: {uwr.error}");
                yield break;
            }

            UnityEngine.AssetBundle bundle = UnityEngine.AssetBundle.LoadFromMemory(uwr.downloadHandler.data);
            if (bundle == null)
            {
                Debug.LogError("failed to load assetbundle");
                yield break;
            }

            GameObject prefab = bundle.LoadAsset<GameObject>("VRFSCamSettings");
            if (prefab == null)
            {
                Debug.LogError("prefab not found in assetbundle");
                yield break;
            }



            UIobj = GameObject.Instantiate(prefab);

            prefab.SetActive(false);

            

            UIobj.SetActive(false);

            UnityEngine.Object.DontDestroyOnLoad(UIobj);

            

            resetzoomfactorbtn = UIobj.transform.Find("VRFSCamPanel/resetzoomfactorbtn").GetComponent<UnityEngine.UI.Button>();

            zoomfactortext = UIobj.transform.Find("VRFSCamPanel/zoomfactorvalue").GetComponent<TextMeshProUGUI>();
       
            resetdistancebtn = UIobj.transform.Find("VRFSCamPanel/resetdistancebtn").GetComponent<UnityEngine.UI.Button>();
      
            zoomfactorslider = UIobj.transform.Find("VRFSCamPanel/zoomfactorslider").GetComponent<Slider>();

            zoomfactorslider.maxValue = 2.5f;

            zoomfactorslider.onValueChanged.AddListener((UnityAction<float>)delegate (float value)
            {
                _zoomFactor = value;
                zoomfactortext.text = value.ToString("F1");
            });

            distancetomaxvalue = UIobj.transform.Find("VRFSCamPanel/distancetomaxvalue").GetComponent<TextMeshProUGUI>();

            distancetomaxslider = UIobj.transform.Find("VRFSCamPanel/distancetomaxslider").GetComponent<Slider>();

            distancetomaxslider.maxValue = 250f;    

            distancetomaxslider.onValueChanged.AddListener((UnityAction<float>)delegate (float value)
            {
                _maxZoomDistance = value;
                distancetomaxvalue.text = value.ToString("F1");
            });

            // Behold the Initialization of Buttons...

            resetzoomfactorbtn.onClick.AddListener(() =>
            {
                _zoomFactor = 0.9f;
                zoomfactortext.text = _zoomFactor.ToString("F1");
                zoomfactorslider.value = _zoomFactor;
            });

            resetdistancebtn.onClick.AddListener(() => 
            {
                _maxZoomDistance = 110f;
                distancetomaxvalue.text = _maxZoomDistance.ToString("F1");
                distancetomaxslider.value = _maxZoomDistance;
            });















            bundle.Unload(false);

            // Mark UI as initialized
            _GUIInitialized = true;
        }



        
        
        
        public void OnResetZoomButtonClick()
        {
            MelonLogger.Msg("[VRFSCam+] [UI] Reset Zoom Button clicked!");
        }

        private void CreateText()
        {
            try
            {
                // Only start LoadUI coroutine if UI gameobject exists OR if gui is initialized
                if (!_GUIInitialized || UIobj == null)
                {
                    // MelonCoroutines.Start(LoadUI());
                }


                if (_canvasObject == null)
                {
                    _canvasObject = new GameObject("SebyCanvas");
                    Canvas canvas = _canvasObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _canvasObject.AddComponent<CanvasScaler>();
                    _canvasObject.AddComponent<GraphicRaycaster>();
                    UnityEngine.Object.DontDestroyOnLoad(_canvasObject); // Removed the need to recreate the text every time
                }

                if (_textObject != null)
                    GameObject.Destroy(_textObject);

                _textObject = new GameObject("SebyText");
                TextMeshPro = _textObject.AddComponent<TextMeshProUGUI>();
                TextMeshPro.text = "VRFSCam+";
                TextMeshPro.fontSize = 36;
                TextMeshPro.color = new Color(1f, 1f, 1f, 0.2f);
                TextMeshPro.alignment = TextAlignmentOptions.BottomLeft;
                _textObject.transform.SetParent(_canvasObject.transform, false);

                RectTransform rectTransform = TextMeshPro.rectTransform;
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.zero;
                rectTransform.pivot = Vector2.zero;
                rectTransform.anchoredPosition = new Vector2(20, 20);
                
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error creating UI text: {ex.Message}");
            }
        }
        #endregion

        #region Game State Management
        public void GetScores()
        {
            try
            {
                GameObject multiplayer = GameObject.Find("Multiplayer");
                if (multiplayer != null)
                {
                    var matchManager = multiplayer.GetComponent<MatchManager>();
                    if (matchManager != null)
                    {
                        Ruleset = matchManager.oau; // obfuscated property
                        RemainingMatchTime = matchManager.bbgp; // obfuscated property

                        var scores = matchManager.obd.rhl;
                        if (scores != null && scores.Length >= 2)
                        {
                            BlueScore = scores[0];
                            RedScore = scores[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't spam logs with this error
                LoggerInstance.Warning($"Failed to get match scores: {ex.Message}");
            }
        }

        private string FormatTime(float timeInSeconds)
        {
            try
            {
                int minutes = Mathf.FloorToInt(timeInSeconds / 60);
                int seconds = Mathf.FloorToInt(timeInSeconds % 60);
                return $"{minutes:D2}:{seconds:D2}";
            }
            catch
            {
                return "00:00"; // Fallback value
            }
        }
        #endregion

        #region Ball Tracking
        private GameObject FindBall()
        {
            try
            {
                int ballLayer = LayerMask.NameToLayer("Ball");
                if (ballLayer == -1)
                {
                    // Only show this warning once to prevent log spam
                    if (!_ballLayerWarningShown)
                    {
                        LoggerInstance.Warning("Ball layer not found");
                        _ballLayerWarningShown = true;
                    }
                    return null;
                }

                GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
                foreach (GameObject obj in allObjects)
                {
                    if (obj.layer == ballLayer && obj.name.StartsWith(BallObjectPrefix))
                    {
                        _ballLayerWarningShown = false; // Reset warning flag if we find the ball
                        return obj;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error finding ball: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Main Update Loop
        public override void OnUpdate()
        {
            try
            {
                
              //  if (isMenuActive)
             //  {
              //      zoomfactorslider = GameObject.Find("zoomfactorslider").GetComponent<Slider>();
             //       zoomfactortext = GameObject.Find("zoomfactorvalue").GetComponent<TextMeshPro>(); 
            //        zoomfactortext.text = zoomfactorslider.value.ToString("F1");
           //         zoomfactorslider.value = _zoomFactor;
           //     }
              
  


                // Initialize camera if needed
                if (_mainCamera == null)
                {
                    _mainCamera = Camera.main;
                    if (_mainCamera != null)
                    {
                        DefaultFOV = _mainCamera.fieldOfView;
                        _targetFOV = DefaultFOV;
                        _baseFOV = DefaultFOV;
                    }
                }

                if (_mainCamera == null || Keyboard == null)
                    return;

                HandleCameraControls();
                HandleMainModeToggle();

                // Only attempt Discord update if it's available
                if (_discordAvailable)
                {
                    UpdateDiscordStatus();
                }

                HandleBallTracking();
            }
            catch (System.IO.FileNotFoundException fnfEx) when (fnfEx.FileName != null && fnfEx.FileName.Contains("DiscordRPC"))
            {
                // Catch Discord assembly load error specifically
                LogDiscordAssemblyErrorOnce($"Discord assembly not found in update loop, disabling Discord features: {fnfEx.Message}");
                SafelyDisposeDiscordClient();
                // Suppress further logging for this specific error
            }
            catch (System.IO.FileLoadException flEx) when (flEx.FileName != null && flEx.FileName.Contains("DiscordRPC"))
            {
                // Catch Discord assembly load error specifically
                LogDiscordAssemblyErrorOnce($"Failed to load Discord assembly in update loop, disabling Discord features: {flEx.Message}");
                SafelyDisposeDiscordClient();
                // Suppress further logging for this specific error
            }
            catch (Exception ex)
            {
                // Log other, non-Discord-assembly related errors from the update loop normally
                // Avoid logging if it's a consequence of Discord being unavailable and already handled
                if (_discordAvailable || !_discordAssemblyErrorLogged) {
                    LoggerInstance.Error($"Error in main update loop: {ex.Message}");
                }
            }
        }

        private void HandleCameraControls()
        {
            try
            {
                if (!MainMode)
                {
                    if (Keyboard.bKey.isPressed)
                    {
                        _targetFOV = Mathf.Max(_targetFOV - _zoomSpeed * Time.deltaTime, _minFOV);
                    }

                    if (Keyboard.vKey.isPressed)
                    {
                        _targetFOV = Mathf.Min(_targetFOV + _zoomSpeed * Time.deltaTime, _maxFOV);
                    }

                    if (Keyboard.rKey.wasPressedThisFrame)
                    {
                        _targetFOV = DefaultFOV;
                        LoggerInstance.Msg("FOV Reset to Default");
                    }

                    _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, _targetFOV, _lerpSpeed * Time.deltaTime);
                }

                if (Keyboard.pKey.wasPressedThisFrame)
                {
                    ToggleMenu();
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error handling camera controls: {ex.Message}");
            }
        }

        private void HandleMainModeToggle()
        {
            try
            {
                if (Keyboard.mKey.wasPressedThisFrame)
                {
                    MainMode = !MainMode;
                    LoggerInstance.Msg($"Main Mode toggled: {MainMode}");

                    if (MainMode && !_cameraPositionSet)
                    {
                        _positionBeforeMainMode = _mainCamera.transform.position;
                        _rotationBeforeMainMode = _mainCamera.transform.rotation;
                        _cameraPositionSet = true;
                    }
                    else if (!MainMode)
                    {
                        _cameraPositionSet = false;
                        _mainCamera.transform.rotation = _rotationBeforeMainMode;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error handling main mode toggle: {ex.Message}");
            }
        }

        private void ToggleMenu()
        {
            if (UIobj == null) return;

            isMenuActive = !isMenuActive; 
            UIobj.SetActive(isMenuActive); // Only set active if menu is active

            
            Cursor.visible = isMenuActive;
            Cursor.lockState = isMenuActive ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void UpdateDiscordStatus()
        {
            // Early return if Discord is not available or client is null
            if (!_discordAvailable || _client == null) return;

            try
            {
                // Verify client is still valid before using it
                if (!_client.IsInitialized)
                {
                    if (_discordAvailable) // Only log the warning once
                    {
                        LoggerInstance.Warning("Discord client is no longer initialized. Disabling rich presence.");
                        _discordAvailable = false;
                        SafelyDisposeDiscordClient(); // Dispose if it became uninitialized unexpectedly
                    }
                    return;
                }

                if (PhotonNetwork.IsConnected)
                {
                    GetScores();
                    int playerCount = PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;

                    UpdateRichPresence(
                        $"Blue: {BlueScore} | Red: {RedScore} | {playerCount} players in lobby | Time Remaining: {FormatTime(RemainingMatchTime)}",
                        "In-Game | VRFSCam+ " + Version);
                }
                else if (SceneManager.GetActiveScene().name == "SB")
                {
                    UpdateRichPresence("Singleplayer", "Version " + Version);
                }
                else if (SceneManager.GetActiveScene().name == "Intermediate")
                {
                    UpdateRichPresence("Connecting to server", "Version " + Version);
                }
            }
            // Catch specific assembly load errors that might *still* occur here in rare JIT cases
            catch (System.IO.FileNotFoundException fnfEx) when (fnfEx.FileName != null && fnfEx.FileName.Contains("DiscordRPC"))
            {
                LogDiscordAssemblyErrorOnce($"Discord assembly not found during status update: {fnfEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (System.IO.FileLoadException flEx) when (flEx.FileName != null && flEx.FileName.Contains("DiscordRPC"))
            {
                LogDiscordAssemblyErrorOnce($"Failed to load Discord assembly during status update: {flEx.Message}");
                SafelyDisposeDiscordClient();
            }
            catch (Exception ex)
            {
                // Only log if it was supposed to be working
                if (_discordAvailable)
                {
                    LoggerInstance.Warning($"Error updating Discord status: {ex.Message}");
                    _discordAvailable = false; // Disable on update failure
                    SafelyDisposeDiscordClient(); // Dispose on failure
                }
                // If !_discordAvailable, suppress repeated update errors
            }
        }

        private void HandleBallTracking()
        {
            try
            {
                if (MainMode)
                {
                    if (_ball == null || !_foundBall)
                    {
                        _ball = FindBall();
                        if (_ball != null)
                        {
                            LoggerInstance.Msg($"[VRFSCam+] Ball found: {_ball.name} on layer {LayerMask.LayerToName(_ball.layer)}");
                            _ballLastPosition = _ball.transform.position;
                            _foundBall = true;
                        }
                    }
                }

                if (_ball != null && MainMode)
                {
                    // Rotate camera to look at ball
                    Quaternion targetRotation = Quaternion.LookRotation(_ball.transform.position - _mainCamera.transform.position);
                    _mainCamera.transform.rotation = Quaternion.Slerp(_mainCamera.transform.rotation, targetRotation, Time.deltaTime * _rotationSpeed);

                    // Maintain original position
                    _mainCamera.transform.position = _positionBeforeMainMode;

                    // Calculate dynamic FOV based on distance
                    float distance = Vector3.Distance(_mainCamera.transform.position, _ball.transform.position);
                    float zoomAmount = Mathf.Clamp01(distance / MaxZoomDistance);
                    float targetFOV = _baseFOV - (zoomAmount * ZoomFactor * _baseFOV);
                    targetFOV = Mathf.Clamp(targetFOV, _minFOV, _baseFOV);

                    _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, targetFOV, Time.deltaTime * _lerpSpeed);
                }
                else
                {
                    if (!MainMode)
                    {
                        _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, _targetFOV, Time.deltaTime * _lerpSpeed);
                        _cameraPositionSet = false;
                    }
                    _foundBall = false;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error in ball tracking: {ex.Message}");
            }
        }
        #endregion


        #region Helper Methods
        // Helper to log assembly errors only once
        private void LogDiscordAssemblyErrorOnce(string message)
        {
            if (!_discordAssemblyErrorLogged)
            {
                LoggerInstance.Error(message);
                _discordAvailable = false; // Ensure Discord is marked as unavailable
                _discordAssemblyErrorLogged = true; // Prevent further logging of this specific error
            }
        }

        // Helper to safely dispose of the Discord client
        private void SafelyDisposeDiscordClient()
        {
            try
            {
                if (_client != null)
                {
                    // Check if client implements IDisposable before calling Dispose
                    if (_client is IDisposable disposableClient)
                    {
                        disposableClient.Dispose();
                    }
                    _client = null; // Set to null after disposal attempt
                }
            }
            catch (Exception disposeEx)
            {
                // Log disposal error once if needed, but avoid spam
                LoggerInstance.Warning($"Error disposing Discord client after failure: {disposeEx.Message}");
            }
            finally
            {
                _discordAvailable = false; // Ensure flag is false after disposal attempt
                _client = null; // Ensure client reference is cleared
            }
        }
        #endregion
    }
}
