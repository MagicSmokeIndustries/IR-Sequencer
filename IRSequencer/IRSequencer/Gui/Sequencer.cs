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

        public bool GUIEnabled = true;
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

        private float currentSpeedMultiplier = 1.0f;
        private float currentDelay = 1.0f;
        private float currentPosition = 0f;


        protected static Rect SequencerWindowPos;
        protected static Rect SequencerEditorWindowPos;
        protected static int SequencerWindowID;
        protected static int SequencerEditorWindowID;

        protected static Vector2 servoListScroll;
        protected static Vector2 commandListScroll;

        protected static Sequencer SequencerInstance;

        public bool SequencerReady {get { return isReady;}}

        public static Sequencer Instance
        {
            get { return SequencerInstance; }
        }

        private List<Sequence> sequences;

        private Sequence openSequence;
        private IRWrapper.IRAPI.IRServo activeServo;

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

            public void Execute()
            {
                isActive = true;
                timeStarted = UnityEngine.Time.time;

                if (wait)
                {
                    //do nothing, we should not get here ever
                    //Logger.Log("[Sequencer] Error, should not execute wait command", Logger.Level.Debug);
                }
                else
                {
                    Logger.Log("[Sequencer] Executing command, servoName= " + servo.Name + ", pos=" + position, Logger.Level.Debug);
                    servo.MoveTo(position, speedMultiplier);
                }
            }

            public void Stop()
            {
                if (wait)
                    return;
                else if (servo != null)
                {
                    servo.Stop();
                }

                isActive = false;
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

            public void Start()
            {
                Logger.Log("[Sequencer] Sequence started", Logger.Level.Debug);

                if (commands == null) return;

                isActive = true;

                //find first unfinished command
                lastCommandIndex = commands.IndexOf(commands.Find(s => s.isFinished == false));
                Logger.Log("[Sequencer] First unfinished Index = " + lastCommandIndex, Logger.Level.Debug);
                if (lastCommandIndex == -1)
                    return;

                //now we can start/continue execution
                //we execute commands until first wait command

                var nextWaitCommandIndex = commands.FindIndex(lastCommandIndex, s => s.wait == true);
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
            }
        }

        /// <summary>
        ///     Load the textures from files to memory
        /// </summary>
        private static void InitTextures()
        {
            if (!GUISetupDone)
            {
                TextureLoader.InitTextures();
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

            var activeSequences = sequences.FindAll(s => s.isActive == true);
            if (activeSequences.Count == 0) 
                return;

            foreach (Sequence sq in activeSequences)
            {
                if (sq.commands == null) continue;

                var activeCommands = sq.commands.FindAll(s => s.isActive == true);
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

                if (sq.isWaiting)
                {
                    if (sq.commands[sq.lastCommandIndex].wait && sq.commands[sq.lastCommandIndex].waitTime == 0f)
                        activeCount--;

                    if (activeCount <= 0)
                    {
                        //sequence was waiting for all active commands to finish to proceed further
                        // we should have only wait command still active
                        //set wait command to finished
                        Logger.Log("[Sequencer] Waiting finished, resuming sequence: " + sq.name, Logger.Level.Debug);
                        sq.commands[sq.lastCommandIndex].isFinished = true;
                        sq.commands[sq.lastCommandIndex].isActive = false;
                        sq.isWaiting = false;
                        sq.Start();
                    }
                    
                }
                else
                {
                    if (activeCount <= 0)
                    {
                        Logger.Log("[Sequencer] Finished sequence " + sq.name, Logger.Level.Debug);
                        if (sq.isLooped)
                        {
                            sq.Reset();
                            sq.Start();
                        }
                        else
                            sq.SetFinished();
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

            InitTextures();

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
                var tooltipStyle = new GUIStyle
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal =
                    {
                        textColor = new Color32(207, 207, 207, 255),
                        background = TextureLoader.EditorBackgroundText
                    },
                    stretchHeight = true,
                    border = new RectOffset(3, 3, 3, 3),
                    padding = new RectOffset(4, 4, 6, 4),
                    alignment = TextAnchor.MiddleLeft
                };

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
            
            var buttonStyle = new GUIStyle(GUI.skin.button);
            var padding2px = new RectOffset(2, 2, 2, 2);

            buttonStyle.padding = padding2px;
            buttonStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();

            GUILayout.Label("Sequence Name", GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.Label("Controls", GUILayout.Width(250), GUILayout.Height(22));

            GUILayout.EndHorizontal();

            for (int i = 0; i < sequences.Count; i++)
            {
                //list through all sequences
                var sq = sequences[i];
                GUILayout.BeginHorizontal();
                sq.name = GUILayout.TextField(sq.name, GUILayout.ExpandWidth(true), GUILayout.Height(22));

                if (GUILayout.Button("Run", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    sq.Start();
                }

                if (GUILayout.Button("Pause", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    sq.Pause();
                }

                if (GUILayout.Button("Reset", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    sq.Reset();
                }

                if (GUILayout.Button("Edit", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    openSequence = sq;
                    guiSequenceEditor = !guiSequenceEditor;
                }

                if (GUILayout.Button("Clone", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    sequences.Add(new Sequence(sq));
                }

                if (GUILayout.Button("Del", buttonStyle, GUILayout.Width(40), GUILayout.Height(22)))
                {
                    sq.Pause();
                    sq.Reset();
                    sequences.RemoveAt(i);
                    return;
                }

                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Add new", buttonStyle, GUILayout.Height(22)))
            {
                sequences.Add(new Sequence());
            }
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

            Vector2 mousePos = Event.current.mousePosition;

            var buttonStyle = new GUIStyle(GUI.skin.button);
            var padding2px = new RectOffset(2, 2, 2, 2);

            buttonStyle.padding = padding2px;
            buttonStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayoutOption maxHeight = GUILayout.MaxHeight(Screen.height * 0.5f);

            servoListScroll = GUILayout.BeginScrollView(servoListScroll, false, false, maxHeight);
            GUILayout.BeginVertical(GUILayout.Width(200));
            
            List<IRWrapper.IRAPI.IRServo> allServos = new List<IRWrapper.IRAPI.IRServo>();

            foreach(IRWrapper.IRAPI.IRControlGroup g in IRWrapper.IRController.ServoGroups)
            {
                allServos.AddRange(g.Servos);
            }

            if (activeServo == null)
            {
                activeServo = allServos[0];
                currentPosition = activeServo.Position;
            }

            for (int i = 0; i < IRWrapper.IRController.ServoGroups.Count; i++)
            {
                IRWrapper.IRAPI.IRControlGroup g = IRWrapper.IRController.ServoGroups[i];

                if (g.Servos.Any())
                {
                    GUILayout.BeginHorizontal();

                    if (g.Expanded)
                    {
                        g.Expanded = !GUILayout.Button(TextureLoader.CollapseIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22));
                    }
                    else
                    {
                        g.Expanded = GUILayout.Button(TextureLoader.ExpandIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22));
                    }

                    //overload default GUIStyle with bold font
                    var t = new GUIStyle(GUI.skin.label.name)
                    {
                        fontStyle = FontStyle.Bold
                    };

                    GUILayout.Label(g.Name, t, GUILayout.ExpandWidth(true), GUILayout.Height(22));

                    GUILayout.EndHorizontal();

                    if (g.Expanded)
                    {
                        GUILayout.BeginHorizontal(GUILayout.Height(5));
                        GUILayout.EndHorizontal();

                        foreach (IRWrapper.IRAPI.IRServo servo in g.Servos)
                        {
                            GUILayout.BeginHorizontal();

                            if (GUILayout.Button(new GUIContent(TextureLoader.RightIcon, "Select"), buttonStyle, GUILayout.Width(18), GUILayout.Height(22)))
                            {
                                activeServo = servo;
                                currentPosition = activeServo.Position;
                                return; //redraw gui window
                            }
                            SetTooltipText();

                            var nameStyle = new GUIStyle(GUI.skin.label)
                            {
                                alignment = TextAnchor.MiddleLeft,
                                clipping = TextClipping.Clip
                            };

                            GUILayout.Label(servo.Name, nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22));
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.BeginHorizontal(GUILayout.Height(5));
                        GUILayout.EndHorizontal();
                    }
                    
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            GUILayout.Label("Selected servo: " + activeServo.Name, GUILayout.ExpandWidth(true), GUILayout.Height(22));
            Rect last = GUILayoutUtility.GetLastRect();
            Vector2 pos = Event.current.mousePosition;
            bool highlight = last.Contains(pos);
            activeServo.Highlight = highlight;

            GUILayout.EndHorizontal();
            /*GUILayout.BeginHorizontal();

            GUILayout.Label("Presets:", GUILayout.Width(50), GUILayout.Height(22));
            foreach (float p in activeServo.PresetPositions)
            {
                if (GUILayout.Button(p.ToString(), buttonStyle, GUILayout.Height(22)))
                {
                    var newCommand = new BasicCommand(activeServo, p, activeServo.speedTweak * currentSpeedMultiplier);
                    openSequence.commands.Add(newCommand);
                }
            }
            GUILayout.EndHorizontal();*/
            GUILayout.BeginHorizontal();

            GUILayout.Label("Position:", GUILayout.Width(50), GUILayout.Height(22));
            string tmpString;
            float tmpValue;
            tmpString = GUILayout.TextField(string.Format("{0:#0.0#}", currentPosition), GUILayout.Width(40), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentPosition = tmpValue;
            }

            GUILayout.Label("Speed:", GUILayout.Width(50), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0.0#}", currentSpeedMultiplier), GUILayout.Width(40), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentSpeedMultiplier = Mathf.Clamp(tmpValue, 0.05f, 1000f);
            }

            if (GUILayout.Button("Add Command", buttonStyle, GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(activeServo, currentPosition, activeServo.Speed * currentSpeedMultiplier);
                openSequence.commands.Add(newCommand);
            }
            
            
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            GUILayout.Label("Delay:", GUILayout.Width(50), GUILayout.Height(22));
            tmpString = GUILayout.TextField(string.Format("{0:#0.0#}", currentDelay), GUILayout.Width(40), GUILayout.Height(22));
            if (float.TryParse(tmpString, out tmpValue))
            {
                currentDelay = Mathf.Clamp(tmpValue, 0f, 600f);
            }
            if (GUILayout.Button("Add Delay", buttonStyle, GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(true, currentDelay);
                openSequence.commands.Add(newCommand);
            }

            if (GUILayout.Button("Add Wait", buttonStyle, GUILayout.Height(22)))
            {
                var newCommand = new BasicCommand(true);
                openSequence.commands.Add(newCommand);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(10));
            GUILayout.EndHorizontal();

            commandListScroll = GUILayout.BeginScrollView(commandListScroll, false, false, maxHeight);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Commands:", GUILayout.ExpandWidth(true), GUILayout.Height(22));
            GUILayout.EndHorizontal();

            //now begin listing commands in sequence
            for (int i = 0; i < openSequence.commands.Count; i++ )
            {
                BasicCommand bc = openSequence.commands[i];

                GUILayout.BeginHorizontal();

                var dotStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };

                string commandStatus = (bc.isActive || openSequence.lastCommandIndex == i) ? "<color=lime>■</color>" : bc.isFinished ? "<color=green>■</color>" : "<color=silver>■</color>";
                GUILayout.Label(commandStatus, dotStyle, GUILayout.Width(20), GUILayout.Height(22));

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
                    else 
                        labelText = "Wait";
                }
                else if (bc.servo != null) 
                    labelText = bc.servo.Name + " to " + Math.Round(bc.position, 2).ToString() + " at " + Math.Round(bc.speedMultiplier, 2).ToString() + "x";

                GUILayout.Label(labelText, GUILayout.ExpandWidth(true), GUILayout.Height(22));

                if (i > 0)
                {
                    if (GUILayout.Button(TextureLoader.UpIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                    {
                        openSequence.Pause();
                        var tmp = openSequence.commands[i - 1];
                        openSequence.commands[i-1] = bc;
                        openSequence.commands[i] = tmp;
                        openSequence.Reset();
                    }
                }

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
                if (GUILayout.Button(TextureLoader.TrashIcon, buttonStyle, GUILayout.Width(20), GUILayout.Height(22)))
                {
                   openSequence.commands.RemoveAt(i);
                   return;
                }
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Reset();
            }

            if (GUILayout.Button("Start", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Start();
            }

            if (GUILayout.Button("Pause", buttonStyle, GUILayout.Height(22)))
            {
                openSequence.Pause();
            }

            openSequence.isLooped = GUILayout.Toggle(openSequence.isLooped, "Loop", buttonStyle, GUILayout.Width(40), GUILayout.Height(22));

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
            
            if (GUIEnabled && !guiHidden)
            {
                SequencerWindowPos = GUILayout.Window(SequencerWindowID, SequencerWindowPos,
                SequencerControlWindow,
                "IR Sequencer",
                GUILayout.Width(400),
                GUILayout.Height(80));

                if (guiSequenceEditor)
                {
                    float height = Screen.height / 2f;
                    SequencerEditorWindowPos = GUILayout.Window(SequencerEditorWindowID, SequencerEditorWindowPos,
                    SequencerEditorWindow,
                    "Edit Sequence",
                    GUILayout.Width(600),
                    GUILayout.Height(height));
                }
            }

            DrawTooltip();
        }
    }

    
}
