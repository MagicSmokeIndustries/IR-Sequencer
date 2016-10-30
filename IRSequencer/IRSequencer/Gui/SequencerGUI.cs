﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IRSequencer.API;
using IRSequencer.Utility;
using IRSequencer.Core;
using IRSequencer.Module;
using KSP.UI.Screens;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using KSP.UI;

namespace IRSequencer.Gui
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SequencerFlight : SequencerGUI
    {
        public override string AddonName { get { return this.name; } }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class SequencerEditor : SequencerGUI
    {
        public override string AddonName { get { return this.name; } }
    }

    /// <summary>
    /// Class for creating and editing command queue for vessel's Infernal Robotic servos
    /// 
    /// So far relies on IR to parse all servos to ServoGroups
    /// </summary>
    public class SequencerGUI : MonoBehaviour
    {
        public virtual String AddonName { get; set; }

        private static GameObject _controlWindow;
        private static GameObject _settingsWindow;
        private static GameObject _editorWindow;

        private static CanvasGroupFader _controlWindowFader;
        private static CanvasGroupFader _editorWindowFader;
        private static CanvasGroupFader _settingsWindowFader;

        public static float _UIAlphaValue = 0.8f;
        public static float _UIScaleValue = 1.0f;
        private const float UI_FADE_TIME = 0.1f;
        private const float UI_MIN_ALPHA = 0.2f;
        private const float UI_MIN_SCALE = 0.5f;
        private const float UI_MAX_SCALE = 2.0f;

        internal static bool guiRebuildPending = false;

        public bool guiHidden = false;

        public bool GUIEnabled = false;
        public bool guiControlWindowEditMode = false;

        public bool alwaysStartInEditMode = false;

        private bool isReady = false;
        private bool firstUpdate = true;

        public bool isEnabled = true;

        internal static bool isKeyboardLocked = false;

        private ApplicationLauncherButton appLauncherButton;
        
        protected static Vector3 SequencerWindowPosition;
        protected static Vector3 SequencerEditorWindowPosition;
        protected static Vector3 SequencerSettingsWindowPosition;

        protected static Vector2 SequencerEditorWindowSize;

        protected static SequencerGUI SequencerInstance;

        public bool SequencerReady { get { return isReady; } }

        public static SequencerGUI Instance
        {
            get { return SequencerInstance; }
        }

        internal List<ModuleSequencer> sequencers;

        internal Sequence openSequence;
        internal int selectedGroupIndex = 0;
        internal int selectedServoIndex = 0;

        internal float moveToValue = 0f;
        internal float moveAtValue = 1.0f;
        internal KSPActionGroup selectedToggleAG = KSPActionGroup.Abort;
        internal KSPActionGroup selectedWaitAG = KSPActionGroup.Abort;

        internal int selectedToggleAGX = 0;
        internal int selectedWaitAGX = 0;
        internal float delayTimeValue = 1f;
        internal int repeatLineIndexValue = 0;
        internal int repeatTimesValue = -1;

        internal KSPActionGroup[] stockActionGroups;
        internal Dictionary<int, string> extendedActionGroups;
        internal List<Dropdown.OptionData> actionGroupsOptions;
        internal List<Dropdown.OptionData> actionGroupsExtendedOptions;
        
        internal Dictionary<SequencerState, GameObject> _stateUIControls;
        internal Dictionary<Sequence, GameObject> _sequenceUIControls;
        internal Dictionary<BasicCommand, GameObject> _openSequenceCommandControls;

        private static Vector2 commandProgressResetAnchor = new Vector2(0f, 1f);

        private void AddAppLauncherButton()
        {
            if (appLauncherButton == null && ApplicationLauncher.Ready && ApplicationLauncher.Instance != null)
            {
                try
                {
                    var texture = UIAssetsLoader.iconAssets.Find(t => t.name == "icon_seq_button");

                    if (ApplicationLauncher.Instance == null)
                    {
                        Logger.Log(string.Format("[GUI AddAppLauncher.Instance is null, PANIC!"), Logger.Level.Fatal);
                        return;
                    }
                    appLauncherButton = ApplicationLauncher.Instance.AddModApplication(ShowControlWindow,
                        CloseAllWindows, null, null, null, null,
                        ApplicationLauncher.AppScenes.FLIGHT, texture);

                }
                catch (Exception ex)
                {
                    Logger.Log(string.Format("[GUI AddAppLauncherButton Exception, {0}", ex.Message), Logger.Level.Fatal);
                    DestroyAppLauncherButton();
                    appLauncherButton = null;
                }
            }
        }

        public void CheckForServoSelection()
        {
            if (Input.GetMouseButtonDown(0) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) //Returns true during the frame the user pressed the given mouse button. 
                                                                                                                          //It will not return true until the user has released the mouse button and pressed it again.
            {
                RaycastHit hit;
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

                if (Physics.Raycast(ray, out hit))
                {
                    GameObject hitObject = hit.transform.gameObject;
                    if (hitObject == null)
                        return;

                    Part part = hitObject.GetComponentInParent<Part>();
                    if (part == null)
                        return;

                    for (int i = 0; i < IRWrapper.IRController.ServoGroups.Count; i++)
                    {
                        var g = IRWrapper.IRController.ServoGroups[i];
                        for (int j = 0; j < g.Servos.Count; j++)
                        {
                            var s = g.Servos[j];
                            if (s.HostPart == part)
                            {
                                selectedServoIndex = j;
                                selectedGroupIndex = i;
                                guiRebuildPending = true;
                                return;
                            }
                        }
                    }
                }
            }
        }

        public static Vector3 ClampWindowPosition(Vector3 windowPosition)
        {
            Canvas canvas = UIMasterController.Instance.appCanvas;
            RectTransform canvasRectTransform = canvas.transform as RectTransform;

            var windowPositionOnScreen = RectTransformUtility.WorldToScreenPoint(UIMasterController.Instance.uiCamera, windowPosition);

            float clampedX = Mathf.Clamp(windowPositionOnScreen.x, 0, Screen.width);
            float clampedY = Mathf.Clamp(windowPositionOnScreen.y, 0, Screen.height);

            windowPositionOnScreen = new Vector2(clampedX, clampedY);

            Vector3 newWindowPosition;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRectTransform, 
                windowPositionOnScreen, UIMasterController.Instance.uiCamera, out newWindowPosition))
            {
                return newWindowPosition;
            }
            else
                return Vector3.zero;
        }


        public void Update()
        {
            if (firstUpdate)
            {
                try
                {
                    IRWrapper.InitWrapper();
                }
                catch (Exception e)
                {
                    Logger.Log("[Sequencer] Exception while initialising API " + e.Message, Logger.Level.Debug);
                }
                firstUpdate = false;
            }
            else
            {
                //requires ServoGroups to be parsed
                if (!IRWrapper.APIReady)
                {
                    if (appLauncherButton != null)
                    {
                        appLauncherButton.VisibleInScenes = ApplicationLauncher.AppScenes.NEVER;
                    }
                    return;
                }

                if (appLauncherButton != null)
                {
                    appLauncherButton.VisibleInScenes = ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB;
                }

                if (HighLogic.LoadedSceneIsFlight)
                {
                    if (FlightGlobals.ActiveVessel == null)
                        return;

                    var sequencerModules = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleSequencer>();
                    if (GUIEnabled && (sequencerModules == null || sequencerModules.Count == 0))
                    {
                        ScreenMessages.PostScreenMessage("Sequencer module is required (add probe core).", 3, ScreenMessageStyle.UPPER_CENTER);
                        GUIEnabled = false;
                        CloseAllWindows();
                        return;
                    }
                }
                else if (HighLogic.LoadedSceneIsEditor)
                {
                    if (GUIEnabled && EditorLogic.fetch != null)
                    {
                        var s = EditorLogic.fetch.ship;
                        if (s != null)
                        {
                            var modulePart = s.Parts.Find(p => p.FindModuleImplementing<ModuleSequencer>() != null);
                            if (modulePart == null)
                            {
                                ScreenMessages.PostScreenMessage("Sequencer module is required (add probe core).", 3, ScreenMessageStyle.UPPER_CENTER);
                                GUIEnabled = false;
                                CloseAllWindows();
                                return;
                            }
                        }
                    }
                }

                if (!GUIEnabled)
                    return;

                if(EventSystem.current.currentSelectedGameObject != null && 
                    (EventSystem.current.currentSelectedGameObject.name.Contains("InputField") || EventSystem.current.currentSelectedGameObject.name.Contains("Key")))
                {
                    if(!isKeyboardLocked)
                        KeyboardLock(true); 
                }
                else
                {
                    if(isKeyboardLocked)
                        KeyboardLock(false);
                }

                if (guiRebuildPending && GUIEnabled)
                    RebuildUI();
                
                if (openSequence != null)
                {
                    if (openSequence.isActive || openSequence.isFinished || openSequence.isWaiting)
                    {
                        UpdateOpenSequenceCommandProgress();
                    }

                    CheckForServoSelection();

                    UpdateOpenSequenceControls();
                }

                //go through all modules and update UI controls accordingly
                if (sequencers == null || sequencers.Count == 0
                    || _stateUIControls == null || _stateUIControls.Count == 0)
                    return;

                for (int i = 0; i < sequencers.Count; i++)
                {
                    var module = sequencers[i];
                    for (int j = 0; j < module.states.Count; j++)
                    {
                        var state = module.states[j];
                        if (!_stateUIControls.ContainsKey(state))
                        {
                            Logger.Log("Could not find state UI controls " + state.stateName);
                            continue;
                        }
                            
                        var stateUIControls = _stateUIControls[state];

                        var stateStatusImage = stateUIControls.GetChild("SequencerStateControlsHLG").GetChild("SequencerStateStatusHandle").GetComponent<RawImage>();

                        if (module.currentState.stateID == state.stateID)
                        {
                            stateStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Active");
                        }
                        else
                        {
                            stateStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "icon_groupdraghandle");
                        }
                    }

                    //now loop through sequences
                    for (int j = 0; j < module.sequences.Count; j++)
                    {
                        var sq = module.sequences[j];
                        if (!_sequenceUIControls.ContainsKey(sq))
                        {
                            Logger.Log("Could not find sequence UI controls " + sq.name);
                            continue;
                        }
                            
                        var sequenceUIControls = _sequenceUIControls[sq];

                        var sequenceStatusImage = sequenceUIControls.GetChild("SequenceStatusRawImage").GetComponent<RawImage>();

                        if (sq.isActive)
                        {
                            sequenceStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Active");
                        }
                        else if (sq.isFinished)
                        {
                            sequenceStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Finished");
                        }
                        else
                        {
                            sequenceStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Idle");
                        }

                        if (sq.IsPaused)
                        {
                            sequenceStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Paused");
                        }

                        if (sq.isLocked)
                        {
                            sequenceStatusImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Locked");
                        }
                        
                        var sequenceStartToggle = sequenceUIControls.GetChild("SequenceStartToggle").GetComponent<Toggle>();

                        if (sequenceStartToggle.isOn != sq.isActive)
                        {
                            sequenceStartToggle.isOn = sq.isActive;
                            //sequenceStartToggle.onValueChanged.Invoke (sq.isActive);
                        }
                        sequenceStartToggle.interactable = (sq.startState == module.currentState);

                        sequenceUIControls.GetChild("SequenceStopButton").GetComponent<Button>().interactable = (sq.startState == module.currentState);

                        var sequenceLoopToggle = sequenceUIControls.GetChild("SequenceLoopToggle").GetComponent<Toggle>();
                        if (sq.isLooped != sequenceLoopToggle.isOn)
                        {
                            //sequenceLoopToggle.isOn = sq.isLooped;
                            sequenceLoopToggle.onValueChanged.Invoke(sq.isLooped);
                        }
                            


                    }
                }
            }
        }

        internal void UpdateOpenSequenceControls()
        {
            //now hook in Sequence control buttons
            var footerButtonsZone = _editorWindow.GetChild("WindowContent").GetChild("Panes").GetChild("LeftPane").GetChild("FooterButtonsHLG");
            var sequenceStartToggle = footerButtonsZone.GetChild("SequenceStartToggle").GetComponent<Toggle>();
            sequenceStartToggle.isOn = openSequence.isActive;

            var sequenceLoopToggle = footerButtonsZone.GetChild("SequenceLoopToggle").GetComponent<Toggle>();
            sequenceLoopToggle.isOn = openSequence.isLooped;
        }

        internal void UpdateOpenSequenceCommandProgress()
        {
            //first animate the progressbar
            for (int i = 0; i < openSequence.commands.Count; i++)
            {
                var bc = openSequence.commands[i];

                float progress = 0f;

                if (bc.gotoIndex != -1)
                {
                    progress = (bc.gotoCommandCounter == -1) ? 0f : 1f - ((float) bc.gotoCounter / (float) bc.gotoCommandCounter);
                }
                else if (bc.isFinished)
                {
                    progress = 1f;
                }
                else if (bc.servo != null)
                {
                    if (bc.servo.MaxPosition - bc.servo.MinPosition > 0.01f)
                        progress = 1f - Mathf.Abs(bc.position - bc.servo.Position) / (bc.servo.MaxPosition - bc.servo.MinPosition);
                    else
                        progress = 1f;
                }
                else if (bc.waitTime > 0f)
                {
                    progress = Mathf.Clamp((UnityEngine.Time.time - bc.timeStarted) / bc.waitTime, 0f, 1f);
                }
                
                var commandUIControls = _openSequenceCommandControls[bc];
                if (!commandUIControls)
                    continue;
                
                var commandProgressBarTransform = commandUIControls.GetChild("CommandProgressBar").GetComponent<RectTransform>();
                commandProgressBarTransform.anchorMax = new Vector2(progress, 1f);
                
                var commandStatusRawImage = commandUIControls.GetChild("CommandDragHandle").GetComponent<RawImage>();

                if (bc.isActive)
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Active");
                }
                else if (bc.isFinished)
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Finished");
                }
                else
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "IRWindowIndicator_Idle");
                }
                
            }
        }

        internal void ResetOpenSequenceCommandProgress()
        {
            if (openSequence == null)
                return;

            for (int i = 0; i < openSequence.commands.Count; i++)
            {
                var bc = openSequence.commands[i];

                var commandUIControls = _openSequenceCommandControls[bc];
                if (!commandUIControls)
                    continue;

                var commandProgressBarTransform = commandUIControls.GetChild("CommandProgressBar").GetComponent<RectTransform>();
                commandProgressBarTransform.anchorMax = commandProgressResetAnchor;

                var commandStatusRawImage = commandUIControls.GetChild("CommandDragHandle").GetComponent<RawImage>();
                commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(t => t.name == "icon_dragHandle");
            }
        }

        public void ShowControlWindow()
        {
            RebuildUI();

            _controlWindowFader.FadeTo(_UIAlphaValue, 0.1f, () =>
                {
                    GUIEnabled = true;
                    appLauncherButton.SetTrue(false);
                });
            
        }

        private void OnVesselChange(Vessel v)
        {
            //if the scene was loaded on non-IR Vessel and then IR vessel became focused we might need to re-init the API
            if (!IRWrapper.APIReady)
                try
                {
                    IRWrapper.InitWrapper();
                }
                catch (Exception e)
                {
                    Logger.Log("[Sequencer] Exception while initialising API " + e.Message, Logger.Level.Debug);
                }

            sequencers.Clear();
            openSequence = null;
            CloseAllWindows();
            guiRebuildPending = true;
            
            //find module SequencerStorage and force loading of sequences
            var modules = v.FindPartModulesImplementing<ModuleSequencer>();
            if (modules == null)
            {
                Logger.Log("Could not find any ModuleSequencer module", Logger.Level.Debug);
                return;
            }
            else
            {
                try
                {
                    if (v == FlightGlobals.ActiveVessel && modules.Count > 0)
                    {
                        sequencers = modules;

                    }
                    else
                    {
                        Logger.Log("Could not find ModuleSequencer module to load sequences from", Logger.Level.Debug);
                        return;
                    }
                        
                }
                catch (Exception e)
                {
                    Logger.Log("[IRSequencer] Exception in OnVesselChange: " + e.Message);
                }
            }

            Logger.Log("[IRSequencer] OnVesselChange finished, sequencers count=" + sequencers.Count);
        }

        private void OnVesselWasModified(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel)
            {
                OnVesselChange(v);
            }
        }

        private void OnEditorShipModified(ShipConstruct ship)
        {
            if (!IRWrapper.APIReady)
            {
                IRWrapper.InitWrapper();
            }
            openSequence = null;
            sequencers.Clear();
            guiRebuildPending = true;

            var sequencerParts = ship.Parts.FindAll(p => p.FindModuleImplementing<ModuleSequencer>() != null);

            for (int i = 0; i < sequencerParts.Count; i++)
            {
                var seqModule = sequencerParts[i].FindModuleImplementing<ModuleSequencer>();
                sequencers.Add(seqModule);
                seqModule.loadPending = true;
            }
        }

        private void Awake()
        {
            LoadConfigXml();

            guiControlWindowEditMode = alwaysStartInEditMode;

            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onHideUI.Add(OnHideUI);

            SequencerInstance = this;
            isReady = true;

            sequencers = new List<ModuleSequencer>();
            sequencers.Clear();

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onGUIApplicationLauncherReady.Add(AddAppLauncherButton);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequestedForAppLauncher);

            Logger.Log("[Sequencer] Awake successful, Addon: " + AddonName, Logger.Level.Debug);
        }

        private void OnEditorRestart()
        {
            GUIEnabled = false;
            CloseAllWindows();
            openSequence = null;
            sequencers.Clear();
        }

        void OnGameSceneLoadRequestedForAppLauncher(GameScenes SceneToLoad)
        {
            DestroyAppLauncherButton();
        }

        public void Start()
        {
            try
            {
                IRWrapper.InitWrapper();
            }
            catch (Exception e)
            {
                Logger.Log("[Sequencer] Exception while initialising API " + e.Message, Logger.Level.Debug);
            }

            Logger.Log("[Sequencer] OnStart successful", Logger.Level.Debug);
        }

        private void OnShowUI()
        {
            guiHidden = false;
        }

        private void OnHideUI()
        {
            guiHidden = true;
        }

        private void DestroyAppLauncherButton()
        {
            if (appLauncherButton != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(appLauncherButton);
                appLauncherButton = null;
            }
        }

        private void OnDestroy()
        {
            if (_controlWindow)
            {
                _controlWindow.DestroyGameObjectImmediate();
                _controlWindow = null;
                _controlWindowFader = null;
            }

            if (_editorWindow)
            {
                _editorWindow.DestroyGameObjectImmediate();
                _editorWindow = null;
                _editorWindowFader = null;
            }

            if (_settingsWindow)
            {
                _settingsWindow.DestroyGameObjectImmediate();
                _settingsWindow = null;
            }

            _stateUIControls = null;
            _sequenceUIControls = null;
            openSequence = null;
            _openSequenceCommandControls = null;

            EditorLocker.EditorLock(false);

            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onHideUI.Remove(OnHideUI);

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);

            SequencerGUI.Instance.isReady = false;
            SaveConfigXml();

            GameEvents.onGUIApplicationLauncherReady.Remove(AddAppLauncherButton);

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequestedForAppLauncher);
            DestroyAppLauncherButton();

            //consider unloading textures too in TextureLoader

            Logger.Log("[Sequencer] Destroy successful", Logger.Level.Debug);
        }


        private void InitSettingsWindow()
        {
            if (_settingsWindow != null)
                return;

            _settingsWindow = GameObject.Instantiate(UIAssetsLoader.uiSettingsWindowPrefab);
            _settingsWindow.transform.SetParent(UIMasterController.Instance.appCanvas.transform, false);
            _settingsWindow.GetChild("WindowTitle").AddComponent<PanelDragger>();
            _settingsWindowFader = _settingsWindow.AddComponent<CanvasGroupFader>();

            _settingsWindow.GetComponent<CanvasGroup>().alpha = 0f;

            if (SequencerSettingsWindowPosition == Vector3.zero)
            {
                //get the default position from the prefab
                SequencerSettingsWindowPosition = _settingsWindow.transform.position;
            }
            else
            {
                _settingsWindow.transform.position = ClampWindowPosition(SequencerSettingsWindowPosition);
            }

            var closeButton = _settingsWindow.GetChild("WindowTitle").GetChild("RightWindowButton");
            if (closeButton != null)
            {
                closeButton.GetComponent<Button>().onClick.AddListener(ToggleSettingsWindow);
            }

            var alphaText = _settingsWindow.GetChild("WindowContent").GetChild("UITransparencySliderHLG").GetChild("TransparencyValue").GetComponent<Text>();
            alphaText.text = string.Format("{0:#0.00}", _UIAlphaValue);

            var transparencySlider = _settingsWindow.GetChild("WindowContent").GetChild("UITransparencySliderHLG").GetChild("TransparencySlider");

            if (transparencySlider)
            {
                var sliderControl = transparencySlider.GetComponent<Slider>();
                sliderControl.minValue = UI_MIN_ALPHA;
                sliderControl.maxValue = 1.0f;
                sliderControl.value = _UIAlphaValue;
                sliderControl.onValueChanged.AddListener(v =>
                    {
                        alphaText.text = string.Format("{0:#0.00}", v);
                    });
            }

            var scaleText = _settingsWindow.GetChild("WindowContent").GetChild("UIScaleSliderHLG").GetChild("ScaleValue").GetComponent<Text>();
            scaleText.text = string.Format("{0:#0.00}", _UIScaleValue);

            var scaleSlider = _settingsWindow.GetChild("WindowContent").GetChild("UIScaleSliderHLG").GetChild("ScaleSlider");

            if (scaleSlider)
            {
                var sliderControl = scaleSlider.GetComponent<Slider>();
                sliderControl.minValue = UI_MIN_SCALE;
                sliderControl.maxValue = UI_MAX_SCALE;
                sliderControl.value = _UIScaleValue;
                sliderControl.onValueChanged.AddListener(v =>
                    {
                        scaleText.text = string.Format("{0:#0.00}", v);
                    });
            }

            var startEditModeToggle = _settingsWindow.GetChild ("WindowContent").GetChild ("StartEditModeHLG").GetChild ("StartEditModeToggle").GetComponent<Toggle> ();
            startEditModeToggle.isOn = alwaysStartInEditMode;
            startEditModeToggle.onValueChanged.AddListener (v => alwaysStartInEditMode = v);


            var footerButtons = _settingsWindow.GetChild("WindowFooter").GetChild("WindowFooterButtonsHLG");

            var cancelButton = footerButtons.GetChild("CancelButton").GetComponent<Button>();
            cancelButton.onClick.AddListener(() =>
                {
                    transparencySlider.GetComponent<Slider>().value = _UIAlphaValue;
                    alphaText.text = string.Format("{0:#0.00}", _UIAlphaValue);

                    scaleSlider.GetComponent<Slider>().value = _UIScaleValue;
                    scaleText.text = string.Format("{0:#0.00}", _UIScaleValue);

                    closeButton.GetComponent<Button>().onClick.Invoke();
                });
            cancelButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Revert and close";

            var defaultButton = footerButtons.GetChild("DefaultButton").GetComponent<Button>();
            defaultButton.onClick.AddListener(() =>
                {
                    _UIAlphaValue = 0.8f;
                    _UIScaleValue = 1.0f;

                    transparencySlider.GetComponent<Slider>().value = _UIAlphaValue;
                    alphaText.text = string.Format("{0:#0.00}", _UIAlphaValue);

                    scaleSlider.GetComponent<Slider>().value = _UIScaleValue;
                    scaleText.text = string.Format("{0:#0.00}", _UIScaleValue);

                    SetGlobalAlpha(_UIAlphaValue);
                    SetGlobalScale(_UIScaleValue);
                });
            defaultButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Revert to default settings";

            var applyButton = footerButtons.GetChild("ApplyButton").GetComponent<Button>();
            applyButton.onClick.AddListener(() =>
                {
                    float newAlphaValue = (float)Math.Round(transparencySlider.GetComponent<Slider>().value, 2);
                    float newScaleValue = (float)Math.Round(scaleSlider.GetComponent<Slider>().value, 2);

                    SetGlobalAlpha(newAlphaValue);
                    SetGlobalScale(newScaleValue);
                });
            applyButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Apply settings";

            _settingsWindow.SetActive(false);
        }

        private void SetGlobalAlpha(float newAlpha)
        {
            _UIAlphaValue = Mathf.Clamp(newAlpha, UI_MIN_ALPHA, 1.0f);

            if (_controlWindow)
            {
                _controlWindow.GetComponent<CanvasGroup>().alpha = _UIAlphaValue;
            }
            if (_settingsWindow)
            {
                _settingsWindow.GetComponent<CanvasGroup>().alpha = _UIAlphaValue;

                var alphaText = _settingsWindow.GetChild("WindowContent").GetChild("UITransparencySliderHLG").GetChild("TransparencyValue").GetComponent<Text>();
                alphaText.text = string.Format("{0:#0.##}", _UIAlphaValue);
            }
            if (_editorWindow && openSequence != null)
            {
                _editorWindow.GetComponent<CanvasGroup>().alpha = _UIAlphaValue;
            }
        }

        private void SetGlobalScale(float newScale)
        {
            newScale = Mathf.Clamp(newScale, UI_MIN_SCALE, UI_MAX_SCALE);

            if (_controlWindow)
            {
                _controlWindow.transform.localScale = Vector3.one * newScale;
            }
            if (_editorWindow)
            {
                _editorWindow.transform.localScale = Vector3.one * newScale;
            }
            if (_settingsWindow)
            {
                _settingsWindow.transform.localScale = Vector3.one * newScale;

                var scaleText = _settingsWindow.GetChild("WindowContent").GetChild("UIScaleSliderHLG").GetChild("ScaleValue").GetComponent<Text>();
                scaleText.text = string.Format("{0:#0.##}", newScale);
            }

            _UIScaleValue = newScale;
        }

        public void ToggleSettingsWindow()
        {
            if (_settingsWindow == null || _settingsWindowFader == null)
                InitSettingsWindow();

            if (_settingsWindow.activeSelf)
            {
                //fade the window out and deactivate
                _settingsWindowFader.FadeTo(0, UI_FADE_TIME, () => _settingsWindow.SetActive(false));
            }
            else
            {
                //activate and fade the window in,
                _settingsWindow.SetActive(true);
                _settingsWindowFader.FadeTo(_UIAlphaValue, UI_FADE_TIME);
            }
        }

        private void InitControlWindow(bool startSolid = true)
        {
            _controlWindow = GameObject.Instantiate(UIAssetsLoader.controlWindowPrefab);
            _controlWindow.transform.SetParent(UIMasterController.Instance.appCanvas.transform, false);
            _controlWindow.GetChild("WindowTitle").AddComponent<PanelDragger>();
            _controlWindowFader = _controlWindow.AddComponent<CanvasGroupFader>();

            _controlWindow.AddComponent<EditorLocker>();

            //start invisible to be toggled later
            if (!startSolid)
                _controlWindow.GetComponent<CanvasGroup>().alpha = 0f;

            if (SequencerWindowPosition == Vector3.zero)
            {
                //get the default position from the prefab
                SequencerWindowPosition = _controlWindow.transform.position;
            }
            else
            {
                _controlWindow.transform.position = ClampWindowPosition(SequencerWindowPosition);
            }

            var settingsButton = _controlWindow.GetChild("WindowTitle").GetChild("LeftWindowButton");
            if (settingsButton != null)
            {
                settingsButton.GetComponent<Button>().onClick.AddListener(ToggleSettingsWindow);
                var t = settingsButton.AddComponent<BasicTooltip>();
                t.tooltipText = "Show/hide UI settings";
            }

            var closeButton = _controlWindow.GetChild("WindowTitle").GetChild("RightWindowButton");
            if (closeButton != null)
            {
                closeButton.GetComponent<Button>().onClick.AddListener(CloseAllWindows);
                var t = closeButton.AddComponent<BasicTooltip>();
                t.tooltipText = "Close window";
            }
        }

        private void InitSequencerLinePrefab(GameObject sequencerLinePrefab, ModuleSequencer module)
        {
            var sequencerControls = sequencerLinePrefab.GetChild("SequencerControlsHLG");

            var sequencerNameLabel = sequencerControls.GetChild("SequencerNameText").GetComponent<Text>();
            sequencerNameLabel.text = module.sequencerName;
            sequencerNameLabel.gameObject.SetActive(!guiControlWindowEditMode);

            var sequencerNameInputField = sequencerControls.GetChild("SequencerNameInputField").GetComponent<InputField>();
            sequencerNameInputField.text = module.sequencerName;
            sequencerNameInputField.onEndEdit.AddListener(v => module.sequencerName = sequencerNameLabel.text = v);
            sequencerNameInputField.gameObject.SetActive(guiControlWindowEditMode); //should be only visible in edit mode

            var sequencerEditModeToggle = sequencerControls.GetChild("SequencerEditToggle").GetComponent<Toggle>();
            sequencerEditModeToggle.isOn = guiControlWindowEditMode;
            sequencerEditModeToggle.onValueChanged.AddListener(ToggleControlWindowEditMode);
            sequencerEditModeToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Toggle Edit Mode";

            var sequencerLockToggle = sequencerControls.GetChild("SequencerLockToggle").GetComponent<Toggle>();
            sequencerLockToggle.isOn = module.isLocked;
            sequencerLockToggle.onValueChanged.AddListener(v => module.isLocked = v);
            sequencerLockToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Lock/unlock this sequencer";

            var sequencerAddStateButton = sequencerControls.GetChild("AddStateButton").GetComponent<Button>();
            sequencerAddStateButton.onClick.AddListener(() =>
                {
                    var newState = new SequencerState();
                    newState.stateName = "New State";
                    module.states.Add(newState);
                    //add gui code
                    guiRebuildPending = true;
                });
            sequencerAddStateButton.gameObject.SetActive(guiControlWindowEditMode);

            var statesArea = sequencerLinePrefab.GetChild("SequencerStatesVLG");
            statesArea.AddComponent<StateDropHandler>();

            for (int i = 0; i < module.states.Count; i++)
            {
                var st = module.states[i];

                var stateLine = GameObject.Instantiate(UIAssetsLoader.stateLinePrefab);
                stateLine.transform.SetParent(statesArea.transform, false);

                InitSequencerStatePrefab(stateLine, st, module);

                _stateUIControls.Add(st, stateLine);
            }
        }

        private void InitSequencerStatePrefab(GameObject stateLinePrefab, SequencerState state, ModuleSequencer module)
        {
            var stateControls = stateLinePrefab.GetChild("SequencerStateControlsHLG");

            var stateDragHandleObject = stateControls.GetChild("SequencerStateStatusHandle");
            stateDragHandleObject.AddComponent<BasicTooltip>().tooltipText = "Grab to reorder";

            var dragHandler = stateDragHandleObject.AddComponent<StateDragHandler>();
            dragHandler.mainCanvas = UIMasterController.Instance.appCanvas;
            dragHandler.background = UIAssetsLoader.spriteAssets.Find(a => a.name == "IRWindowGroupFrame_Drag");
            dragHandler.linkedState = state;

            var sequencerStateNameLabel = stateControls.GetChild("SequencerStateNameText").GetComponent<Text>();
            sequencerStateNameLabel.text = state.stateName;
            sequencerStateNameLabel.gameObject.SetActive(!guiControlWindowEditMode); //should be hidden in edit mode

            var sequencerStateNameInputField = stateControls.GetChild("SequencerStateNameInputField").GetComponent<InputField>();
            sequencerStateNameInputField.text = state.stateName;
            sequencerStateNameInputField.onEndEdit.AddListener(v => state.stateName = sequencerStateNameLabel.text = v);
            sequencerStateNameInputField.gameObject.SetActive(guiControlWindowEditMode); //should be only visible in edit mode

            var sequencerStateDeleteButton = stateControls.GetChild("SequencerStateDeleteButton").GetComponent<Button>();
            sequencerStateDeleteButton.onClick.AddListener(() =>
                {
                    //remove all affected sequences
                    module.sequences.RemoveAll(seq => (seq.startState == state || seq.endState == state));
                    module.states.Remove(state);
                    if (module.currentState == state)
                    {
                        //reset to the first state
                        module.currentState = module.states[0];
                    }

                    guiRebuildPending = true;

                });
            sequencerStateDeleteButton.gameObject.SetActive(guiControlWindowEditMode);
            sequencerStateDeleteButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Delete the State.\nSequences will be reassigned to first state.";

            var stateAddSequenceButton = stateControls.GetChild("AddSequenceButton").GetComponent<Button>();
            stateAddSequenceButton.onClick.AddListener(() =>
                {
                    var newSeq = new Sequence();
                    newSeq.startState = state;
                    newSeq.endState = state;
                    module.sequences.Add(newSeq);
                    guiRebuildPending = true;
                });

            var sequencesArea = stateLinePrefab.GetChild("SequencerStateSequencesVLG");
            var dropHandler = sequencesArea.AddComponent<SequenceDropHandler>();
            dropHandler.linkedState = state;

            var stateSequences = module.sequences.FindAll(s => s.startState == state);

            if (stateSequences == null || stateSequences.Count == 0)
                return;

            for (int i = 0; i < stateSequences.Count; i++)
            {
                var sq = stateSequences[i];

                var sequenceLine = GameObject.Instantiate(UIAssetsLoader.sequenceLinePrefab);
                sequenceLine.transform.SetParent(sequencesArea.transform, false);

                InitSequenceLinePrefab(sequenceLine, sq, module);

                _sequenceUIControls.Add(sq, sequenceLine);
            }
        }

        private void InitSequenceLinePrefab(GameObject sequenceLinePrefab, Sequence s, ModuleSequencer module)
        {
            var sequenceStatusHandle = sequenceLinePrefab.GetChild("SequenceStatusRawImage").GetComponent<RawImage>();
            sequenceStatusHandle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Grab to reorder or re-assign.";

            var sequenceDragHandler = sequenceStatusHandle.gameObject.AddComponent<SequenceDragHandler>();
            sequenceDragHandler.mainCanvas = UIMasterController.Instance.appCanvas;
            sequenceDragHandler.background = UIAssetsLoader.spriteAssets.Find(a => a.name == "IRWindowServoFrame_Drag");
            sequenceDragHandler.draggedItem = sequenceLinePrefab;
            sequenceDragHandler.linkedSequence = s;

            var sequenceNameText = sequenceLinePrefab.GetChild("SequenceNameText").GetComponent<Text>();
            sequenceNameText.text = s.name;
            sequenceNameText.gameObject.SetActive(!guiControlWindowEditMode);

            var sequenceNameInputField = sequenceLinePrefab.GetChild("SequenceNameInputField").GetComponent<InputField>();
            sequenceNameInputField.text = s.name;
            sequenceNameInputField.onEndEdit.AddListener(v => s.name = sequenceNameText.text = v);
            sequenceNameInputField.gameObject.SetActive(guiControlWindowEditMode);
            
            var dropdownStateOptions = new List<Dropdown.OptionData>();
            var sequenceEndStateIndex = 0;
            for (int i = 0; i < module.states.Count; i++)
            {
                dropdownStateOptions.Add(new Dropdown.OptionData(module.states[i].stateName));
                if (module.states[i] == s.endState)
                    sequenceEndStateIndex = i;
            }

            var endStateText = sequenceLinePrefab.GetChild("EndStateNameText").GetComponent<Text>();
            endStateText.text = s.endState.stateName;
            endStateText.gameObject.SetActive(!guiControlWindowEditMode);

            var endStateDropdown = sequenceLinePrefab.GetChild("EndStateDropdown").GetComponent<Dropdown>();
            
            var template = endStateDropdown.transform.FindChild("Template");
            var canvas = template.GetComponent<Canvas>();
            if (canvas == null)
                canvas = template.gameObject.AddComponent<Canvas>();
            canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

            endStateDropdown.options = dropdownStateOptions;
            endStateDropdown.value = sequenceEndStateIndex;
            endStateDropdown.onValueChanged.AddListener(v =>
                {
                    var newEndState = module.states.Find(st => st.stateName == endStateDropdown.options[v].text);
                    if (newEndState != null)
                    {
                        s.endState = newEndState;
                        endStateText.text = newEndState.stateName;
                    }
                });
            endStateDropdown.gameObject.SetActive(guiControlWindowEditMode);
            
            var sequenceToggleKeyInputField = sequenceLinePrefab.GetChild("SequenceToggleKey").GetComponent<InputField>();
            sequenceToggleKeyInputField.text = s.keyShortcut;
            sequenceToggleKeyInputField.onEndEdit.AddListener(v => s.keyShortcut = v);
            sequenceToggleKeyInputField.gameObject.SetActive(true); //zodius wants it visible all the time
            sequenceToggleKeyInputField.gameObject.AddComponent<BasicTooltip>().tooltipText = "Keyboard shortcut to\n start/stop the sequence.";

            var sequenceAutoStartToggle = sequenceLinePrefab.GetChild("SequenceAutoStartToggle").GetComponent<Toggle>();
            sequenceAutoStartToggle.isOn = s.autoStart;
            sequenceAutoStartToggle.onValueChanged.AddListener(v => s.autoStart = v);

            var sequenceStartToggle = sequenceLinePrefab.GetChild("SequenceStartToggle").GetComponent<Toggle>();
            sequenceStartToggle.isOn = s.isActive;
            sequenceStartToggle.interactable = !module.isLocked;
            sequenceStartToggle.onValueChanged.AddListener(v =>
                {
                    if (s.isLocked)
                        return;
                    
                    if (v && !s.isActive)
                        s.Start(module.currentState);

                    if (!v && s.isActive)
                        s.Pause();

                });
            sequenceStartToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Start/pause the sequence.";

            var sequenceStopButton = sequenceLinePrefab.GetChild("SequenceStopButton").GetComponent<Button>();
            sequenceStopButton.onClick.AddListener(() =>
                {
                    sequenceStartToggle.onValueChanged.Invoke(false);

                    if (!s.isLocked)
                        s.Reset();

                    if (openSequence != null && openSequence.sequenceID == s.sequenceID)
                        ResetOpenSequenceCommandProgress();
                });
            sequenceStopButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Stop and reset the sequence.";

            var sequenceLoopToggle = sequenceLinePrefab.GetChild("SequenceLoopToggle").GetComponent<Toggle>();
            sequenceLoopToggle.isOn = s.isLooped;
            sequenceLoopToggle.onValueChanged.AddListener(v => s.isLooped = v);
            sequenceLoopToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Loop the sequence";

            var sequenceEditModeToggle = sequenceLinePrefab.GetChild("SequenceEditModeToggle").GetComponent<Toggle>();
            sequenceEditModeToggle.isOn = (openSequence != null && openSequence.sequenceID == s.sequenceID);
            sequenceEditModeToggle.onValueChanged.AddListener(v => ToggleSequenceEditor(s, v, sequenceEditModeToggle));
            sequenceEditModeToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Open/close sequence editor.";

            var sequenceCloneButton = sequenceLinePrefab.GetChild("SequenceCloneButton").GetComponent<Button>();
            sequenceCloneButton.onClick.AddListener(() =>
                {
                    module.sequences.Add(new Sequence(s));
                    guiRebuildPending = true;
                });
            sequenceCloneButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Create a copy (clone).";

            var sequenceDeleteButton = sequenceLinePrefab.GetChild("SequenceDeleteButton").GetComponent<Button>();
            sequenceDeleteButton.onClick.AddListener(() =>
                {
                    s.Pause();
                    s.Reset();
                    if (openSequence == s)
                    {
                        CloseEditorWindow();
                        openSequence = null;
                    }
                    module.sequences.Remove(s);

                    guiRebuildPending = true;
                });
            sequenceDeleteButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Delete the sequence";

        }

        private void ToggleSequenceEditor(Sequence s, bool value, Toggle currentToggle)
        {
            if (value)
            {
                if(openSequence!= null && openSequence.sequenceID != s.sequenceID)
                {
                    //we need to close current window and rebuild it;
                    _editorWindow.DestroyGameObjectImmediate();
                    _editorWindow = null;
                    _editorWindowFader = null;
                }

                var toggles = _controlWindow.GetComponentsInChildren<Toggle>(true);
                for(int i=0; i<toggles.Length; i++)
                {
                    if(toggles[i].name == "SequenceEditModeToggle" && toggles[i] != currentToggle)
                    {
                        toggles[i].isOn = false;
                    }
                }

                openSequence = s;

                //init the window if necessary (most likely it is necessary)
                if (!_editorWindowFader)
                    InitEditorWindow();

                _editorWindowFader.FadeTo(_UIAlphaValue, 0.1f);

            }
            else
            {
                openSequence = null;
                CloseEditorWindow();
            }
        }

        private void ToggleControlWindowEditMode(bool value)
        {
            //toggle edit mode
            guiControlWindowEditMode = value;
            guiRebuildPending = true;
        }

        private void InitEditorWindow(bool startSolid = true)
        {
            _editorWindow = GameObject.Instantiate(UIAssetsLoader.editorWindowPrefab);
            _editorWindow.transform.SetParent(UIMasterController.Instance.appCanvas.transform, false);
            _editorWindow.GetChild("WindowTitle").AddComponent<PanelDragger>();
            _editorWindowFader = _editorWindow.AddComponent<CanvasGroupFader>();

            _editorWindow.AddComponent<EditorLocker>();

            //start invisible to be toggled later
            if (!startSolid)
                _editorWindow.GetComponent<CanvasGroup>().alpha = 0f;

            if (SequencerEditorWindowPosition == Vector3.zero)
            {
                //get the default position from the prefab
                SequencerEditorWindowPosition = _editorWindow.transform.position;
            }
            else
            {
                _editorWindow.transform.position = ClampWindowPosition(SequencerEditorWindowPosition);
            }

            if (SequencerEditorWindowSize == Vector2.zero)
            {
                SequencerEditorWindowSize = _editorWindow.GetComponent<RectTransform>().sizeDelta;
            }
            else
            {
                _editorWindow.GetComponent<RectTransform>().sizeDelta = SequencerEditorWindowSize;
            }

            var settingsButton = _editorWindow.GetChild("WindowTitle").GetChild("LeftWindowButton");
            if (settingsButton != null)
            {
                settingsButton.GetComponent<Button>().onClick.AddListener(ToggleSettingsWindow);
                var t = settingsButton.AddComponent<BasicTooltip>();
                t.tooltipText = "Show/hide UI settings";
            }

            var titleText = _editorWindow.GetChild("WindowTitle").GetComponent<Text>();
            titleText.text = "Editing: " + openSequence.name;

            var closeButton = _editorWindow.GetChild("WindowTitle").GetChild("RightWindowButton");
            if (closeButton != null)
            {
                closeButton.GetComponent<Button>().onClick.AddListener(CloseEditorWindow);
                var t = closeButton.AddComponent<BasicTooltip>();
                t.tooltipText = "Close window";
            }

            var editorFooterButtons = _editorWindow.GetChild("WindowFooter").GetChild("WindowFooterButtonsHLG");
            
            var resizeHandler = editorFooterButtons.GetChild("ResizeHandle").AddComponent<PanelResizer>();
            resizeHandler.rectTransform = _editorWindow.transform as RectTransform;
            resizeHandler.minSize = new Vector2(470, 450);
            resizeHandler.maxSize = new Vector2(2000, 1600);

            var leftPane = _editorWindow.GetChild("WindowContent").GetChild("Panes").GetChild("LeftPane").GetChild("CommandZone");
            var moveServoTemplate = leftPane.GetChild("MoveZoneHLG");

            var moveServoDetails = moveServoTemplate.GetChild("MoveDetails").GetChild("ServoDataVLG");
            moveServoDetails.AddComponent<BasicTooltip>().tooltipText = "Ctrl-Click any servo on your craft to select it\n or use the dropdown menus.";

            var moveToInputField = moveServoDetails.GetChild("MoveToHLG").GetChild("MoveToPositionInputField").GetComponent<InputField>();
            moveToInputField.text = string.Format("{0:#0.00}", moveToValue);
            moveToInputField.onEndEdit.AddListener(v =>
                {
                    float tmp = 0f;

                    if (float.TryParse(v, out tmp))
                    {
                        moveToValue = tmp;
                    }
                });

            var moveAtInputField = moveServoDetails.GetChild("MoveToHLG").GetChild("MoveToSpeedInputField").GetComponent<InputField>();
            moveAtInputField.text = string.Format("{0:#0.0}", moveAtValue);
            moveAtInputField.onEndEdit.AddListener(v =>
                {
                    float tmp = 0f;

                    if (float.TryParse(v, out tmp))
                    {
                        moveAtValue = tmp;
                    }
                });

            var servoGroupsFiltered = new List<IRWrapper.IControlGroup>();

            foreach (IRWrapper.IControlGroup g in IRWrapper.IRController.ServoGroups)
            {
                if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != g.Vessel)
                    continue;
                servoGroupsFiltered.Add(g);
            }

            var servoGroupsDropdownList = new List<Dropdown.OptionData>();
            foreach (IRWrapper.IControlGroup g in servoGroupsFiltered)
            {
                servoGroupsDropdownList.Add(new Dropdown.OptionData(g.Name));
            }

            var servoGroupsDropdown = moveServoDetails.GetChild("GroupDropdown").GetComponent<Dropdown>();
            servoGroupsDropdown.options = servoGroupsDropdownList;
            servoGroupsDropdown.value = selectedGroupIndex;

            var canvas = servoGroupsDropdown.template.gameObject.AddOrGetComponent<Canvas>();
            canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

            var servosDropdownList = new List<Dropdown.OptionData>();
            foreach (var s in servoGroupsFiltered[selectedGroupIndex].Servos)
            {
                servosDropdownList.Add(new Dropdown.OptionData(s.Name));
            }

            var servosDropdown = moveServoDetails.GetChild("ServoDropdown").GetComponent<Dropdown>();
            servosDropdown.options = servosDropdownList;
            servosDropdown.value = selectedServoIndex;

            canvas = servosDropdown.template.gameObject.AddOrGetComponent<Canvas>();
            canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

            var servoHighlighter = servosDropdown.gameObject.AddComponent<ServoHighlighter>();
            servoHighlighter.servo = servoGroupsFiltered[selectedGroupIndex].Servos[selectedServoIndex];

            servosDropdown.onValueChanged.AddListener(v =>
                {
                    selectedServoIndex = v;
                    moveToValue = servoGroupsFiltered[selectedGroupIndex].Servos[selectedServoIndex].Position;
                    moveToInputField.text = string.Format("{0:#0.00}", moveToValue);
                    servoHighlighter.servo = servoGroupsFiltered[selectedGroupIndex].Servos[selectedServoIndex];
                });
            
            servoGroupsDropdown.onValueChanged.AddListener(v =>
                {
                    selectedGroupIndex = v;
                    servosDropdownList.Clear();
                    foreach (var s in servoGroupsFiltered[selectedGroupIndex].Servos)
                    {
                        servosDropdownList.Add(new Dropdown.OptionData(s.Name));
                    }
                    servosDropdown.options = servosDropdownList;
                    servosDropdown.onValueChanged.Invoke(0);
                });
            
            var addMoveCommandButton = moveServoTemplate.GetChild("MoveAddButton").GetComponent<Button>();
            addMoveCommandButton.onClick.AddListener(() =>
                {
                    var servo = servoGroupsFiltered[selectedGroupIndex].Servos[selectedServoIndex];
                
                    var bc = new BasicCommand(servo, moveToValue, moveAtValue);

                    openSequence.commands.Add(bc);

                    guiRebuildPending = true;
                });
            addMoveCommandButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Add a move servo command\n to the end of the list.\nYou can reorder commands\n via drag-n-drop.";

            var AGToggleZone = leftPane.GetChild("AGToggleZoneHLG");
            var AGToggleDropDown = AGToggleZone.GetChild("AGToggleDetails").GetChild("ActionGroupDropdown").GetComponent<Dropdown>();

            //first list all the stock AGs into global vars
            if (actionGroupsOptions == null)
                actionGroupsOptions = new List<Dropdown.OptionData>();
            else
                actionGroupsOptions.Clear();
            
            stockActionGroups = (KSPActionGroup[])Enum.GetValues(typeof(KSPActionGroup));

            foreach (KSPActionGroup a in stockActionGroups)
            {
                if (a == KSPActionGroup.None)
                    continue;

                actionGroupsOptions.Add(new Dropdown.OptionData(a.ToString()));

            }

            AGToggleDropDown.options = actionGroupsOptions;
            canvas = AGToggleDropDown.template.gameObject.AddOrGetComponent<Canvas>();
            canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

            AGToggleDropDown.value = actionGroupsOptions.FindIndex(t => t.text == selectedToggleAG.ToString());
            AGToggleDropDown.onValueChanged.AddListener(v =>
                {
                    //selectedToggleAG = stockActionGroups[v+1]; //because we skipped KSPActionGroup.none
                    selectedToggleAG = stockActionGroups.FirstOrDefault(x => x.ToString() == actionGroupsOptions[v].text);
                });

            var addToggleAGCommandButton = AGToggleZone.GetChild("AGToggleAddButton").GetComponent<Button>();
            addToggleAGCommandButton.onClick.AddListener(() =>
                {
                    var bc = new BasicCommand(selectedToggleAG);
                    openSequence.commands.Add(bc);
                    guiRebuildPending = true;
                });
            
            //now if AGX is installed, list all the groups
            if (ActionGroupsExtendedAPI.Instance != null && ActionGroupsExtendedAPI.Instance.Installed())
            {
                if (HighLogic.LoadedSceneIsFlight)
                {
                    extendedActionGroups = ActionGroupsExtendedAPI.Instance.GetAssignedGroups(FlightGlobals.ActiveVessel);
                }
                else
                {
                    extendedActionGroups = ActionGroupsExtendedAPI.Instance.GetAssignedGroups();
                }

                //first list all the stock AGs into global vars
                if (actionGroupsExtendedOptions == null)
                    actionGroupsExtendedOptions = new List<Dropdown.OptionData>();
                else
                    actionGroupsExtendedOptions.Clear();

                foreach (var pair in extendedActionGroups)
                {
                    actionGroupsExtendedOptions.Add(new Dropdown.OptionData(pair.Value));
                }

                //consider creating a separate box for AGX

                var AGXToggleZone = leftPane.GetChild("AGXToggleZoneHLG");
                var AGXToggleDropDown = AGXToggleZone.GetChild("AGToggleDetails").GetChild("ActionGroupDropdown").GetComponent<Dropdown>();

                AGXToggleDropDown.options = actionGroupsExtendedOptions;
                canvas = AGXToggleDropDown.template.gameObject.AddOrGetComponent<Canvas>();
                canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

                AGXToggleDropDown.value = selectedToggleAGX;
                AGXToggleDropDown.onValueChanged.AddListener(v => selectedToggleAGX = v);

                var addToggleAGXCommandButton = AGXToggleZone.GetChild("AGToggleAddButton").GetComponent<Button>();
                addToggleAGXCommandButton.onClick.AddListener(() =>
                    {
                        var bc = new BasicCommand(selectedToggleAGX);
                        openSequence.commands.Add(bc);
                        guiRebuildPending = true;
                    });

                AGXToggleZone.SetActive(extendedActionGroups.Count > 0);

                var AGXWaitZone = leftPane.GetChild("AGXWaitZoneHLG");
                var AGXWaitDropDown = AGXWaitZone.GetChild("AGWaitDetails").GetChild("ActionGroupDropdown").GetComponent<Dropdown>();

                AGXWaitDropDown.options = actionGroupsExtendedOptions;
                canvas = AGXWaitDropDown.template.gameObject.AddOrGetComponent<Canvas>();
                canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

                AGXWaitDropDown.value = selectedWaitAGX;
                AGXWaitDropDown.onValueChanged.AddListener(v => selectedWaitAGX = v);

                var addWaitAGXCommandButton = AGXWaitZone.GetChild("AGWaitAddButton").GetComponent<Button>();
                addWaitAGXCommandButton.onClick.AddListener(() =>
                    {
                        var bc = new BasicCommand(selectedWaitAGX);
                        openSequence.commands.Add(bc);
                        guiRebuildPending = true;
                    });

                AGXWaitZone.SetActive(extendedActionGroups.Count > 0);
            }
            
            var waitForServosAddButton = leftPane.GetChild("WaitForServoZoneHLG").GetChild("WaitForServoAddButton").GetComponent<Button>();
            waitForServosAddButton.onClick.AddListener(() =>
                {
                    var bc = new BasicCommand(true);
                    openSequence.commands.Add(bc);

                    guiRebuildPending = true;
                });

            var AGWaitZone = leftPane.GetChild("AGWaitZoneHLG");
            var AGWaitDropDown = AGWaitZone.GetChild("AGWaitDetails").GetChild("ActionGroupDropdown").GetComponent<Dropdown>();

            AGWaitDropDown.options = actionGroupsOptions;
            canvas = AGWaitDropDown.template.gameObject.AddOrGetComponent<Canvas>();
            canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

            AGWaitDropDown.value = actionGroupsOptions.FindIndex(t => t.text == selectedWaitAG.ToString());
            AGWaitDropDown.onValueChanged.AddListener(v =>
                {
                    //selectedWaitAG = stockActionGroups[v+1]; //because we skipped KSPActionGroup.none
                    selectedWaitAG = stockActionGroups.FirstOrDefault(x => x.ToString() == actionGroupsOptions[v].text);
                });

            var addWaitAGCommandButton = AGWaitZone.GetChild("AGWaitAddButton").GetComponent<Button>();
            addWaitAGCommandButton.onClick.AddListener(() =>
                {
                    var bc = new BasicCommand(selectedWaitAG);
                    bc.wait = true;

                    openSequence.commands.Add(bc);
                    guiRebuildPending = true;
                });


            var delayZone = leftPane.GetChild("DelayZoneHLG");
            var delayTimeInputField = delayZone.GetChild("DelayDetails").GetChild("DelayZone").GetChild("DelayInputField").GetComponent<InputField>();
            delayTimeInputField.text = string.Format("{0:#0.0}", delayTimeValue);
            delayTimeInputField.onEndEdit.AddListener(v =>
                {
                    float tmp = 0f;

                    if (float.TryParse(v, out tmp))
                    {
                        delayTimeValue = tmp;
                    }
                });

            var addDelayCommandButton = delayZone.GetChild("DelayAddButton").GetComponent<Button>();
            addDelayCommandButton.onClick.AddListener(() =>
                {
                    var bc = new BasicCommand(true, delayTimeValue);

                    openSequence.commands.Add(bc);

                    guiRebuildPending = true;
                });

            var repeatZone = leftPane.GetChild("RepeatZoneHLG");
            var repeatDetails = repeatZone.GetChild("RepeatDetails").GetChild("RepeatDataVLG");

            var gotoIndexInputField = repeatDetails.GetChild("GotoHLG").GetChild("GotoIndexInputField").GetComponent<InputField>();
            gotoIndexInputField.text = string.Format("{0:#0}", repeatLineIndexValue);
            gotoIndexInputField.onEndEdit.AddListener(v =>
                {
                    int tmp;
                    if (int.TryParse(v, out tmp))
                    {
                        repeatLineIndexValue = tmp;
                    }
                });

            var repeatInputField = repeatDetails.GetChild("RepeatHLG").GetChild("RepeatInputField").GetComponent<InputField>();
            repeatInputField.text = string.Format("{0:#0}", repeatTimesValue);
            repeatInputField.onEndEdit.AddListener(v =>
                {
                    int tmp;
                    if (int.TryParse(v, out tmp))
                    {
                        repeatTimesValue = tmp;
                    }
                });

            var addRepeatButton = repeatZone.GetChild("RepeatAddButton").GetComponent<Button>();
            addRepeatButton.onClick.AddListener(() =>
                {
                    var bc = new BasicCommand(repeatLineIndexValue, repeatTimesValue);

                    openSequence.commands.Add(bc);

                    guiRebuildPending = true;
                });

            //now hook in Sequence control buttons
            var footerButtonsZone = _editorWindow.GetChild("WindowContent").GetChild("Panes").GetChild("LeftPane").GetChild("FooterButtonsHLG");
            var sequenceStartToggle = footerButtonsZone.GetChild("SequenceStartToggle").GetComponent<Toggle>();
            sequenceStartToggle.isOn = openSequence.isActive;
            sequenceStartToggle.onValueChanged.AddListener(v =>
                {
                    if (openSequence.isLocked)
                        return;
                    var module = sequencers.Find(s => s.sequences.Contains(openSequence));
                    if (v && !openSequence.isActive)
                        openSequence.Start(module.currentState);

                    if (!v && openSequence.isActive)
                        openSequence.Pause();
                });
            sequenceStartToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Start/pause the sequence";

            var sequenceStopButton = footerButtonsZone.GetChild("SequenceStopButton").GetComponent<Button>();
            sequenceStopButton.onClick.AddListener(() =>
                {
                    sequenceStartToggle.onValueChanged.Invoke(false);
                    openSequence.Reset();
                    ResetOpenSequenceCommandProgress();
                });
            sequenceStopButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Stop and reset the sequence";

            var sequenceStepButton = footerButtonsZone.GetChild("SequenceStepButton").GetComponent<Button>();
            sequenceStepButton.onClick.AddListener(() =>
                {
                    openSequence.Step();
                });
            sequenceStepButton.gameObject.AddComponent<BasicTooltip>().tooltipText = "Advance sequence by 1 command";

            var sequenceLoopToggle = footerButtonsZone.GetChild("SequenceLoopToggle").GetComponent<Toggle>();
            sequenceLoopToggle.isOn = openSequence.isLooped;
            sequenceLoopToggle.onValueChanged.AddListener(v => openSequence.isLooped = v);
            sequenceLoopToggle.gameObject.AddComponent<BasicTooltip>().tooltipText = "Loop the sequence";

            if (_openSequenceCommandControls == null)
                _openSequenceCommandControls = new Dictionary<BasicCommand, GameObject>();
            else
                _openSequenceCommandControls.Clear();

            //now we can display commands on the right pane
            var commandsArea = _editorWindow.GetChild("WindowContent").GetChild("Panes").GetChild("RightPane").GetChild("Viewport").GetChild("Content").GetChild("CommandsVLG");
            commandsArea.AddComponent<CommandDropHandler>();

            for (int i = 0; i < openSequence.commands.Count; i++)
            {
                var bc = openSequence.commands[i];

                var commandLine = GameObject.Instantiate(UIAssetsLoader.sequenceCommandLinePrefab);
                commandLine.transform.SetParent(commandsArea.transform, false);

                InitCommandLine(commandLine, bc);

                _openSequenceCommandControls.Add(bc, commandLine);
            }

            SetGlobalScale(_UIScaleValue);

        }

        private void InitCommandLine(GameObject commandLinePrefab, BasicCommand bc)
        {
            bool isServoCommand = (bc.servo != null);
            bool isToggleAGCommand = (bc.ag != KSPActionGroup.None && bc.wait == false);
            bool isWaitAGCommand = (bc.ag != KSPActionGroup.None && bc.wait);
            bool isDelayCommand = bc.wait && (bc.waitTime > 0f);
            bool isRepeatCommand = (bc.gotoIndex != -1);
            bool isWaitForServosCommands = bc.wait && !isWaitAGCommand && !isDelayCommand && !isRepeatCommand;


            var backgroundImage = commandLinePrefab.GetComponent<Image>();
            var progressBarImage = commandLinePrefab.GetChild("CommandProgressBar").GetComponent<Image>();

            if (isServoCommand || isToggleAGCommand)
                backgroundImage.sprite = progressBarImage.sprite = UIAssetsLoader.spriteAssets.Find(i => i.name == "IRWindowButtonGreen");
            else if (isRepeatCommand)
                backgroundImage.sprite = progressBarImage.sprite = UIAssetsLoader.spriteAssets.Find(i => i.name == "IRWindowButtonYellow");
            else
                backgroundImage.sprite = progressBarImage.sprite = UIAssetsLoader.spriteAssets.Find(i => i.name == "IRWindowButtonRed");

            var commandDragHandle = commandLinePrefab.GetChild("CommandDragHandle");
            var commandStatusRawImage = commandDragHandle.GetComponent<RawImage>();

            if (openSequence.isActive || openSequence.IsPaused) //we only need to display statuses when we are in play/pause/step
            {
                if (bc.isActive)
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(i => i.name == "IRWindowIndicator_Active");
                }
                else if (bc.isFinished)
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(i => i.name == "IRWindowIndicator_Finished");
                }
                else
                {
                    commandStatusRawImage.texture = UIAssetsLoader.iconAssets.Find(i => i.name == "IRWindowIndicator_Idle");
                }
            }

            commandDragHandle.AddComponent<BasicTooltip>().tooltipText = "Grab to reposition the command in the list.";

            var dragHandler = commandDragHandle.AddComponent<CommandDragHandler>();
            dragHandler.mainCanvas = UIMasterController.Instance.appCanvas;
            dragHandler.draggedItem = commandLinePrefab;
            dragHandler.linkedCommand = bc;

            var commandLineNumberText = commandLinePrefab.GetChild("CommandNumberLabel").GetComponent<Text>();
            commandLineNumberText.text = string.Format("{0:#0}", openSequence.commands.FindIndex(c => c == bc));

            var commandText = commandLinePrefab.GetChild("CommandTextLabel").GetComponent<Text>();

            if (isServoCommand)
            {
                commandText.text = "Move";
            }
            else if (isToggleAGCommand)
            {
                commandText.text = "Toggle";
            }
            else if (isWaitAGCommand)
            {
                commandText.text = "Wait for";
            }
            else if (isWaitForServosCommands)
            {
                commandText.text = "Wait for commands";
                commandText.gameObject.GetComponent<LayoutElement>().minWidth = 100;
            }
            else if (isDelayCommand)
            {
                commandText.text = "Delay for";
            }
            else if (isRepeatCommand)
            {
                commandText.text = "Goto line#";
            }
            else
                commandText.text = "Unknown command";

            commandText.gameObject.SetActive(true);
            
            var servoDropdown = commandLinePrefab.GetChild("ServoDropdown").GetComponent<Dropdown>();

            if (isServoCommand)
            {
                var allServos = new List<IRWrapper.IServo>();
                var servosDropdownList = new List<Dropdown.OptionData>();
                foreach (IRWrapper.IControlGroup g in IRWrapper.IRController.ServoGroups)
                {
                    if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != g.Vessel)
                        continue;
                    allServos.AddRange(g.Servos);
                }

                int commandServoIndex = -1;
                for (int i = 0; i < allServos.Count; i++)
                {
                    var s = allServos[i];
                    servosDropdownList.Add(new Dropdown.OptionData(s.Name));
                    if (bc.servo.UID == s.UID)
                        commandServoIndex = i;
                }

                servoDropdown.options = servosDropdownList;
                var canvas = servoDropdown.template.gameObject.AddOrGetComponent<Canvas>();
                canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

                var servoHighlighter = servoDropdown.gameObject.AddComponent<ServoHighlighter>();
                servoHighlighter.servo = bc.servo;

                servoDropdown.value = commandServoIndex;
                servoDropdown.onValueChanged.AddListener(v =>
                    {
                        bc.servo = allServos[v];
                        servoHighlighter.servo = bc.servo;
                    });
            }
            servoDropdown.gameObject.SetActive(isServoCommand);

            var moveToInputField = commandLinePrefab.GetChild("MoveToPositionInputField").GetComponent<InputField>();
            moveToInputField.text = string.Format("{0:#0.00}", bc.position);
            moveToInputField.onEndEdit.AddListener(v =>
                {
                    float tmp;
                    if (float.TryParse(v, out tmp))
                    {
                        bc.position = tmp;
                    }
                });
            moveToInputField.gameObject.SetActive(isServoCommand);
            commandLinePrefab.GetChild("MoveToLabel").SetActive(isServoCommand);
            commandLinePrefab.GetChild("MoveSpeedLabel").SetActive(isServoCommand);

            var moveAtInputField = commandLinePrefab.GetChild("MoveToSpeedInputField").GetComponent<InputField>();
            moveAtInputField.text = string.Format("{0:#0.00}", bc.speedMultiplier);
            moveAtInputField.onEndEdit.AddListener(v =>
                {
                    float tmp;
                    if (float.TryParse(v, out tmp))
                    {
                        bc.speedMultiplier = tmp;
                    }
                });
            moveAtInputField.gameObject.SetActive(isServoCommand);

            if (isToggleAGCommand || isWaitAGCommand)
            {
                var AGDropdown = commandLinePrefab.GetChild("ActionGroupDropdown").GetComponent<Dropdown>();

                AGDropdown.options = actionGroupsOptions;
                var canvas = AGDropdown.template.gameObject.AddOrGetComponent<Canvas>();
                canvas.sortingLayerID = UIMasterController.Instance.appCanvas.sortingLayerID;

                AGDropdown.value = actionGroupsOptions.FindIndex(t => t.text == bc.ag.ToString());
                AGDropdown.onValueChanged.AddListener(v =>
                    {
                        bc.ag = stockActionGroups.FirstOrDefault(x => x.ToString() == actionGroupsOptions[v].text);
                    });
                AGDropdown.gameObject.SetActive(isToggleAGCommand || isWaitAGCommand);
            }

            if (isDelayCommand)
            {
                var delayInputField = commandLinePrefab.GetChild("CommandDelayInputField").GetComponent<InputField>();
                delayInputField.text = string.Format("{0:#0.0}", bc.waitTime);
                delayInputField.onEndEdit.AddListener(v =>
                    {
                        float tmp;
                        if (float.TryParse(v, out tmp))
                        {
                            bc.waitTime = tmp;
                        }
                    });
                delayInputField.gameObject.SetActive(isDelayCommand);
                commandLinePrefab.GetChild("CommandDelayLabel").SetActive(isDelayCommand);
            }

            if (isRepeatCommand)
            {
                var placeholder = new GameObject();
                placeholder.name = "RepeatCommandPlaceholder";
                placeholder.transform.SetParent(commandLinePrefab.transform.parent, false);
                placeholder.transform.SetSiblingIndex(bc.gotoIndex);
                placeholder.AddComponent<BasicTooltip>().tooltipText = "GoTo command indicator.";
                var rt = placeholder.AddComponent<RectTransform>();
                rt.pivot = Vector2.zero;

                var placeholderBG = placeholder.AddComponent<Image>();
                placeholderBG.sprite = UIAssetsLoader.spriteAssets.Find(i => i.name == "IRWindowButtonYellow");
                placeholderBG.type = Image.Type.Sliced;
                placeholderBG.fillCenter = true;

                var le = placeholder.AddComponent<LayoutElement>();
                le.preferredHeight = 6;
                le.flexibleWidth = 1;

                var gotoIndexInputField = commandLinePrefab.GetChild("CommandGotoIndexInputField").GetComponent<InputField>();
                gotoIndexInputField.text = string.Format("{0:#0}", bc.gotoIndex);
                gotoIndexInputField.onEndEdit.AddListener(v =>
                    {
                        int tmp;
                        if (int.TryParse(v, out tmp))
                        {
                            bc.gotoIndex = tmp;
                        }

                        if(placeholder)
                            placeholder.transform.SetSiblingIndex(bc.gotoIndex);
                    });

                var repeatInputField = commandLinePrefab.GetChild("CommandRepeatInputField").GetComponent<InputField>();
                repeatInputField.text = string.Format("{0:#0}", bc.gotoCommandCounter);
                repeatInputField.onEndEdit.AddListener(v =>
                    {
                        int tmp;
                        if (int.TryParse(v, out tmp))
                        {
                            bc.gotoCommandCounter = tmp;
                        }
                    });

                gotoIndexInputField.gameObject.SetActive(isRepeatCommand);
                commandLinePrefab.GetChild("CommandRepeatLabel").SetActive(isRepeatCommand);
                repeatInputField.gameObject.SetActive(isRepeatCommand);
                commandLinePrefab.GetChild("CommandRepeatLabel2").SetActive(isRepeatCommand);
            }

            var commandDeleteButton = commandLinePrefab.GetChild("CommandDeleteButton").GetComponent<Button>();
            commandDeleteButton.onClick.AddListener(() =>
                {
                    openSequence.Pause();
                    openSequence.commands.Remove(bc);
                    openSequence.Reset(); 
                    guiRebuildPending = true;
                });
        }

        public void RebuildUI()
        {
            if (_controlWindow)
            {
                SequencerWindowPosition = _controlWindow.transform.position;
                _controlWindow.DestroyGameObjectImmediate();
                _controlWindow = null;
            }

            if (_editorWindow)
            {
                SequencerEditorWindowPosition = _editorWindow.transform.position;
                SequencerEditorWindowSize = _editorWindow.GetComponent<RectTransform>().sizeDelta;
                _editorWindow.DestroyGameObjectImmediate();
                _editorWindow = null;
            }

            if (_settingsWindow)
                SequencerSettingsWindowPosition = _settingsWindow.transform.position;
            //should be called by ServoController when required (Vessel changed and such).

            if (_stateUIControls == null)
                _stateUIControls = new Dictionary<SequencerState, GameObject>();
            else
                _stateUIControls.Clear();

            if (_sequenceUIControls == null)
                _sequenceUIControls = new Dictionary<Sequence, GameObject>();
            else
                _sequenceUIControls.Clear();

            if (!UIAssetsLoader.allPrefabsReady)
            {
                GUIEnabled = false;
                return;
            }

            if (_settingsWindow == null)
            {
                InitSettingsWindow();
            }

            //here we need to wait until prefabs become available and then Instatiate the window
            if (_controlWindow == null)
            {
                InitControlWindow(GUIEnabled);
            }

            if (_controlWindow)
            {
                var sequencersArea = _controlWindow.GetChild("WindowContent");
                for (int i = 0; i < sequencers.Count; i++)
                {
                    var sequencerLine = GameObject.Instantiate(UIAssetsLoader.sequencerLinePrefab);
                    sequencerLine.transform.SetParent(sequencersArea.transform, false);

                    InitSequencerLinePrefab(sequencerLine, sequencers[i]);
                }
            }

            if (UIAssetsLoader.allPrefabsReady && _editorWindow == null && openSequence != null)
            {
                InitEditorWindow(GUIEnabled);
            }

            //we don't need to set global alpha as all the windows will be faded it to the setting
            SetGlobalScale(_UIScaleValue);
            guiRebuildPending = false;

        }

        private void CloseAllWindows()
        {
            CloseEditorWindow();

            if (_controlWindowFader)
                _controlWindowFader.FadeTo(0f, 0.1f, () =>
                    {
                        GUIEnabled = false;
                        appLauncherButton.SetFalse(false);
                        SequencerWindowPosition = _controlWindow.transform.position;
                        _controlWindow.DestroyGameObjectImmediate();
                        _controlWindow = null;
                        _controlWindowFader = null;
                    });

            if (_settingsWindowFader)
            {
                if (_settingsWindow.activeSelf)
                    _settingsWindowFader.FadeTo(0f, 0.1f, () =>
                        {
                            SequencerSettingsWindowPosition = _settingsWindow.transform.position;
                            _settingsWindow.DestroyGameObjectImmediate();
                            _settingsWindow = null;
                            _settingsWindowFader = null;
                        });
                else
                {
                    SequencerSettingsWindowPosition = _settingsWindow.transform.position;
                    _settingsWindow.DestroyGameObjectImmediate();
                    _settingsWindow = null;
                    _settingsWindowFader = null;
                }
            }
            
            EditorLocker.EditorLock(false);
        }

        private void CloseEditorWindow()
        {
            if (_editorWindowFader)
            {
                _editorWindowFader.FadeTo(0f, 0.1f, () =>
                    {
                        SequencerEditorWindowPosition = _editorWindow.transform.position;
                        SequencerEditorWindowSize = _editorWindow.GetComponent<RectTransform>().sizeDelta;

                        openSequence = null;
                        _editorWindow.DestroyGameObjectImmediate();
                        _editorWindow = null;
                        _editorWindowFader = null;
                    });

                if (!_controlWindow)
                    return;
                
                var toggles = _controlWindow.GetComponentsInChildren<Toggle>(true);
                for(int i=0; i<toggles.Length; i++)
                {
                    if(toggles[i].name == "SequenceEditModeToggle")
                    {
                        toggles[i].isOn = false;
                    }
                }

            }
                
        }

        internal void KeyboardLock(Boolean apply)
        {
            //only do this lock in the editor - no point elsewhere
            if (apply)
            {
                //only add a new lock if there isnt already one there
                if (InputLockManager.GetControlLock("IRKeyboardLock") != ControlTypes.KEYBOARDINPUT)
                {
                    Logger.Log(String.Format("[GUI] AddingLock-{0}", "IRKeyboardLock"), Logger.Level.Debug);

                    InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, "IRKeyboardLock");
                }
            }
            //Otherwise make sure the lock is removed
            else
            {
                //Only try and remove it if there was one there in the first place
                if (InputLockManager.GetControlLock("IRKeyboardLock") == ControlTypes.KEYBOARDINPUT)
                {
                    Logger.Log(String.Format("[GUI] Removing-{0}", "IRKeyboardLock"), Logger.Level.Debug);
                    InputLockManager.RemoveControlLock("IRKeyboardLock");
                }
            }

            isKeyboardLocked = apply;
        }

        public void LoadConfigXml()
        {
            KSP.IO.PluginConfiguration config = KSP.IO.PluginConfiguration.CreateForType<SequencerGUI>();
            config.load();

            SequencerWindowPosition = config.GetValue<Vector3>("SequencerWindowPosition");
            SequencerEditorWindowPosition = config.GetValue<Vector3>("SequencerEditorWindowPosition");
            SequencerEditorWindowSize = config.GetValue<Vector2>("SequencerEditorWindowSize");
            SequencerSettingsWindowPosition = config.GetValue<Vector3>("SequencerSettingsWindowPosition");
            alwaysStartInEditMode = config.GetValue<bool>("alwaysStartInEditMode");

            _UIAlphaValue = (float)config.GetValue<double>("UIAlphaValue", 0.8);
            _UIScaleValue = (float)config.GetValue<double>("UIScaleValue", 1.0);
            
        }

        public void SaveConfigXml()
        {
            if (_controlWindow)
                SequencerWindowPosition = _controlWindow.transform.position;

            if (_editorWindow)
            {
                SequencerEditorWindowPosition = _editorWindow.transform.position;
                SequencerEditorWindowSize = _editorWindow.GetComponent<RectTransform>().sizeDelta;
            }
            if (_settingsWindow)
            {
                SequencerSettingsWindowPosition = _settingsWindow.transform.position;
            }

            KSP.IO.PluginConfiguration config = KSP.IO.PluginConfiguration.CreateForType<SequencerGUI>();
            config.SetValue("SequencerWindowPosition", SequencerWindowPosition);
            config.SetValue("SequencerEditorWindowPosition", SequencerEditorWindowPosition);
            config.SetValue("SequencerEditorWindowSize", SequencerEditorWindowSize);
            config.SetValue("SequencerSettingsWindowPosition", SequencerSettingsWindowPosition);
            config.SetValue("UIAlphaValue", (double)_UIAlphaValue);
            config.SetValue("UIScaleValue", (double)_UIScaleValue);
            config.SetValue("alwaysStartInEditMode", (bool)alwaysStartInEditMode);
            config.save();
        }

        
        
    }

    
}
