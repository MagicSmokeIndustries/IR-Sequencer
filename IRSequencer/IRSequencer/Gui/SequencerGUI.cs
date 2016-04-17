using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using KSP.IO;
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

        public bool SequencerReady {get { return isReady;}}

        public static SequencerGUI Instance
        {
            get { return SequencerInstance; }
        }

        internal List<ModuleSequencer> sequencers;

        internal Sequence openSequence;
        internal SequencerState selectedState = null;
        internal Sequence selectedSequence = null;
        internal ModuleSequencer selectedSequencer = null;
        private List<BasicCommand> availableServoCommands;

        private Dictionary<ModuleSequencer, GameObject> _modulesUIControls;
        private Dictionary<SequencerState, GameObject> _statesUIControls;
        private Dictionary<Sequence, GameObject> _sequencesUIControls;
        
        
        private void AddAppLauncherButton()
        {
            if (appLauncherButton == null && ApplicationLauncher.Ready && ApplicationLauncher.Instance != null)
            {
                try
                {
                    var texture = UIAssetsLoader.iconAssets.Find(t => t.name =="icon_seq_button");

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
                    DestroyAppLauncherButton ();
                    appLauncherButton = null;
                }
            }
        }

        public void Update()
        {
            if(firstUpdate)
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
                if (guiRebuildPending && GUIEnabled)
                    RebuildUI();
                
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
                        return;
                    }
                }
                else if (HighLogic.LoadedSceneIsEditor)
                {
                    if (EditorLogic.fetch != null)
                    {
                        var s = EditorLogic.fetch.ship;
                        if (s != null)
                        {
                            var modulePart = s.Parts.Find(p => p.FindModuleImplementing<ModuleSequencer>() != null);
                            if (GUIEnabled && modulePart == null)
                            {
                                ScreenMessages.PostScreenMessage("Sequencer module is required (add probe core).", 3, ScreenMessageStyle.UPPER_CENTER);
                                GUIEnabled = false;
                                return;
                            }
                        }
                    }
                }

            }
        }


        public void ShowControlWindow()
        {
            RebuildUI();

            _controlWindowFader.FadeTo(_UIAlphaValue, 0.1f, () => { GUIEnabled = true; appLauncherButton.SetTrue(false); });
            
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
            availableServoCommands = null;
            openSequence = null;

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
                    if  (v == FlightGlobals.ActiveVessel && modules.Count > 0)
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
            if(!IRWrapper.APIReady)
            {
                IRWrapper.InitWrapper();
            }
            availableServoCommands = null;
            openSequence = null;
            sequencers.Clear ();

            var sequencerParts = ship.Parts.FindAll(p => p.FindModuleImplementing<ModuleSequencer>() != null);

            for (int i=0; i < sequencerParts.Count; i++)
            {
                var seqModule = sequencerParts[i].FindModuleImplementing<ModuleSequencer>();
                sequencers.Add(seqModule);
                seqModule.loadPending = true;
            }

            guiRebuildPending = true;
        }

        private void Awake()
        {
            LoadConfigXml();

            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onHideUI.Add(OnHideUI);

            SequencerInstance = this;
            isReady = true;

            sequencers = new List<ModuleSequencer> ();
            sequencers.Clear ();

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onGUIApplicationLauncherReady.Add (AddAppLauncherButton);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequestedForAppLauncher);

            Logger.Log("[Sequencer] Awake successful, Addon: " + AddonName, Logger.Level.Debug);
        }

        private void OnEditorRestart()
        {
            GUIEnabled = false;
            availableServoCommands = null;
            openSequence = null;
            sequencers.Clear ();
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
            if(_controlWindow)
            {
                _controlWindow.DestroyGameObjectImmediate();
                _controlWindow = null;
                _controlWindowFader = null;
            }

            if(_editorWindow)
            {
                _editorWindow.DestroyGameObjectImmediate();
                _editorWindow = null;
                _editorWindowFader = null;
            }

            if(_settingsWindow)
            {
                _settingsWindow.DestroyGameObjectImmediate();
                _settingsWindow = null;
            }

            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onHideUI.Remove(OnHideUI);

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);

            SequencerGUI.Instance.isReady = false;
            SaveConfigXml();

            GameEvents.onGUIApplicationLauncherReady.Remove (AddAppLauncherButton);

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
                sliderControl.onValueChanged.AddListener(v => { alphaText.text = string.Format("{0:#0.00}", v); });
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
                sliderControl.onValueChanged.AddListener(v => { scaleText.text = string.Format("{0:#0.00}", v); });
            }
            
            var footerButtons = _settingsWindow.GetChild("WindowFooter").GetChild("WindowFooterButtonsHLG");

            var cancelButton = footerButtons.GetChild("CancelButton").GetComponent<Button>();
            cancelButton.onClick.AddListener(() =>
            {
                transparencySlider.GetComponent<Slider>().value = _UIAlphaValue;
                alphaText.text = string.Format("{0:#0.00}", _UIAlphaValue);

                scaleSlider.GetComponent<Slider>().value = _UIScaleValue;
                scaleText.text = string.Format("{0:#0.00}", _UIScaleValue);
            });

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

            var applyButton = footerButtons.GetChild("ApplyButton").GetComponent<Button>();
            applyButton.onClick.AddListener(() =>
            {
                float newAlphaValue = (float) Math.Round(transparencySlider.GetComponent<Slider>().value, 2);
                float newScaleValue = (float) Math.Round(scaleSlider.GetComponent<Slider>().value, 2);

                SetGlobalAlpha(newAlphaValue);
                SetGlobalScale(newScaleValue);
            });
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
                return;

            //lets simplify things
            if (_settingsWindowFader.IsFading)
                return;

            if (_settingsWindow.activeInHierarchy)
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
                _controlWindow.transform.position = SequencerWindowPosition;
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

            var sequencerEditModeToggle = sequencerControls.GetChild("SequencerEditToggle").GetComponent<Toggle>();
            sequencerEditModeToggle.isOn = guiControlWindowEditMode;
            sequencerEditModeToggle.onValueChanged.AddListener(ToggleControlWindowEditMode);

            var sequencerLockToggle = sequencerControls.GetChild("SequencerLockToggle").GetComponent<Toggle>();
            sequencerLockToggle.isOn = module.isLocked;
            sequencerLockToggle.onValueChanged.AddListener(v => module.isLocked = v);

            var sequencerAddStateButton = sequencerControls.GetChild("AddStateButton").GetComponent<Button>();
            sequencerAddStateButton.onClick.AddListener(() =>
                {
                    var newState = new SequencerState();
                    newState.stateName = "New State";
                    module.states.Add(newState);
                    //add gui code
                    guiRebuildPending = true;
                });
            //there will be more fields here eventually

            var statesArea = sequencerLinePrefab.GetChild("SequencerStatesVLG");

            for(int i=0; i<module.states.Count; i++)
            {
                var st = module.states[i];

                var stateLine = GameObject.Instantiate(UIAssetsLoader.stateLinePrefab);
                stateLine.transform.SetParent(statesArea.transform, false);

                InitSequencerStatePrefab(stateLine, st, module);
            }
        }

        private void InitSequencerStatePrefab(GameObject stateLinePrefab, SequencerState state, ModuleSequencer module)
        {
            var stateControls = stateLinePrefab.GetChild("SequencerStateControlsHLG");

            var stateDragHandle = stateControls.GetChild("SequencerStateStatusHandle");
            //add DragHandler component here and change icon accordingly

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

            var stateSequences = module.sequences.FindAll(s => s.startState == state);

            if (stateSequences == null || stateSequences.Count == 0)
                return;

            for(int i=0; i<stateSequences.Count; i++)
            {
                var sq = stateSequences[i];

                var sequenceLine = GameObject.Instantiate(UIAssetsLoader.sequenceLinePrefab);
                sequenceLine.transform.SetParent(sequencesArea.transform, false);

                InitSequenceLinePrefab(sequenceLine, sq, module);

            }
        }

        private void InitSequenceLinePrefab(GameObject sequenceLinePrefab, Sequence s, ModuleSequencer module)
        {
            var sequenceStatusHandle = sequenceLinePrefab.GetChild("SequenceStatusRawImage").GetComponent<RawImage>();
            var sequenceDragHandler = sequenceStatusHandle.gameObject.AddComponent<SequenceDragHandler>();
            sequenceDragHandler.mainCanvas = UIMasterController.Instance.appCanvas;
            sequenceDragHandler.background = UIAssetsLoader.spriteAssets.Find(a => a.name == "IRWindowGroupFrame_Drag");

            var sequenceNameInputField = sequenceLinePrefab.GetChild("SequenceNameInputField").GetComponent<InputField>();
            sequenceNameInputField.text = s.name;
            sequenceNameInputField.onEndEdit.AddListener(v => s.name = v);

            var sequenceToggleKeyInputField = sequenceLinePrefab.GetChild("SequenceToggleKey").GetComponent<InputField>();
            sequenceToggleKeyInputField.text = s.keyShortcut;
            sequenceToggleKeyInputField.onEndEdit.AddListener(v => s.keyShortcut = v);

            var sequenceAutoStartToggle = sequenceLinePrefab.GetChild("SequenceAutoStartToggle").GetComponent<Toggle>();
            sequenceAutoStartToggle.isOn = s.autoStart;
            sequenceAutoStartToggle.onValueChanged.AddListener(v => s.autoStart = v);

            var sequenceEditModeToggle = sequenceLinePrefab.GetChild("SequenceEditModeToggle").GetComponent<Toggle>();
            sequenceEditModeToggle.isOn = (selectedSequence != null);
            sequenceEditModeToggle.onValueChanged.AddListener(v => ToggleSequenceEditor(s, v));

            var dropdownStateOptions = new List<Dropdown.OptionData>();
            var sequenceEndStateIndex = 0;
            for(int i=0; i< module.states.Count; i++)
            {
                dropdownStateOptions.Add(new Dropdown.OptionData(module.states[i].stateName));
                if (module.states[i] == s.endState)
                    sequenceEndStateIndex = i;
            }
            
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
                    if(newEndState!= null)
                    {
                        s.endState = newEndState;
                    }
                });

            var sequenceStartToggle = sequenceLinePrefab.GetChild("SequenceStartToggle").GetComponent<Toggle>();
            sequenceStartToggle.isOn = s.isActive;
            sequenceStartToggle.interactable = !module.isLocked;
            sequenceStartToggle.onValueChanged.AddListener(v =>
                {
                    if (v && !s.isLocked)
                    {
                        if (v != s.isActive)
                        {
                            s.Start(module.currentState);
                        }
                    }
                    else if (!s.isLocked)
                    {
                        if (v != s.isActive && !s.isFinished)
                        {
                            s.Pause();
                        }
                    }

                });

            var sequenceStopButton = sequenceLinePrefab.GetChild("SequenceStopButton").GetComponent<Button>();
            sequenceStopButton.onClick.AddListener(() =>
                {
                    if (!s.isLocked)
                        s.Reset();
                });

            var sequenceLoopToggle = sequenceLinePrefab.GetChild("SequenceLoopToggle").GetComponent<Toggle>();
            sequenceLoopToggle.isOn = s.isLooped;
            sequenceLoopToggle.onValueChanged.AddListener(v => s.isLooped = v);

            var sequenceCloneButton = sequenceLinePrefab.GetChild("SequenceCloneButton").GetComponent<Button>();
            sequenceCloneButton.onClick.AddListener(() =>
            {
                module.sequences.Add(new Sequence(s));
                guiRebuildPending = true;
            });

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

        }

        private void ToggleSequenceEditor(Sequence s, bool value)
        {

        }

        private void ToggleControlWindowEditMode(bool value)
        {
            //toggle edit mode
            guiControlWindowEditMode = value;
        }

        private void InitEditorWindow(bool startSolid = true)
        {
            _editorWindow = GameObject.Instantiate(UIAssetsLoader.editorWindowPrefab);
            _editorWindow.transform.SetParent(UIMasterController.Instance.appCanvas.transform, false);
            _editorWindow.GetChild("WindowTitle").AddComponent<PanelDragger>();
            _editorWindowFader = _editorWindow.AddComponent<CanvasGroupFader>();

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
                _editorWindow.transform.position = SequencerEditorWindowPosition;
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
            resizeHandler.minSize = new Vector2(350, 280);
            resizeHandler.maxSize = new Vector2(2000, 1600);


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


            if (UIAssetsLoader.allPrefabsReady && _settingsWindow == null)
            {
                InitSettingsWindow();
            }

            //here we need to wait until prefabs become available and then Instatiate the window
            if (UIAssetsLoader.allPrefabsReady && _controlWindow == null)
            {
                InitControlWindow(GUIEnabled);
            }

            var sequencersArea = _controlWindow.GetChild("WindowContent");
            for(int i=0; i< sequencers.Count; i++)
            {
                var sequencerLine = GameObject.Instantiate(UIAssetsLoader.sequencerLinePrefab);
                sequencerLine.transform.SetParent(sequencersArea.transform, false);

                InitSequencerLinePrefab(sequencerLine, sequencers[i]);
            }
            
            if (UIAssetsLoader.allPrefabsReady && _editorWindow == null && openSequence != null)
            {
                InitEditorWindow(guiControlWindowEditMode & GUIEnabled);
            }
            

            //we don't need to set global alpha as all the windows will be faded it to the setting
            SetGlobalScale(_UIScaleValue);
            guiRebuildPending = false;

        }

        private void CloseAllWindows()
        {
            CloseEditorWindow();

            if (_controlWindowFader)
                _controlWindowFader.FadeTo(0f, 0.1f, () => {
                    GUIEnabled = false;
                    appLauncherButton.SetFalse(false);
                    SequencerWindowPosition = _controlWindow.transform.position;
                    _controlWindow.DestroyGameObjectImmediate();
                    _controlWindow = null;
                    _controlWindowFader = null;
                });

            if (_settingsWindow && _settingsWindow.activeSelf)
            {
                SequencerSettingsWindowPosition = _settingsWindow.transform.position;
                _settingsWindowFader.FadeTo(0f, 0.1f);
                _settingsWindow.SetActive(false);
            }
            
        }

        private void CloseEditorWindow()
        {
            if (_editorWindowFader)
                _editorWindowFader.FadeTo(0f, 0.1f, () => {
                    SequencerEditorWindowPosition = _editorWindow.transform.position;
                    SequencerEditorWindowSize = _editorWindow.GetComponent<RectTransform>().sizeDelta;

                    openSequence = null;
                    _editorWindow.DestroyGameObjectImmediate();
                    _editorWindow = null;
                    _editorWindowFader = null;
                });
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
            PluginConfiguration config = PluginConfiguration.CreateForType<SequencerGUI>();
            config.load();

            SequencerWindowPosition = config.GetValue<Vector3>("SequencerWindowPosition");
            SequencerEditorWindowPosition = config.GetValue<Vector3>("SequencerEditorWindowPosition");
            SequencerEditorWindowSize = config.GetValue<Vector2>("SequencerEditorWindowSize");
            SequencerSettingsWindowPosition = config.GetValue<Vector3>("SequencerSettingsWindowPosition");

            _UIAlphaValue = (float) config.GetValue<double>("UIAlphaValue", 0.8);
            _UIScaleValue = (float) config.GetValue<double>("UIScaleValue", 1.0);
            
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

            PluginConfiguration config = PluginConfiguration.CreateForType<SequencerGUI>();
            config.SetValue("controlWindowPosition", SequencerWindowPosition);
            config.SetValue("editorWindowPosition", SequencerEditorWindowPosition);
            config.SetValue("editorWindowSize", SequencerEditorWindowSize);
            config.SetValue("uiSettingsWindowPosition", SequencerSettingsWindowPosition);
            config.SetValue("UIAlphaValue", (double) _UIAlphaValue);
            config.SetValue("UIScaleValue", (double) _UIScaleValue);
            
            config.save();
        }

        
        
    }

    
}
