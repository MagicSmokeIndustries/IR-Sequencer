using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using KSP.IO;
using IRSequencer.API;
using IRSequencer.Utility;

namespace IRSequencer.Gui
{
    /// <summary>
    /// Class for creating and editing command queue for vessel's Infernal Robotic servos
    /// Only used in flight
    /// 
    /// So far relies on ControlGUI to parse all servos to ServoGroups, 
    /// until we move this functinality elsewhere
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Sequencer : MonoBehaviour
    {
        public bool guiHidden = false;

        public bool GUIEnabled = false;
        public bool guiSequenceEditor = false;
        private bool isReady = false;

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

        private static Color solidColor;
        private static Color opaqueColor;

        private float currentDelay = 1.0f;
        private int currentMode = 0;
        private int currentGotoIndex = 0;
        private int currentGotoCounter = -1;

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

        private List<Sequence> sequences;

        private Sequence openSequence;

        private List<BasicCommand> availableServoCommands;

        static Sequencer()
        {
            string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            SequencerWindowID = UnityEngine.Random.Range(1000, 2000000) + assemblyName.GetHashCode();
            SequencerEditorWindowID = SequencerWindowID + 1;
        }

        public class BasicCommand
        {
            internal IRWrapper.IRAPI.IRServo servo;
            public float timeStarted;
            public bool wait;
            public float waitTime=0f;
            public float position;
            public float speedMultiplier;
            public bool isActive = false;
            public bool isFinished = false;
            public KSPActionGroup ag = KSPActionGroup.None;
            public int gotoIndex = -1;
            public int gotoCounter = -1;
            private int gotoCommandCounter = -1;

            public BasicCommand(IRWrapper.IRAPI.IRServo s, float p, float sp)
            {
                servo = s;
                position = p;
                speedMultiplier = sp;
                wait = false;
            }

            public BasicCommand(bool w)
            {
                servo = null;
                position = 0;
                speedMultiplier = 0;
                wait = w;
            }

            public BasicCommand(bool w, float t) : this(w)
            {
                waitTime = t;
            }

            public BasicCommand(KSPActionGroup g) : this(false)
            {
                ag = g;
            }

            public BasicCommand(int targetIndex, int counter)
                : this(true)
            {
                gotoIndex = targetIndex;
                gotoCounter = counter;
                gotoCommandCounter = counter;
            }

            public BasicCommand(BasicCommand clone)
            {
                servo = clone.servo;
                position = clone.position;
                speedMultiplier = clone.speedMultiplier;
                wait = clone.wait;
                waitTime = clone.waitTime;
                ag = clone.ag;
            }

            public void Execute()
            {
                isActive = true;
                timeStarted = UnityEngine.Time.time;

                if (ag != KSPActionGroup.None)
                {
                    FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup (ag);
                    isActive = false;
                    isFinished = true;
                    Logger.Log("[Sequencer] Firing ActionGroup = " + ag.ToString(), Logger.Level.Debug);
                    return;
                }

                if (wait)
                {
                    //wait commands are dealt with separately in FixedUpdate
                    //all we had to do is mark it as Active and set Timestamp, as we did above
                }
                else
                {
                    Logger.Log("[Sequencer] Executing command, servoName= " + servo.Name + ", pos=" + position, Logger.Level.Debug);
                    servo.MoveTo(position, speedMultiplier);
                }
            }

            public void Stop()
            {
                timeStarted = 0f;
                gotoCounter = gotoCommandCounter;
                if (wait)
                    return;
                else if (servo != null)
                {
                    servo.Stop();
                }

                isActive = false;
                isFinished = false;
            }

        }

        public class Sequence
        {
            internal List<BasicCommand> commands;
            public bool isLooped = false;
            public int lastCommandIndex = -1;
            public bool isActive = false;
            public bool isFinished = false;
            public bool isWaiting = false; 
            public bool isLocked = false; //sequence is Locked if any of the servos in its commands list are busy
            public string name = "";

            public Sequence ()
            {
                commands = new List<BasicCommand>();
                name = "New Sequence";
            }

            public Sequence (BasicCommand b) : this()
            {
                commands.Add(b);
            }

            public Sequence (Sequence baseSequence) :this()
            {
                commands.AddRange(baseSequence.commands);
                name = "Copy of " + baseSequence.name;
            }

            public void Resume(int commandIndex)
            {
                Logger.Log("[Sequencer] Sequence resumed from index " + commandIndex, Logger.Level.Debug);

                if (commands == null) return;

                if (isLocked)
                {
                    Logger.Log ("[Sequencer] Cannot resume sequence " + name + " as it is Locked", Logger.Level.Debug);
                    return;
                }

                isActive = true;

                //resume from given index
                lastCommandIndex = commandIndex;
                if (lastCommandIndex == -1)
                    return;

                //now we can start/continue execution
                //we execute commands until first wait command
                var nextWaitCommandIndex = commands.FindIndex(lastCommandIndex, s => s.wait);
                if (nextWaitCommandIndex == -1)
                {
                    //there are no Waits left, execute all the rest;
                    nextWaitCommandIndex = commands.Count;
                }

                Logger.Log("[Sequencer] nextWaitCommandIndex = " + nextWaitCommandIndex, Logger.Level.Debug);

                for (int i = lastCommandIndex; i < nextWaitCommandIndex; i++)
                {
                    commands[i].Execute();
                }

                lastCommandIndex = nextWaitCommandIndex;

                if (lastCommandIndex < commands.Count)
                {
                    //need to put timestamp on that wait command
                    commands[lastCommandIndex].Execute();
                    isWaiting = true;
                    Logger.Log("[Sequencer] Sequence is waiting, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
                }


                Logger.Log("[Sequencer] Sequence Resume finished, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
                //else we are either finished, or most likely waiting for commands to finish.
            }

            public void Start()
            {
                Logger.Log("[Sequencer] Sequence started", Logger.Level.Debug);

                if (commands == null) return;

                if (isLocked)
                {
                    Logger.Log ("[Sequencer] Cannot start sequence " + name + " as it is Locked", Logger.Level.Debug);
                    return;
                }

                isActive = true;

                //find first unfinished command
                lastCommandIndex = commands.FindIndex(s => s.isFinished == false);
                Logger.Log("[Sequencer] First unfinished Index = " + lastCommandIndex, Logger.Level.Debug);
                if (lastCommandIndex == -1)
                    return;

                //now we can start/continue execution
                //we execute commands until first wait command

                Resume(lastCommandIndex);

                Logger.Log("[Sequencer] Sequence Start finished, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
                //else we are either finished, or most likely waiting for commands to finish.
            }
            public void Pause()
            {
                if (commands == null) return;

                // should find last finished command and reset sequence index to it
                lastCommandIndex = commands.IndexOf(commands.FindLast(s => s.isFinished));

                //now we need to stop all the commands with index > lastCommandIndex
                for (int i = lastCommandIndex+1; i < commands.Count; i++)
                {
                    commands[i].Stop();
                }

                isActive = false;
                isWaiting = false;

                Logger.Log("[Sequencer] Sequence Paused, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
            }

            public void Reset()
            {
                //return Sequence to the start
                lastCommandIndex = -1;
                isActive = false;
                isFinished = false;
                isWaiting = false;

                if (commands == null) return;

                foreach (BasicCommand c in commands)
                {
                    c.Stop();
                    c.isActive = false;
                    c.isFinished = false;
                }
            }

            public void SetFinished()
            {
                isActive = false;
                isFinished = true;
                isWaiting = false;

                //set all commands as Finished and not Active
                commands.ForEach(delegate(BasicCommand c) { c.isActive = false; c.isFinished = true; });
            }
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
                buttonStyle.padding =  new RectOffset(2, 2, 2, 2);;
                buttonStyle.alignment = TextAnchor.MiddleCenter;

                nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    clipping = TextClipping.Overflow
                };

                dotStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };

                solidColor = new Color (1, 1, 1, 1);
                opaqueColor = new Color (1, 1, 1, 0.7f);
            }
        }

        private void OnAppReady()
        {
            if (appLauncherButton == null)
            {
                try
                {
                    var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    TextureLoader.LoadImageFromFile(texture, "presetmode.png");
                    
                    appLauncherButton = ApplicationLauncher.Instance.AddModApplication(delegate { GUIEnabled = true; },
                        delegate { GUIEnabled = false; }, null, null, null, null,
                        ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.VAB |
                        ApplicationLauncher.AppScenes.SPH, texture);

                }
                catch (Exception ex)
                {
                    Logger.Log(string.Format("[GUI OnnAppReady Exception, {0}", ex.Message), Logger.Level.Fatal);
                }
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

                var affectedServos = new List <IRWrapper.IRAPI.IRServo> ();
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
                                    sq.Start();
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


        private void Awake()
        {
            LoadConfigXml();

            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onHideUI.Add(OnHideUI);

            SequencerInstance = this;
            isReady = true;

            sequences = new List<Sequence>();
            sequences.Clear();

            GameEvents.onGUIApplicationLauncherReady.Add(OnAppReady);
            
            Logger.Log("[Sequencer] Awake successful", Logger.Level.Debug);
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

            Logger.Log("[Sequencer] Start successful", Logger.Level.Debug);
        }

        private void OnShowUI()
        {
            guiHidden = false;
        }

        private void OnHideUI()
        {
            guiHidden = true;
        }

        private void OnDestroy()
        {
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onHideUI.Remove(OnHideUI);

            Sequencer.Instance.isReady = false;
            SaveConfigXml();

            try
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(OnAppReady);

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
            //requires ServoGroups to be parsed
            if (!IRWrapper.APIReady)
                return;
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
                if (sq.isLocked)
                    sequenceStatus = "<color=red>■</color>";
                GUI.color = solidColor;

                GUILayout.Label(sequenceStatus, dotStyle, GUILayout.Width(20), GUILayout.Height(22));

                sq.name = GUILayout.TextField(sq.name, GUILayout.ExpandWidth(true), GUILayout.Height(22));

                if (GUILayout.Button(new GUIContent(TextureLoader.PlayIcon, "Play"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    sq.Start();
                }
                SetTooltipText ();

                if (GUILayout.Button(new GUIContent(TextureLoader.PauseIcon, "Pause"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    sq.Pause();
                }
                SetTooltipText ();


                if (GUILayout.Button(new GUIContent(TextureLoader.StopIcon, "Reset"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    sq.Reset();
                }
                SetTooltipText ();

                GUILayout.Space(4);

                if (GUILayout.Button(new GUIContent(TextureLoader.EditIcon, "Edit"), buttonStyle, GUILayout.Width(22), GUILayout.Height(22)))
                {
                    openSequence = sq;
                    guiSequenceEditor = !guiSequenceEditor;
                }
                SetTooltipText ();

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

        private void SequencerEditorWindow(int windowID)
        {
            //requires ServoGroups to be parsed
            if (!IRWrapper.APIReady)
                return;

            if (openSequence == null)
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
            openSequence.isLooped = GUILayout.Toggle(openSequence.isLooped, "Looping", buttonStyle,  GUILayout.Height(22));
            GUI.color = opaqueColor;
            GUILayout.EndHorizontal();

            var allServos = new List<IRWrapper.IRAPI.IRServo>();

            if (currentMode == 0) 
            {
                foreach (IRWrapper.IRAPI.IRControlGroup g in IRWrapper.IRController.ServoGroups) 
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
                    foreach (IRWrapper.IRAPI.IRServo s in allServos) 
                    {
                        var bc = new BasicCommand (s, s.Position, 1f);
                        availableServoCommands.Add (bc);
                    }
                }

                servoListScroll = GUILayout.BeginScrollView (servoListScroll, false, false, maxHeight);
                for (int i = 0; i < IRWrapper.IRController.ServoGroups.Count; i++) 
                {
                    IRWrapper.IRAPI.IRControlGroup g = IRWrapper.IRController.ServoGroups [i];

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

                            foreach (IRWrapper.IRAPI.IRServo servo in g.Servos) 
                            {
                                GUILayout.BeginHorizontal ();
                                var avCommand = availableServoCommands.FirstOrDefault (t => t.servo == servo);

                                if (avCommand == null) 
                                {
                                    Logger.Log ("[Sequencer] Cannot find matching command for servo " + servo.Name, Logger.Level.Debug);
                                    return;
                                }
                                GUI.color = solidColor;

                                if (GUILayout.Button ("Add", buttonStyle, GUILayout.Width (30), GUILayout.Height (22))) 
                                {
                                    openSequence.commands.Add (new BasicCommand (avCommand));
                                }

                                GUILayout.Label (servo.Name, nameStyle, GUILayout.ExpandWidth (true), GUILayout.Height (22));

                                tmpString = GUILayout.TextField (string.Format ("{0:#0.0#}", avCommand.position), GUILayout.Width (40), GUILayout.Height (22));
                                if (float.TryParse (tmpString, out tmpValue)) 
                                {
                                    avCommand.position = tmpValue;
                                }
                                GUILayout.Label ("@", nameStyle, GUILayout.Height (22));
                                tmpString = GUILayout.TextField (string.Format ("{0:#0.0#}", avCommand.speedMultiplier), GUILayout.Width (30), GUILayout.Height (22));
                                if (float.TryParse (tmpString, out tmpValue)) 
                                {
                                    avCommand.speedMultiplier = Mathf.Clamp (tmpValue, 0.05f, 1000f);
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
                        var newCommand = new BasicCommand (a);
                        openSequence.commands.Add (newCommand);
                    }
                    GUILayout.Label ("Toggle AG: " + a.ToString(), GUILayout.ExpandWidth (true), GUILayout.Height (22));
                    GUI.color = opaqueColor;
                    GUILayout.EndHorizontal ();
                }
                GUILayout.EndScrollView ();
            }
            
            GUILayout.BeginHorizontal(GUILayout.Height(10));
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.scrollView);
            GUILayout.BeginHorizontal();
            GUI.color = solidColor;
            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(true, currentDelay);
                openSequence.commands.Add(newCommand);
            }

            GUILayout.Label("Delay for ", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0.0#}", currentDelay), GUILayout.Width(40), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentDelay = Mathf.Clamp(tmpValue, 0f, 600f);
            }
            GUILayout.Label("s ", nameStyle, GUILayout.Width(18), GUILayout.Height(22));
            
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(true);
                openSequence.commands.Add(newCommand);
            }
            GUILayout.Label("Wait for finish", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Add", buttonStyle, GUILayout.Width(30), GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(currentGotoIndex, currentGotoCounter);
                openSequence.commands.Add(newCommand);
            }

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Go To command #", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0}", currentGotoIndex+1), GUILayout.Width(30), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentGotoIndex = (int)Mathf.Clamp(tmpValue-1, 0f, openSequence.commands.Count-1);
            }
            
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Repeat ", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0}", currentGotoCounter), GUILayout.Width(30), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentGotoCounter = (int)Math.Max(tmpValue, -1);
            }
            
            GUILayout.Label(" times (-1 for loop).", nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
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

            //now begin listing commands in sequence
            for (int i = 0; i < openSequence.commands.Count; i++ )
            {
                BasicCommand bc = openSequence.commands[i];

                GUILayout.BeginHorizontal();
                GUI.color = solidColor;
                string commandStatus = (bc.isActive || openSequence.lastCommandIndex == i) ? "<color=lime>■</color>" : bc.isFinished ? "<color=green>■</color>" : "<color=silver>■</color>";
                GUILayout.Label(commandStatus, dotStyle, GUILayout.Width(20), GUILayout.Height(22));

                GUILayout.Label((i+1).ToString() + ":", dotStyle, GUILayout.Width(30), GUILayout.Height(22));

                var labelText = "";
                if (bc.wait)
                {
                    if (bc.waitTime > 0f)
                    {
                        if (bc.isActive)
                            labelText = "Waiting for " + Math.Round(bc.timeStarted + bc.waitTime - UnityEngine.Time.time, 2) + "s";
                        else
                            labelText = "Delay for " + Math.Round(bc.waitTime, 2) + "s";
                    }
                    else if (bc.gotoIndex != -1)
                    {
                        labelText = "Go to command # " + (bc.gotoIndex+1).ToString();
                        if (bc.gotoCounter != -1)
                        {
                            labelText += ", do this " + bc.gotoCounter + " times.";
                        }
                    }
                    else
                        labelText = "Wait";
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
                    GUILayout.Space(25);

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
                    GUILayout.Space(25);

                if (GUILayout.Button(TextureLoader.TrashIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                {
                   openSequence.commands.RemoveAt(i);
                }
                GUI.color = opaqueColor;
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();

            GUI.color = solidColor;
            if (GUILayout.Button("Start", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Start();
            }

            if (GUILayout.Button("Pause", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Pause();
            }

            if (GUILayout.Button("Reset", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Reset();
            }
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

        private void OnGUI()
        {

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
                SequencerWindowPos = GUILayout.Window(SequencerWindowID, SequencerWindowPos,
                SequencerControlWindow,
                "Servo Sequencer",
                GUILayout.Width(300),
                GUILayout.Height(80));

                if (guiSequenceEditor)
                {
                    float height = Screen.height / 2f;
                    if(openSequence != null)
                        SequencerEditorWindowPos = GUILayout.Window(SequencerEditorWindowID, SequencerEditorWindowPos,
                        SequencerEditorWindow,
                        "Sequence Editor: " + openSequence.name,
                        GUILayout.Width(620),
                        GUILayout.Height(height));
                }
            }
            GUI.color = solidColor;
            DrawTooltip();
        }
    }

    
}
