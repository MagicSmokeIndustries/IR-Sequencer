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

namespace IRSequencer.Gui
{
    /// <summary>
    /// Class for creating and editing command queue for vessel's Infernal Robotic servos
    /// Only used in flight
    /// 
    /// So far relies on ControlGUI to parse all servos to ServoGroups, 
    /// until we move this functinality elsewhere
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class Sequencer : MonoBehaviour
    {
        public bool guiHidden = false;

        public bool GUIEnabled = false;
        public bool guiSequenceEditor = false;
        private bool isReady = false;
        private bool firstUpdate = true;

        public bool isEnabled = true;
        internal static bool GUISetupDone = false;

        private ApplicationLauncherButton appLauncherButton;
        private string tooltipText = "";
        private string lastTooltipText = "";
        private float tooltipTime;
        private const float TOOLTIP_MAX_TIME = 8f;
        private const float TOOLTIP_DELAY = 1.5f;
        private static GUIStyle tooltipStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle nameStyle;
        private static GUIStyle dotStyle;
        private static GUIStyle playheadStyle;
        private static GUIStyle textFieldStyle;
        private static GUIStyle insertToggleStyle;
        private static GUIStyle hoverStyle;


        private static Color solidColor;
        private static Color opaqueColor;

        //Sequence Editor UI related
        private float currentDelay = 1.0f;
        private int currentMode = 0;
        private string currentGotoIndexString = "1";
        private int currentGotoIndex = 0;
        private int currentGotoCounter = -1;
        //index wher to insert new commands
        private int insertCommandIndex = -1;

        private string lastFocusedControlName = "";
        private string lastFocusedTextFieldValue = "";

        protected static Rect SequencerWindowPos;
        protected static Rect SequencerEditorWindowPos;
        protected static int SequencerWindowID;
        protected static int SequencerEditorWindowID;

        protected static Vector2 servoListScroll;
        protected static Vector2 actionListScroll;
        protected static Vector2 commandListScroll;

        protected static Sequencer SequencerInstance;

        public bool SequencerReady {get { return isReady;}}

        public static Sequencer Instance
        {
            get { return SequencerInstance; }
        }

        internal List<Sequence> sequences;

        internal Sequence openSequence;

        private List<BasicCommand> availableServoCommands;

        static Sequencer()
        {
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            SequencerWindowID = UnityEngine.Random.Range(1000, 2000000) + assemblyName.GetHashCode();
            SequencerEditorWindowID = SequencerWindowID + 1;
        }

        /// <summary>
        ///     Load the textures from files to memory
        ///     Initialise Styles
        /// </summary>
        private static void InitGUI()
        {
            if (!GUISetupDone)
            {
                TextureLoader.InitTextures();

                tooltipStyle = new GUIStyle
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal =
                    {
                        textColor = new Color32(207, 207, 207, 255),
                        background = TextureLoader.BgIcon
                    },
                    stretchHeight = true,
                    border = new RectOffset(3, 3, 3, 3),
                    padding = new RectOffset(4, 4, 6, 4),
                    alignment = TextAnchor.MiddleLeft
                };

                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.padding =  new RectOffset(2, 2, 2, 2);
                buttonStyle.alignment = TextAnchor.MiddleCenter;

                nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    clipping = TextClipping.Overflow
                };

                playheadStyle = new GUIStyle()
                {
                    padding = new RectOffset(0, 0, 0, 0),
                    border = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, -3, -3),
                    normal =
                    {
                        background = TextureLoader.PlayheadBG
                    },
                };

