using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IRSequencer.API;
using IRSequencer.Gui;
using IRSequencer.Core;

namespace IRSequencer.Module
{

    /// <summary>
    /// Sequencer PartModule. Acts as a Finite State Machine.
    /// Also handles saving/loading of its own sequences and states (deprecates SequencerStorage)
    /// All the non-GUI Sequencer code should be moved here as well.
    /// </summary>
    public class ModuleSequencer : PartModule
    {
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "serializedSequences")]
        public string serializedSequences = "";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "serializedStates")]
        public string serializedStates = "";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "lastState")]
        public string lastStateID = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "IRS Locked?")]
        public bool isLocked = false;

        private float lastKeyPressedTime = 0f;
        private const float keyCooldown = 0.2f;

        internal bool loadPending = false;
        private float lastSavedUT = 0f;

        public SequencerState currentState;

        public List<SequencerState> states;

        public List<Sequence> sequences;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Seqeuncer Name")]
        public string sequencerName = "New Sequencer";

        const float POSDELTA = 0.001f;

        public override void OnAwake()
        {
            sequences = new List<Sequence>();
            sequences.Clear();

            states = new List<SequencerState> ();
            states.Clear ();

            var s = new SequencerState ();
            s.stateName = "Default";

            states.Add (s);

            currentState = states[0];
        }

        public override void OnStart(StartState state)
        {
            //load the current state on first FixedUpdate
            loadPending = true;

            if (sequences == null || states == null)
            {
                sequences = new List<Sequence>();
                sequences.Clear();

                states = new List<SequencerState> ();
                states.Clear ();

                var s = new SequencerState ();
                s.stateName = "Default";

                states.Add (s);

                currentState = states[0];

                Logger.Log("[Sequencer]: for some reason sequences/states is null in OnStart", Logger.Level.Debug);
            }

        }

        public void SaveData ()
        {
            //requires ServoGroups to be parsed
            if (!IRWrapper.APIReady)
                return;

            if (sequences == null || states == null)
                return;

            var message = "";
            foreach(Sequence s in sequences)
            {
                message += s.Serialize () + "$";
            }

            serializedSequences = message;

            message = "";

            foreach(SequencerState s in states)
            {
                message += s.Serialize () + "$";
            }

            serializedStates = message;

            if(currentState != null)
                lastStateID = currentState.stateID.ToString();

        }

        public void LoadData ()
        {
            //requires ServoGroups to be parsed
            if (!IRWrapper.APIReady)
            {
                //we tried loading, but the ServoController was not ready
                loadPending = true;
                return;
            }

            if (sequences == null || states == null)
                return;


            states.Clear ();
            sequences.Clear ();

            //first load the states

            var chunks = serializedStates.Split ('$');

            int counter = 0;
            foreach (string serializedState in chunks)
            {
                SequencerState s;
                if (TryParseState(serializedState, out s))
                {
                    states.Add(s);
                    counter++;
                }
            }

            //there should always be at least one state, a default state
            if(states.Count == 0)
            {
                var defState = new SequencerState ();
                defState.stateName = "Default";
                states.Add (defState);

                Logger.Log(string.Format("Failed loading States, creating Default State"), Logger.Level.Debug);
            }
            else
                Logger.Log(string.Format("Successfully Loaded {0} States", counter), Logger.Level.Debug);

            currentState = states.Find (x => x.stateID.ToString() == lastStateID);

            //if we could not find current state revert to first available state
            if(currentState == null)
            {
                currentState = states [0];
            }

            chunks = serializedSequences.Split ('$');

            counter = 0;
            foreach (string serializedSequence in chunks)
            {
                Sequence s;
                if (TryParseSequence(serializedSequence, out s))
                {
                    sequences.Add(s);
                    counter++;
                }
            }

            loadPending = false;
            Logger.Log(string.Format("Successfully Loaded {0} out of {1} Sequences", counter, chunks.Length), Logger.Level.Debug);
        }

        public bool TryParseState(string s, out SequencerState st)
        {
            if (s == "" || !s.Contains(":"))
            {
                Logger.Log("TryParseState, invalid format s=" + s, Logger.Level.Debug);
                st = null;
                return false;
            }
            var chunks = s.Split (':');
            if (chunks.Count () < 2)
            {
                st = null;
                return false;
            }
            st = new SequencerState (chunks[0]);
            st.stateName = chunks [1];

            return true;
        }

        public bool TryParseSequence(string s, out Sequence seq)
        {
            seq = new Sequence();

            if (s == "" || !s.Contains("<") || !s.Contains(">"))
            {
                seq = null;
                Logger.Log("TryParseSequence, invalid format s=" + s, Logger.Level.Debug);
                return false;
            }

            var allServos = new List<IRWrapper.IServo>();

            foreach (IRWrapper.IControlGroup g in IRWrapper.IRController.ServoGroups)
            {
                allServos.AddRange(g.Servos);
            }
            // name {:command1:command2}
            try
            {
                var seqName = s.Substring(0, s.IndexOf('<'));
                Logger.Log("TryParseSequence, seqName =" + seqName, Logger.Level.Debug);

                var t = seqName.Split('|');
                seq.name = t[0];

                if (t.Length > 1)
                {
                    if (!bool.TryParse(t[1], out seq.isLooped))
                    {
                        seq.isLooped = false;
                    }
                }

                if (t.Length > 2)
                {
                    seq.keyShortcut = t[2];
                }

                if(t.Length > 3)
                {
                    if (!bool.TryParse(t[3], out seq.autoStart))
                    {
                        seq.autoStart = false;
                    }
                }
                //now find startState and endState by IDs
                if(t.Length > 5)
                {
                    var startStateID = t[4];
                    var endStateID = t[5];

                    seq.startState = states.Find(st => st.stateID.ToString() == startStateID);
                    seq.endState = states.Find(st => st.stateID.ToString() == endStateID);
                }

                //sequencer must have at least one state, so if nothing found, default states to it
                if(seq.startState == null) 
                    seq.startState = states[0];

                if(seq.endState == null)
                    seq.endState = states[0];

                var seqCommands = s.Substring(s.IndexOf('<') + 1, s.IndexOf('>') - seqName.Length - 1);

                Logger.Log("TryParseSequence, seqCommands=" + seqCommands, Logger.Level.Debug);

                var chunks = seqCommands.Split(':');

                Logger.Log("TryParseSequence, chunks.length=" + chunks.Length, Logger.Level.Debug);
                for (int i = 0; i < chunks.Length; i++ )
                {
                    BasicCommand bc;
                    if (TryParseBasicCommand(chunks[i], allServos, out bc))
                    {
                        seq.commands.Add(bc);
                    }
                }

            }
            catch (Exception e)
            {
                Logger.Log("TryParseSequence, string=" + s + ", Exception: " + e.Message, Logger.Level.Debug);
            }
            return true;
        }

        public bool TryParseBasicCommand(string s, List<IRWrapper.IServo> allServos, out BasicCommand bc)
        {
            var chunks = s.Split ('|');

            if (chunks.Length < 8)
            {
                Logger.Log("Invalid number of chunks in BasicCommand string: " + s, Logger.Level.Debug);
                bc = null;
                return false;
            }

            bc = new BasicCommand(false);

            if (chunks[0] != "null")
            {
                uint servoUID;
                if (!uint.TryParse (chunks [0], out servoUID)) 
                {
                    bc = null;
                    return false;
                }
                else
                {
                    bc.servo = allServos.Find(p => p.UID == servoUID);
                    if (bc.servo == null)
                    {
                        bc = null;
                        return false;
                    }
                }

                if (!float.TryParse(chunks[1], out bc.position))
                {
                    bc = null;
                    return false;
                }

                if (!float.TryParse(chunks[2], out bc.speedMultiplier))
                {
                    bc = null;
                    return false;
                }
            }

            if (!bool.TryParse(chunks[3], out bc.wait))
            {
                bc = null;
                return false;
            }

            if (!float.TryParse(chunks[4], out bc.waitTime))
            {
                bc = null;
                return false;
            }

            if (!int.TryParse(chunks[5], out bc.gotoIndex))
            {
                bc = null;
                return false;
            }

            if (!int.TryParse (chunks [6], out bc.gotoCommandCounter)) 
            {
                bc = null;
                return false;
            } 
            else
                bc.gotoCounter = bc.gotoCommandCounter;

            int temp = 0;
            if (!int.TryParse (chunks [7], out temp)) 
            {
                bc = null;
                return false;
            } 
            else
                bc.ag = (KSPActionGroup)temp;

            //Add agX support
            if (chunks.Length > 8)
            {
                temp = -1;
                if (!int.TryParse(chunks[8], out temp))
                {
                    bc = null;
                    return false;
                }
                else
                    bc.agX = temp;
            }

            Logger.Log("Successfully parsed BasicCommand, bc.gotoIndex = " + bc.gotoIndex + ", bc.ag=" + bc.ag);

            return true;
        }

        protected bool KeyPressed(string key)
        {
            return (key != "" && InputLockManager.IsUnlocked(ControlTypes.LINEAR) && Input.GetKey(key));
        }

        protected bool KeyUnPressed(string key)
        {
            return (key != "" && InputLockManager.IsUnlocked(ControlTypes.LINEAR) && Input.GetKeyUp(key));
        }

        protected void CheckInputs()
        {
            //do sanity checks and halt if locked
            if (sequences == null || isLocked)
                return;


            bool keyTriggered = false;
            for(int i=0; i<sequences.Count; i++)
            {
                var s = sequences [i];
                if(KeyPressed(s.keyShortcut))
                {
                    if (Time.time > lastKeyPressedTime  + keyCooldown) 
                    {
                        if (s.isActive) 
                            s.Pause ();
                        else
                            s.Start (currentState);

                        keyTriggered = true;
                    }
                }
            }

            if (keyTriggered)
                lastKeyPressedTime = Time.time;
        }

        public void Update()
        {
            CheckInputs ();
        }

        //the following 3 functions are here to provide some utility for possible hook-ins from other mods.
        public void StartSequence(Guid sID)
        {
            if (sequences == null)
                return;

            var s = sequences.Find (sq => sq.sequenceID == sID);

            if (s == null)
                return;

            s.Start (currentState);
        }

        public void PauseSequence(Guid sID)
        {
            if (sequences == null)
                return;

            var s = sequences.Find (sq => sq.sequenceID == sID);

            if (s == null)
                return;

            s.Pause ();
        }

        public void ResetSequence(Guid sID)
        {
            if (sequences == null)
                return;

            var s = sequences.Find (sq => sq.sequenceID == sID);

            if (s == null)
                return;

            s.Reset ();
        }

        /// <summary>
        /// Main heartbeat loop.
        /// </summary>
        private void FixedUpdate()
        {

            //no need to run for non-robotic crafts or if disabled
            if (!isEnabled || sequences == null)
                return;

            //only autosave every second
            //if (Time.time < lastSavedUT + 0.2f)
            //    return;

            if (loadPending)
            {
                LoadData();
            }
            else if (Time.time > lastSavedUT + 0.2f)
            {
                SaveData();

                lastSavedUT = Time.time;
            }

            //if the sequencer is locked, there is no need to process any sequences.
            if (isLocked)
                return;

            var activeSequences = sequences.FindAll(s => s.isActive);

            if (activeSequences.Count == 0) 
            {
                //unlock all sequences as there are none active
                sequences.ForEach (((Sequence s) => s.isLocked = false));
            }

            if(activeSequences.Count(s => s.endState != s.startState) == 0)
            {
                //the only sequences that run are ones that do not change the state, so we can unlock all sequences
                sequences.ForEach (((Sequence s) => s.isLocked = false));
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

                //in addition lock all other sequeces that change State
                if(sq.endState != sq.startState)
                {
                    sequences.FindAll (s => s.endState != s.startState).ForEach ((Sequence seq) => seq.isLocked = true);
                    sq.isLocked = false;
                }

                var activeCommands = sq.commands.FindAll(s => s.isActive);
                var activeCount = activeCommands.Count;

                foreach (BasicCommand bc in activeCommands)
                {
                    if (bc.wait)
                    {
                        if (bc.ag != KSPActionGroup.None)
                        {
                            //we should wait until ActionGroup is executed
                            if(HighLogic.LoadedSceneIsFlight)
                            {
                                if (FlightGlobals.ActiveVessel != null)
                                {
                                    if(FlightGlobals.ActiveVessel.ActionGroups[bc.ag])
                                    {
                                        Logger.Log("[Sequencer] ActionGroup wait finished, AG fired was " + bc.ag.ToString(), Logger.Level.Debug);
                                        bc.isFinished = true;
                                        bc.isActive = false;
                                        activeCount--;
                                    }
                                }
                            }
                            else
                            {
                                Logger.Log("[Sequencer] ActionGroup wait auto-finished in Editor", Logger.Level.Debug);
                                bc.isFinished = true;
                                bc.isActive = false;
                                activeCount--;
                            }
                        }
                        else if (bc.agX > -1)
                        {
                            //we should wait until ActionGroupExtended is executed
                            if (HighLogic.LoadedSceneIsFlight && ActionGroupsExtendedAPI.Instance.Installed())
                            {
                                if (FlightGlobals.ActiveVessel != null)
                                {
                                    if (ActionGroupsExtendedAPI.Instance.GetGroupState(FlightGlobals.ActiveVessel, bc.agX))
                                    {
                                        Logger.Log("[Sequencer] ActionGroup wait finished, AG fired was " + ActionGroupsExtendedAPI.Instance.GetGroupName(bc.agX), Logger.Level.Debug);
                                        bc.isFinished = true;
                                        bc.isActive = false;
                                        activeCount--;
                                    }
                                }
                            }
                            else
                            {
                                Logger.Log("[Sequencer] ActionGroup wait auto-finished in Editor", Logger.Level.Debug);
                                bc.isFinished = true;
                                bc.isActive = false;
                                activeCount--;
                            }
                        }
                        else if (bc.waitTime > 0f)
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
                    else if (Math.Abs (bc.servo.Position - bc.position) <= POSDELTA)
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
                        sq.Start(currentState);
                    }
                    else 
                    { 
                        //there are no more commands in the sequence left to execute
                        if (sq.isLooped)
                        {
                            Logger.Log("[Sequencer] Looping sequence " + sq.name, Logger.Level.Debug);
                            sq.Reset();
                            sq.Start(currentState);
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
                        if (sq.commands[sq.lastCommandIndex].wait && 
                            sq.commands[sq.lastCommandIndex].waitTime == 0f && 
                            sq.commands[sq.lastCommandIndex].ag == KSPActionGroup.None &&
                            sq.commands[sq.lastCommandIndex].agX == -1)
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
                                    sq.Start(currentState);
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

            //now we need to look through all the sequences and if there are some that are Finished 
            //we need to change the currentState accordingly and reset the sequence
            foreach (Sequence sq in sequences)
            {
                if(sq.isFinished && sq.endState != null && sq.startState != sq.endState)
                {
                    var oldState = currentState;
                    currentState = sq.endState;
                    //reset the sequence to the original state
                    sq.Reset();
                    Logger.Log ("[ModuleSequencer] Sequence " + sq.name + " finished, changing current state to " + sq.endState.stateName, Logger.Level.Debug);

                    //now we need to process OnStateChange events
                    OnStateChange(oldState, currentState);
                }
            }
        }

        public void OnStateChange(SequencerState oldState, SequencerState newState)
        {
            Logger.Log ("[ModuleSequencer] OnStateChange from " + oldState.stateName + "  to " + newState.stateName + " starting.", Logger.Level.Debug);

            foreach (Sequence sq in sequences) 
            {
                //first we need to stop/reset all active sequences which startState is not newState
                if(sq.isActive && sq.startState != null && sq.startState != newState)
                {
                    sq.Pause ();
                    sq.Reset ();
                    Logger.Log ("[ModuleSequencer] OnStateChange stopping sequence " + sq.name, Logger.Level.Debug);
                }

                //now we need to unlock previously locked sequences
                var activeSequences = sequences.FindAll(s => s.isActive);

                if (activeSequences.Count == 0) 
                {
                    //unlock all sequences as there are none active
                    sequences.ForEach (((Sequence s) => s.isLocked = false));
                }

                if(activeSequences.Count(s => s.endState != s.startState) == 0)
                {
                    //the only sequences that run are ones that do not change the state, so we can unlock all sequences
                    sequences.ForEach (((Sequence s) => s.isLocked = false));
                }

                //check for sequences in AutoStart mode and start the ones that should trigger when we enter newState
                if(!sq.isActive && sq.autoStart && sq.startState != null && sq.startState == newState && !sq.isLocked)
                {
                    sq.Start (currentState);

                    Logger.Log ("[ModuleSequencer] OnStateChange starting sequence " + sq.name + " due to AutoStart flag.", Logger.Level.Debug);
                }
            }

            Logger.Log ("[ModuleSequencer] OnStateChange from " + oldState.stateName + "  to " + newState.stateName + " complete.", Logger.Level.Debug);

        }

        public void LockSequencer()
        {
            //we need to stop all running sequences and lock them
            foreach (Sequence sq in sequences) 
            {
                sq.Pause ();
                sq.Reset ();
                sq.isLocked = true;
            }

            isLocked = true;
        }

        public void UnlockSequencer ()
        {
            //upon unlocking sequencer should initiate state change to lastState to trigger any auto-starting sequences
            //there must be at least one state (by definition)
            if(currentState == null)
            {
                currentState = states [0];
            }
            //feels a bit hacky, but it should work;
            //it will unlock all the sequences as a side effect.
            OnStateChange (currentState, currentState);

            isLocked = false;
        }

        [KSPAction("Toggle Sequencer Lock")]
        public void ToggleSequencerLock(KSPActionParam param)
        {
            if(isLocked)
            {
                UnlockSequencer ();
            }
            else
            {
                LockSequencer ();
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad (node);

            //LoadData ();
            //to ensure everything loads properly load data on the next FixedUpdate.
            loadPending = true;
        }

        /*public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            SaveData ();
        }*/

        public override void OnInitialize ()
        {
            base.OnInitialize ();

            loadPending = true;
        }

        public override string GetInfo()
        {
            string moduleInfo = "Sequencer with an integrated storage module for sequences and built-in Finite State Machine";

            return moduleInfo;
        }

    }
}