                hoverStyle = new GUIStyle (GUI.skin.label) 
                {
                    hover = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBGHover
                    },
                    padding = new RectOffset(1, 1, 1, 1),
                    border = new RectOffset (1, 1, 1, 1)
                };

                insertToggleStyle = new GUIStyle (GUI.skin.label) 
                {
                    onNormal = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBG
                    },
                    onActive = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBG
                    },
                    onHover = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBGHover
                    },
                    hover = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBGHover
                    },
                    active = 
                    {
                        textColor = Color.white,
                        background = TextureLoader.ToggleBG
                    },
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(1, 1, 1, 1),
                    border = new RectOffset (1, 1, 1, 1)
                };

                dotStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };

                textFieldStyle = new GUIStyle(GUI.skin.textField);

                solidColor = new Color (1, 1, 1, 1);
                opaqueColor = new Color (1, 1, 1, 0.7f);

                GUISetupDone = true;
            }
        }

        private void AddAppLauncherButton()
        {
            if (appLauncherButton == null)
            {
                try
                {
                    var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    TextureLoader.LoadImageFromFile(texture, "icon_seq_button.png");
                    
                    appLauncherButton = ApplicationLauncher.Instance.AddModApplication(delegate { GUIEnabled = true; },
                        delegate { GUIEnabled = false; }, null, null, null, null,
                        ApplicationLauncher.AppScenes.FLIGHT, texture);

                }
                catch (Exception ex)
                {
                    Logger.Log(string.Format("[GUI AddAppLauncherButton Exception, {0}", ex.Message), Logger.Level.Fatal);
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
        }

        public void FixedUpdate()
        {
            //no need to run for non-robotic crafts or if disabled
            if (!isEnabled || sequences == null)
                return;

            var activeSequences = sequences.FindAll(s => s.isActive);
            if (activeSequences.Count == 0) 
            {
                //unlock all sequences
                sequences.ForEach (((Sequence s) => s.isLocked = false));
                return;
            }
            foreach (Sequence sq in activeSequences)
            {
                if (sq.commands == null) continue;

                var affectedServos = new List <IRWrapper.IServo> ();
                sq.commands.FindAll (s => s.servo != null).ForEach ((BasicCommand c) => affectedServos.Add (c.servo));

                if (affectedServos.Any ()) 
                {
                    sequences.FindAll (s => s.commands.Any (c => affectedServos.Contains (c.servo))).ForEach ((Sequence seq) => seq.isLocked = true);
                    //exclude current sequence from Locked List
                    sq.isLocked = false;
                }

                var activeCommands = sq.commands.FindAll(s => s.isActive);
                var activeCount = activeCommands.Count;
                
                foreach (BasicCommand bc in activeCommands)
                {
                    if (bc.wait)
                    {
                        if (bc.waitTime > 0f)
                        {
                            if(UnityEngine.Time.time >= bc.timeStarted + bc.waitTime)
                            {
                                Logger.Log("[Sequencer] Timed wait finished, waitTime was " + bc.waitTime + "s", Logger.Level.Debug);
                                bc.isFinished = true;
                                bc.isActive = false;
                                activeCount--;
                            }
                        }
                    }
                    else if (Math.Abs(bc.servo.Position - bc.position) <= 0.000001)
                    {
                        Logger.Log("[Sequencer] Command finished, servo = " + bc.servo.Name + ", pos = " + bc.position, Logger.Level.Debug);
                        bc.isFinished = true;
                        bc.isActive = false;
                        activeCount--;
                    }
                }

                //need to calculate if there are any active waiting commands
                var activeWaitCount = activeCommands.Count(t => t.wait && t.isActive);
                //if (activeWaitCount == 0) sq.isWaiting = false;

                if (activeCount <= 0)
                {
                    //there are no active commands being executed, including Delays
                    if (sq.lastCommandIndex+1 < sq.commands.Count)
                    {
                        //there are still commands left to execute
                        //need to start from first unfinished command
                        Logger.Log("[Sequencer] Restarting sequence " + sq.name + " from first unfinished command", Logger.Level.Debug);
                        sq.isWaiting = false;
                        sq.Start();
                    }
                    else 
                    { 
                        //there are no more commands in the sequence left to execute
                        if (sq.isLooped)
                        {
                            Logger.Log("[Sequencer] Looping sequence " + sq.name, Logger.Level.Debug);
                            sq.Reset();
                            sq.Start();
                        }
                        else
                        {
                            Logger.Log("[Sequencer] Finished sequence " + sq.name, Logger.Level.Debug);
                            sq.SetFinished();
                            //move lastCommandIndex past the last command so it does not get highlighted
                            sq.lastCommandIndex++;
                            //unlock all other sequences that may have been locked by this sequence
                            if (affectedServos.Any())
                            {
                                sequences.FindAll(s => s.commands.Any(c => affectedServos.Contains(c.servo))).ForEach((Sequence seq) => seq.isLocked = false);
                            }
                        }
                    }
                }
                else
                {
                    //there are still active commands
                    if (activeWaitCount > 0)
                    {
                        //we have some waits in the queue
                        if (sq.commands[sq.lastCommandIndex].wait && sq.commands[sq.lastCommandIndex].waitTime == 0f)
                        {
                            //the last executed command is to wait for all other commands to finish
                            //if it is the only active command we are waiting for - mark it as Finished and proceeed.
                            if (activeWaitCount == 1 && activeCount == 1)
                            {
                                sq.commands[sq.lastCommandIndex].isFinished = true;
                                sq.commands[sq.lastCommandIndex].isActive = false;
                                sq.isWaiting = false;

                                if (sq.commands[sq.lastCommandIndex].gotoIndex != -1)
                                {
                                    //apart from pure wait this is a Goto command

                                    if (sq.commands[sq.lastCommandIndex].gotoCounter > 0 || sq.commands[sq.lastCommandIndex].gotoCounter == -1)
                                    {
                                        //we need to set all commands before it in the sequence as not Finished and Resume from gotoIndex
                                        if (sq.commands[sq.lastCommandIndex].gotoCounter > 0) sq.commands[sq.lastCommandIndex].gotoCounter--;

                                        sq.commands.GetRange(sq.commands[sq.lastCommandIndex].gotoIndex, sq.commands.Count - sq.commands[sq.lastCommandIndex].gotoIndex)
                                                   .ForEach(delegate(BasicCommand c) { c.isFinished = false; c.isActive = false; });
                                        sq.Resume(sq.commands[sq.lastCommandIndex].gotoIndex);
                                    }
                                }
                                else
                                {
                                    Logger.Log("[Sequencer] Restarting sequence " + sq.name + " after Wait command", Logger.Level.Debug);
                                    sq.Start();
                                }
                            }
                            else
                            {
                                //there are some Delays among other commands in the active queue, we should wait for them to finish too
                                //doing nothing here
                            }
                        }
                        else
                        {
                            //last command was not a wait, but there are delays in the queue
                            //just wait for them to complete, do nothing
                        }
                    }
                    else
                    {
                        //there are no wait commands in the queue, we can restart the queue from lastCommandIndex+1
                        if (sq.lastCommandIndex + 1 < sq.commands.Count)
                        {
                            sq.isWaiting = false;
                            sq.Resume(sq.lastCommandIndex + 1);
                        }
                        else
                        {
                            //do nothing, just wait for active commands to finish
                        }
                    }
                }
            }
        }

        private void OnVesselChange(Vessel v)
        {
            sequences.Clear();
            guiSequenceEditor = false;
            availableServoCommands = null;
            openSequence = null;

            //find module SequencerStorage and force loading of sequences
            var storage = v.FindPartModulesImplementing<SequencerStorage>();
            if (storage == null)
            {
                Logger.Log("Could not find SequencerStorage module to load sequences from", Logger.Level.Debug);
                return;
            }
            else
            {
                try
                {
                    if  (v == FlightGlobals.ActiveVessel && storage.Count > 0)
                    {
                        storage[0].LoadSequences();
                    }
                    else
                    {
                        Logger.Log("Could not find SequencerStorage module to load sequences from", Logger.Level.Debug);
                        return;
                    }
                        
                }
                catch (Exception e)
                {
                    Logger.Log("[IRSequencer] Exception in OnVesselChange: " + e.Message);
                }
            }

            Logger.Log("[IRSequencer] OnVesselChange finished, sequences count=" + sequences.Count);
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
            guiSequenceEditor = false;
            availableServoCommands = null;
            openSequence = null;
            
            var storagePart = ship.Parts.Find(p => p.FindModuleImplementing<SequencerStorage>() != null);
            if (storagePart != null)
            {
                var storageModule = storagePart.FindModuleImplementing<SequencerStorage>();
                storageModule.LoadSequences();
            }
        }

        private void Awake()
        {
            LoadConfigXml();

            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onHideUI.Add(OnHideUI);

            SequencerInstance = this;
            isReady = true;

            sequences = new List<Sequence>();
            sequences.Clear();

            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            GameEvents.onEditorRestart.Add(OnEditorRestart);

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequestedForAppLauncher);

            if (ApplicationLauncher.Ready && appLauncherButton == null)
            {
                AddAppLauncherButton();
            }

            Logger.Log("[Sequencer] Awake successful", Logger.Level.Debug);
        }

        private void OnEditorRestart()
        {
            GUIEnabled = false;
            guiSequenceEditor = false;
            availableServoCommands = null;
            openSequence = null;
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
            try
            {
                if (appLauncherButton != null && ApplicationLauncher.Instance != null)
                {
                    ApplicationLauncher.Instance.RemoveModApplication(appLauncherButton);
                    appLauncherButton = null;
                }
            }
            catch (Exception e)
            {
                Logger.Log("[Sequencer] Failed unregistering AppLauncher handlers," + e.Message);
            }
        }

        private void OnDestroy()
        {
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onHideUI.Remove(OnHideUI);

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);

            Sequencer.Instance.isReady = false;
            SaveConfigXml();

            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequestedForAppLauncher);
            DestroyAppLauncherButton();

            //consider unloading textures too in TextureLoader

            Logger.Log("[Sequencer] Destroy successful", Logger.Level.Debug);
        }

        /// <summary>
        /// Has to be called after any GUI element with tooltips.
        /// </summary>
        private void SetTooltipText()
        {
            if (Event.current.type == EventType.Repaint)
            {
                tooltipText = GUI.tooltip;
            }
        }

        /// <summary>
        /// Called in the end of OnGUI(), draws tooltip saved in tooltipText
        /// </summary>
        private void DrawTooltip()
        {
            Vector2 pos = Event.current.mousePosition;
            if (tooltipText != "" && tooltipTime < TOOLTIP_MAX_TIME)
            {
                
                var tooltip = new GUIContent(tooltipText);
                Vector2 size = tooltipStyle.CalcSize(tooltip);

                var tooltipPos = new Rect(pos.x - (size.x / 4), pos.y + 17, size.x, size.y);

                if (tooltipText != lastTooltipText)
                {
                    //reset timer
                    tooltipTime = 0f;
                }

                if (tooltipTime > TOOLTIP_DELAY)
                {
                    GUI.Label(tooltipPos, tooltip, tooltipStyle);
                    GUI.depth = 0;
                }

                tooltipTime += Time.deltaTime;
            }

            if (tooltipText != lastTooltipText) tooltipTime = 0f;
            lastTooltipText = tooltipText;
        }

        private void SequencerControlWindow(int windowID)
        {
            GUI.color = opaqueColor;

            GUILayout.BeginVertical();

            /*GUILayout.BeginHorizontal();

            GUILayout.Label("Sequence Name", GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.Label("Controls", GUILayout.Width(150), GUILayout.Height(22));

            GUILayout.EndHorizontal();
            */
            for (int i = 0; i < sequences.Count; i++)
            {
                //list through all sequences
                var sq = sequences[i];
                GUILayout.BeginHorizontal();

                string sequenceStatus = (sq.isActive) ? "<color=lime>■</color>" : sq.isFinished ? "<color=green>■</color>" : "<color=silver>■</color>";
                if (sq.IsPaused)
                    sequenceStatus = "<color=yellow>■</color>";

                if (sq.isLocked)
                    sequenceStatus = "<color=red>■</color>";
                
                GUI.color = solidColor;

                GUILayout.Label(sequenceStatus, dotStyle, GUILayout.Width(20), GUILayout.Height(22));

                sq.name = GUILayout.TextField(sq.name, textFieldStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));

                bool playToggle = GUILayout.Toggle(sq.isActive, 
                    sq.isActive ? new GUIContent(TextureLoader.PauseIcon, "Pause") : new GUIContent(TextureLoader.PlayIcon, "Play"), 
                    buttonStyle, GUILayout.Width(22), GUILayout.Height(22));
                SetTooltipText ();

                if(playToggle && !sq.isLocked)
                {
                    if (playToggle != sq.isActive)
                    {
                        sq.Start();
                    }
                }
                else if (!sq.isLocked)
                {
                    if (playToggle != sq.isActive && !sq.isFinished)
                    {
                        sq.Pause();
                    }
                }
                
                if (GUILayout.Button(new GUIContent(TextureLoader.StopIcon, "Stop"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    if (!sq.isLocked)
                        sq.Reset();
                }
                SetTooltipText ();

                sq.isLooped = GUILayout.Toggle(sq.isLooped, 
                                               new GUIContent(sq.isLooped ? TextureLoader.LoopingIcon : TextureLoader.LoopIcon, "Loop"), 
                                               buttonStyle, GUILayout.Width(22), GUILayout.Height(22));

                GUILayout.Space(4);

                bool sequenceEditToggle = (openSequence == sq) && guiSequenceEditor;
                
                bool toggleVal = GUILayout.Toggle(sequenceEditToggle, new GUIContent(TextureLoader.EditIcon, "Edit"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22));
                SetTooltipText();

                if (sequenceEditToggle != toggleVal)
                {
                    if (guiSequenceEditor && Equals(openSequence, sq))
                        guiSequenceEditor = !guiSequenceEditor;
                    else
                    {
                        openSequence = sq;
                        if (!guiSequenceEditor)
                            guiSequenceEditor = true;
                    }
                }

                if (GUILayout.Button(new GUIContent(TextureLoader.CloneIcon, "Clone"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    sequences.Add(new Sequence(sq));
                }
                SetTooltipText ();

                GUILayout.Space(4);
                
                if (GUILayout.Button(new GUIContent(TextureLoader.TrashIcon, "Delete"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    sq.Pause();
                    sq.Reset();
                    if (openSequence == sq)
                    {
                        guiSequenceEditor = false;
                        openSequence = null;
                    }
                    sequences.RemoveAt(i);
                }
                SetTooltipText ();
                GUI.color = opaqueColor;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUI.color = solidColor;

            if(GUILayout.Button("Add new", buttonStyle, GUILayout.Height(22)))
            {
                sequences.Add(new Sequence());
            }
            GUI.color = opaqueColor;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        /// <summary>
        /// Draws the text field and returns its value
        /// </summary>
        /// <returns>Entered value</returns>
        /// <param name="controlName">Control name.</param>
        /// <param name="value">Value.</param>
        /// <param name="format">Format.</param>
        /// <param name="style">Style.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        private string DrawTextField(string controlName, float value, string format, GUIStyle style, GUILayoutOption width, GUILayoutOption height)
        {
            string focusedControlName = GUI.GetNameOfFocusedControl ();

            if (controlName == focusedControlName 
                && lastFocusedTextFieldValue == "")
            {
                lastFocusedTextFieldValue = string.Format (format, value);
            }

            string tmp = (controlName == focusedControlName) 
                ? lastFocusedTextFieldValue 
                : string.Format (format, value);

            GUI.SetNextControlName(controlName);
            tmp = GUILayout.TextField(tmp, style, width, height);

            if (controlName == focusedControlName 
                && focusedControlName == lastFocusedControlName)
                lastFocusedTextFieldValue = tmp;

            return tmp;
        }


        private void SequencerEditorWindow(int windowID)
        {
            if (openSequence == null)
                return;

            if (IRWrapper.IRController.ServoGroups == null)
                return;

            GUI.color = opaqueColor;

            string tmpString;
            float tmpValue;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayoutOption maxHeight = GUILayout.MaxHeight(Screen.height * 0.5f);

            GUILayout.BeginVertical(GUILayout.Width(270));

            //draw buttons here
            GUILayout.BeginHorizontal();
            var modes = new String[] {"Servos", "ActionGroups"};
            GUI.color = solidColor;
            currentMode = GUILayout.Toolbar (currentMode, modes, buttonStyle,  GUILayout.Height(22));
            GUI.color = opaqueColor;
            GUILayout.EndHorizontal();

            var allServos = new List<IRWrapper.IServo>();

            if (currentMode == 0) 
            {
                foreach (IRWrapper.IControlGroup g in IRWrapper.IRController.ServoGroups) 
                {
                    allServos.AddRange (g.Servos);
                }

                if (availableServoCommands == null) 
                {
                    availableServoCommands = new List<BasicCommand> ();    
                }

                if (allServos.Count != availableServoCommands.Count) 
                {
                    availableServoCommands.Clear ();
                    //rebuild the list of available commands
                    foreach (IRWrapper.IServo s in allServos) 
                    {
                        var bc = new BasicCommand (s, s.Position, 1f);
                        availableServoCommands.Add (bc);
                    }
                }

                servoListScroll = GUILayout.BeginScrollView (servoListScroll, false, false, maxHeight);
                for (int i = 0; i < IRWrapper.IRController.ServoGroups.Count; i++) 
                {
                    IRWrapper.IControlGroup g = IRWrapper.IRController.ServoGroups [i];

                    if (g.Servos.Any ()) 
                    {
                        GUILayout.BeginHorizontal ();

                        GUI.color = solidColor;
                        if (g.Expanded) 
                        {
                            g.Expanded = !GUILayout.Button (TextureLoader.CollapseIcon, buttonStyle, GUILayout.Width (20), GUILayout.Height (22));
                        } 
                        else 
                        {
                            g.Expanded = GUILayout.Button (TextureLoader.ExpandIcon, buttonStyle, GUILayout.Width (20), GUILayout.Height (22));
                        }
                        
                        nameStyle.fontStyle = FontStyle.Bold;
                        GUILayout.Label (g.Name, nameStyle, GUILayout.ExpandWidth (true), GUILayout.Height (22));
                        nameStyle.fontStyle = FontStyle.Normal;
                        GUI.color = opaqueColor;
                        GUILayout.EndHorizontal ();

                        if (g.Expanded) 
                        {
                            GUILayout.BeginHorizontal (GUILayout.Height (5));
                            GUILayout.EndHorizontal ();

                            foreach (IRWrapper.IServo servo in g.Servos) 
                            {
                                GUILayout.BeginHorizontal (hoverStyle);
                                GUI.color = solidColor;

                                var avCommand = availableServoCommands.FirstOrDefault(t => t.servo.Equals(servo));

                                if (avCommand == null) 
                                {
                                    Logger.Log ("[Sequencer] Cannot find matching command for servo " + servo.Name, Logger.Level.Debug);
                                    return;
                                }
                               
                                if (GUILayout.Button ("Add", buttonStyle, GUILayout.Width (30), GUILayout.Height (22))) 
                                {
                                    openSequence.Pause ();
                                    openSequence.Reset ();
                                    if (insertCommandIndex + 1 == openSequence.commands.Count)
                                    {
                                        openSequence.commands.Add (new BasicCommand (avCommand));
                                        insertCommandIndex++;
                                    }
                                    else
                                    {
                                        openSequence.commands.Insert (insertCommandIndex + 1, new BasicCommand (avCommand));
                                    }
                                }
                                
                                GUILayout.Label (servo.Name, nameStyle, GUILayout.ExpandWidth (true), GUILayout.Height (22));

                                Rect last = GUILayoutUtility.GetLastRect();
                                Vector2 pos = Event.current.mousePosition;
                                bool highlight = last.Contains(pos);
                                servo.Highlight = highlight;

                                var e = Event.current;
                                if (e.isMouse && e.clickCount == 2 && last.Contains(e.mousePosition))
                                {
                                    avCommand.position = servo.Position;
                                }

                                string focusedControlName = GUI.GetNameOfFocusedControl ();
                                string thisControlName = "SequencerPosition " + servo.UID;

                                tmpString = DrawTextField (thisControlName, avCommand.position, "{0:#0.0#}", 
                                    textFieldStyle, GUILayout.Width (40), GUILayout.Height (22));

                                var valueChanged = (thisControlName == focusedControlName && 
                                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter));

                                if (float.TryParse (tmpString, out tmpValue) && valueChanged) 
                                {
                                    avCommand.position = Mathf.Clamp(tmpValue, avCommand.servo.MinPosition, avCommand.servo.MaxPosition);
                                    lastFocusedTextFieldValue = "";
                                }

                                GUILayout.Label ("@", nameStyle, GUILayout.Height (22));

                                thisControlName = "SequencerSpeed " + servo.UID;

                                tmpString = DrawTextField (thisControlName, avCommand.speedMultiplier, "{0:#0.0#}", 
                                    textFieldStyle, GUILayout.Width (30), GUILayout.Height (22));

                                valueChanged = (thisControlName == focusedControlName && 
                                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter));
                                
                                if (float.TryParse (tmpString, out tmpValue) && valueChanged)
                                {
                                    avCommand.speedMultiplier = Mathf.Clamp (tmpValue, 0.05f, 1000f);
                                    lastFocusedTextFieldValue = "";
                                }
                                GUI.color = opaqueColor;
                                GUILayout.EndHorizontal ();
                            }

                            GUILayout.BeginHorizontal (GUILayout.Height (5));
                            GUILayout.EndHorizontal ();
                        }
                    
                    }
                }
                GUILayout.EndScrollView ();
            }
            else 
            {
                //here goes actiongroup stuff
                actionListScroll = GUILayout.BeginScrollView (actionListScroll, false, false, maxHeight);
                foreach (KSPActionGroup a in Enum.GetValues(typeof(KSPActionGroup)).Cast<KSPActionGroup>())
                {
                    if (a == KSPActionGroup.None)
                        continue;
                    
                    GUILayout.BeginHorizontal ();
                    GUI.color = solidColor;
                    if (GUILayout.Button ("Add", buttonStyle, GUILayout.Width (30), GUILayout.Height (22))) 
                    {
                        openSequence.Pause ();
                        openSequence.Reset ();
                        
                        var newCommand = new BasicCommand (a);

                        if (insertCommandIndex + 1 == openSequence.commands.Count)
                        {
                            openSequence.commands.Add (newCommand);
                            insertCommandIndex++;
                        }
                        else
                        {
                            openSequence.commands.Insert (insertCommandIndex + 1, newCommand);
                        }
                    }
                    GUILayout.Label ("Toggle AG: " + a.ToString(), GUILayout.ExpandWidth (true), GUILayout.Height (22));
                    GUI.color = opaqueColor;
                    GUILayout.EndHorizontal ();
                }
                GUILayout.EndScrollView ();
            }

            GUILayout.BeginHorizontal(GUILayout.Height(5));
            GUILayout.Label("", GUILayout.ExpandWidth(true), GUILayout.Height(5));
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.scrollView);
            GUILayout.BeginHorizontal();
            GUI.color = solidColor;
            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                openSequence.Pause ();
                openSequence.Reset ();
                
                var newCommand = new BasicCommand(true, currentDelay);
                if (insertCommandIndex + 1 == openSequence.commands.Count)
                {
                    openSequence.commands.Add (newCommand);
                    insertCommandIndex++;
                }
                else
                {
                    openSequence.commands.Insert (insertCommandIndex + 1, newCommand);
                }
            }

            GUILayout.Label("Delay for ", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0.0#}", currentDelay), textFieldStyle, GUILayout.Width(40), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentDelay = Mathf.Clamp(tmpValue, 0f, 600f);
            }
            GUILayout.Label("s ", nameStyle, GUILayout.Width(18), GUILayout.Height(22));
            
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                openSequence.Pause ();
                openSequence.Reset ();
                
                var newCommand = new BasicCommand(true);
                if (insertCommandIndex + 1 == openSequence.commands.Count)
                {
                    openSequence.commands.Add (newCommand);
                    insertCommandIndex++;
                }
                else
                {
                    openSequence.commands.Insert (insertCommandIndex + 1, newCommand);
                }
            }
            GUILayout.Label("Wait for Moves", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                openSequence.Pause ();
                openSequence.Reset ();
                
                var newCommand = new BasicCommand(currentGotoIndex, currentGotoCounter);
                if (insertCommandIndex + 1 == openSequence.commands.Count)
                {
                    openSequence.commands.Add (newCommand);
                    insertCommandIndex++;
                }
                else
                {
                    openSequence.commands.Insert (insertCommandIndex + 1, newCommand);
                }
            }

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Go To Command #", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            if (GUILayout.Button ("-", buttonStyle, GUILayout.Width (18), GUILayout.Height (22))) 
            {
                currentGotoIndex = Math.Max (currentGotoIndex - 1, 0);
                currentGotoIndexString = (currentGotoIndex+1).ToString ();
            }
            currentGotoIndexString = GUILayout.TextField(string.Format("{0:#0}", currentGotoIndexString), textFieldStyle, GUILayout.Width(25), GUILayout.Height(22));

            if (float.TryParse(currentGotoIndexString, out tmpValue))
            {
                currentGotoIndex = (int)Mathf.Clamp(tmpValue-1, 0f, openSequence.commands.Count-1);
            }

            if (GUILayout.Button ("+", buttonStyle, GUILayout.Width (18), GUILayout.Height (22))) 
            {
                currentGotoIndex = Math.Max (Math.Min (currentGotoIndex + 1, openSequence.commands.Count-1), 0);
                currentGotoIndexString = (currentGotoIndex+1).ToString ();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Repeat (-1 for loop)", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            if (GUILayout.Button ("-", buttonStyle, GUILayout.Width (18), GUILayout.Height (22))) 
            {
                currentGotoCounter = Math.Max (currentGotoCounter - 1, -1);
            }

            tmpString = GUILayout.TextField(string.Format("{0:#0}", currentGotoCounter), textFieldStyle, GUILayout.Width(25), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentGotoCounter = (int)Math.Max(tmpValue, -1);
            }
            if (GUILayout.Button ("+", buttonStyle, GUILayout.Width (18), GUILayout.Height (22))) 
            {
                currentGotoCounter = Math.Max (currentGotoCounter + 1, -1);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
            GUI.color = opaqueColor;
            GUILayout.EndVertical();

            GUILayout.EndVertical ();
            GUILayout.BeginVertical();

            commandListScroll = GUILayout.BeginScrollView(commandListScroll, false, false, maxHeight);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUI.color = solidColor;
            GUILayout.Label("Commands:", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUI.color = opaqueColor;
            GUILayout.EndHorizontal();

            //set pointer to last command
            if (insertCommandIndex < -1 || insertCommandIndex >= openSequence.commands.Count)
                insertCommandIndex = openSequence.commands.Count-1;

            if (insertCommandIndex == -1) 
            {
                playheadStyle.normal.background = TextureLoader.ToggleBGHover;
                GUILayout.BeginHorizontal (GUILayout.Height(1));
                GUILayout.Label ("", playheadStyle, GUILayout.Height(1));
                GUILayout.EndHorizontal ();
            } 
            else 
            {
                playheadStyle.normal.background = null;
            }

            //now begin listing commands in sequence
            for (int i = 0; i < openSequence.commands.Count; i++ )
            {
                BasicCommand bc = openSequence.commands[i];

                if (openSequence.lastCommandIndex == i)
                {
                    playheadStyle.normal.background = openSequence.commands[i].isActive ? TextureLoader.PlayheadBG : TextureLoader.PlayheadBGPaused;
                    GUILayout.BeginHorizontal(playheadStyle);
                }
                else
                {
                    playheadStyle.normal.background = null;
                    GUILayout.BeginHorizontal(playheadStyle);
                }

                GUI.color = solidColor;
                string commandStatus = (bc.isActive || openSequence.lastCommandIndex == i) ? "<color=lime>■</color>" : bc.isFinished ? "<color=green>■</color>" : "<color=silver>■</color>";
                if (openSequence.lastCommandIndex == i && !bc.isActive)
                    commandStatus = "<color=yellow>■</color>";
                GUILayout.Label(commandStatus, dotStyle, GUILayout.Width(20), GUILayout.Height(22));

                //GUILayout.Label((i+1).ToString() + ":", dotStyle, GUILayout.Width(25), GUILayout.Height(22));

                if(GUILayout.Toggle((i == insertCommandIndex), new GUIContent((i+1).ToString() + ":", "Insert After"), insertToggleStyle, GUILayout.Width(25), GUILayout.Height(22)))
                {
                    insertCommandIndex = i;
                }
                else if (insertCommandIndex==i)
                {
                    insertCommandIndex = -1;
                }

                var labelText = "";
                if (bc.wait)
                {
                    if (bc.waitTime > 0f)
                    {
                        if (bc.isActive)
                            labelText = "Delaying for " + Math.Round(bc.timeStarted + bc.waitTime - UnityEngine.Time.time, 2) + "s";
                        else
                            labelText = "Delay for " + Math.Round(bc.waitTime, 2) + "s";
                    }
                    else if (bc.gotoIndex != -1)
                    {
                        labelText = "Go To Command # " + (bc.gotoIndex + 1).ToString();
                        if (bc.gotoCounter != -1)
                        {
                            labelText += ", " + bc.gotoCounter + " more times.";
                        }
                    }
                    else
                        labelText = (bc.isActive ? "Waiting" : "Wait") + " for Moves";
                }
                else if (bc.servo != null) 
                    labelText = bc.servo.Name + " to " + Math.Round(bc.position, 2).ToString() + " at " + Math.Round(bc.speedMultiplier, 2).ToString() + "x";
                else if (bc.ag != KSPActionGroup.None)
                {
                    labelText = "Toggle ActionGroup: " + bc.ag.ToString ();
                }

                GUILayout.Label(labelText, nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
                if (i > 0)
                {
                    if (GUILayout.Button(TextureLoader.UpIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                    {
                        openSequence.Pause();
                        var tmp = openSequence.commands[i - 1];
                        openSequence.commands[i - 1] = bc;
                        openSequence.commands[i] = tmp;
                        openSequence.Reset();
                    }
                }
                else
                    GUILayout.Space(24);

                if (i < openSequence.commands.Count - 1)
                {
                    if (GUILayout.Button(TextureLoader.DownIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                    {
                        openSequence.Pause();
                        var tmp = openSequence.commands[i + 1];
                        openSequence.commands[i + 1] = bc;
                        openSequence.commands[i] = tmp;
                        openSequence.Reset();
                    }
                }
                else
                    GUILayout.Space(24);

                if (GUILayout.Button(TextureLoader.TrashIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                {
                    openSequence.Pause();
                    openSequence.commands.RemoveAt(i);
                    openSequence.Reset();
                }
                GUI.color = opaqueColor;
                GUILayout.EndHorizontal();

                if (i == insertCommandIndex) 
                {
                    playheadStyle.normal.background = TextureLoader.ToggleBGHover;
                    GUILayout.BeginHorizontal (GUILayout.Height(1));
                    GUILayout.Label ("", playheadStyle, GUILayout.Height(1));
                    GUILayout.EndHorizontal ();
                } 
                else 
                {
                    playheadStyle.normal.background = null;
                }
                    
            }
            
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();

            GUI.color = solidColor;

            bool playToggle = GUILayout.Toggle(openSequence.isActive,
                   openSequence.isActive ? "Pause" : "Play",
                   buttonStyle, GUILayout.Width(100), GUILayout.Height(22));
            SetTooltipText();

            if (playToggle && !openSequence.isLocked)
            {
                if (playToggle != openSequence.isActive)
                {
                    openSequence.Start();
                }
            }
            else if (!openSequence.isLocked)
            {
                if (playToggle != openSequence.isActive && !openSequence.isFinished)
                {
                    openSequence.Pause();
                }
            }

            if (GUILayout.Button("Step", buttonStyle, GUILayout.Width(100), GUILayout.Height(22)))
            {
                if (!openSequence.isLocked)
                {
                    openSequence.Step();
                }
            }
            
            if (GUILayout.Button("Stop", buttonStyle, GUILayout.Width(100), GUILayout.Height(22)))
            {
                if (!openSequence.isLocked)
                {
                    openSequence.Pause();
                    openSequence.Reset();
                }
            }

            openSequence.isLooped = GUILayout.Toggle(openSequence.isLooped, "Looping", buttonStyle, GUILayout.Height(22));
            
            GUI.color = opaqueColor;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        public void LoadConfigXml()
        {
            PluginConfiguration config = PluginConfiguration.CreateForType<Sequencer>();
            config.load();
            SequencerWindowPos = config.GetValue<Rect>("SequencerWindowPos");
            SequencerEditorWindowPos = config.GetValue<Rect>("SequencerEditorWindowPos");
        }

        public void SaveConfigXml()
        {
            PluginConfiguration config = PluginConfiguration.CreateForType<Sequencer>();
            config.SetValue("SequencerWindowPos", SequencerWindowPos);
            config.SetValue("SequencerEditorWindowPos", SequencerEditorWindowPos);
            config.save();
        }

        private void ProcessFocusChange()
        {
            var temp = lastFocusedControlName.Split (' ');
            Logger.Log ("[GUI] Focus change, lastName = " + lastFocusedControlName 
                + ", lastValue = " + lastFocusedTextFieldValue 
                + ", temp.Length = " + temp.Length, Logger.Level.Debug);

            var servoFields = new string[2] {"SequencerPosition", "SequencerSpeed"};

            var pos = Array.IndexOf (servoFields, temp [0]);

            Logger.Log ("availableServoCommands found: " + (availableServoCommands != null), Logger.Level.Debug);
            Logger.Log ("pos: " + pos, Logger.Level.Debug);

            if (temp.Length == 2 && pos >= 0 && pos < 2)
            {
                uint servoUID = 0;
                if(uint.TryParse(temp[1], out servoUID) && availableServoCommands != null)
                {
                    float tmpValue;
                    var command = availableServoCommands.Find (p => p.servo.UID == servoUID);
                    Logger.Log ("Command found: " + (command != null), Logger.Level.Debug);

                    if (float.TryParse (lastFocusedTextFieldValue, out tmpValue)) 
                    {
                        if (pos == 0 && command != null && command.servo != null)
                        {
                            command.position = Mathf.Clamp(tmpValue, command.servo.MinPosition, command.servo.MaxPosition);
                        }
                        else if (pos == 1 && command != null && command.servo != null)
                        {
                            command.speedMultiplier = Mathf.Clamp(tmpValue, 0.05f, 1000f);
                        }
                    }

                }
            }

            lastFocusedControlName = GUI.GetNameOfFocusedControl();
            lastFocusedTextFieldValue = "";
        }

        private void OnGUI()
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
                
                var storage = FlightGlobals.ActiveVessel.FindPartModulesImplementing<SequencerStorage>();
                if (GUIEnabled && (storage == null || storage.Count == 0))
                {
                    ScreenMessages.PostScreenMessage("Sequencer Storage module is required (add probe core).", 3, ScreenMessageStyle.UPPER_CENTER);
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
                        var storagePart = s.Parts.Find (p => p.FindModuleImplementing<SequencerStorage> () != null);
                        if (GUIEnabled && storagePart == null) 
                        {
                            ScreenMessages.PostScreenMessage("Sequencer Storage module is required (add probe core).", 3, ScreenMessageStyle.UPPER_CENTER);
                            GUIEnabled = false;
                            return;
                        }
                    }
                }
            }

            if (SequencerWindowPos.x == 0 && SequencerWindowPos.y == 0)
            {
                SequencerWindowPos = new Rect(Screen.width - 510, 70, 10, 10);
            }

            if (SequencerEditorWindowPos.x == 0 && SequencerEditorWindowPos.y == 0)
            {
                SequencerEditorWindowPos = new Rect(Screen.width - 510, 300, 10, 10);
            }

            GUI.skin = DefaultSkinProvider.DefaultSkin;
            GUI.color = opaqueColor;

            if (!GUISetupDone)
                InitGUI();
            
            if (GUIEnabled && !guiHidden)
            {
                if (lastFocusedControlName != GUI.GetNameOfFocusedControl())
                {
                    ProcessFocusChange ();
                }

                //this code defocuses the TexFields if you click mouse elsewhere
                if (GUIUtility.hotControl > 0 && GUIUtility.hotControl != GUIUtility.keyboardControl)
                {
                    GUIUtility.keyboardControl = 0;
                }

                SequencerWindowPos = GUILayout.Window(SequencerWindowID, SequencerWindowPos,
                SequencerControlWindow,
                "Servo Sequencer",
                GUILayout.Width(300),
                GUILayout.Height(80));

                if (guiSequenceEditor)
                {
                    float height = Screen.height / 2f;
                    string windowTitle = "Sequence Editor: " + openSequence.name;

                    if (openSequence.isLocked)
                        windowTitle += " (locked)";

                    if(openSequence != null)
                        SequencerEditorWindowPos = GUILayout.Window(SequencerEditorWindowID, SequencerEditorWindowPos,
                        SequencerEditorWindow,
                        windowTitle,
                        GUILayout.Width(640),
                        GUILayout.Height(height));
                }
            }
            GUI.color = solidColor;
            DrawTooltip();

            if(HighLogic.LoadedSceneIsEditor)
            {
                var mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                bool lockEditor = GUIEnabled && (SequencerWindowPos.Contains(mousePos) || (guiSequenceEditor && SequencerEditorWindowPos.Contains(mousePos)));

                EditorLock(lockEditor);
            }
        }

        /// <summary>
        ///     Applies or removes the lock
        /// </summary>
        /// <param name="apply">Which way are we going</param>
        internal void EditorLock(Boolean apply)
        {
            //only do this lock in the editor - no point elsewhere
            if (HighLogic.LoadedSceneIsEditor && apply)
            {
                //only add a new lock if there isnt already one there
                if (InputLockManager.GetControlLock("IRSGUILockOfEditor") != ControlTypes.EDITOR_LOCK)
                {
                    Logger.Log(String.Format("[GUI] AddingLock-{0}", "IRSGUILockOfEditor"), Logger.Level.Debug);

                    InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, "IRSGUILockOfEditor");
                }
            }
            //Otherwise make sure the lock is removed
            else
            {
                //Only try and remove it if there was one there in the first place
                if (InputLockManager.GetControlLock("IRSGUILockOfEditor") == ControlTypes.EDITOR_LOCK)
                {
                    Logger.Log(String.Format("[GUI] Removing-{0}", "IRSGUILockOfEditor"), Logger.Level.Debug);
                    InputLockManager.RemoveControlLock("IRSGUILockOfEditor");
                }
            }
        }
    }

    
}
