using System;
using System.Collections.Generic;

namespace IRSequencer.Core
{
    /// <summary>
    /// Implements a Sequence of BasicCommands. 
    /// Each Sequence must start and end at a certain SequencerState (could be the same one).
    /// As Sequence is supposed to operate only for ActiveVessel there is no link to a vessel at this level.
    /// </summary>
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
        public string keyShortcut = "";
        public readonly Guid sequenceID;

        public SequencerState startState, endState;

        public bool autoStart = false;

        public bool IsPaused { 
            get 
            {
                if (commands == null || lastCommandIndex < 0) return false;
                else
                {
                    if (lastCommandIndex >= commands.Count) 
                        return false;
                    return (!commands[lastCommandIndex].isActive); 
                }
            } 

        }

        public Sequence ()
        {
            commands = new List<BasicCommand>();
            name = "New Sequence";
            sequenceID = Guid.NewGuid ();
        }

        public Sequence(string newID) : this()
        {
            sequenceID = new Guid (newID);
        }

        public Sequence (BasicCommand b) : this()
        {
            commands.Add(b);
        }

        public Sequence (Sequence baseSequence) :this()
        {
            //commands.AddRange(baseSequence.commands);
            baseSequence.commands.ForEach ((BasicCommand bc) => commands.Add (new BasicCommand (bc)));
            name = "Copy of " + baseSequence.name;
            startState = baseSequence.startState;
            endState = baseSequence.endState;
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
            //if the sequence is marked as Finished - reset it and start anew.
            if (isFinished)
                Reset();

            isActive = true;

            //find first unfinished command
            lastCommandIndex = commands.FindIndex(s => s.isFinished == false);
            Logger.Log("[Sequencer] First unfinished Index = " + lastCommandIndex, Logger.Level.Debug);
            if (lastCommandIndex == -1)
            {
                //there are no unfinished commands, loop if needed or SetFinished and exit
                if(isLooped)
                {
                    Reset();
                }
                else
                {
                    SetFinished();
                    return;
                }
            }
            //now we can start/continue execution
            //we execute commands until first wait command

            Resume(lastCommandIndex);

            Logger.Log("[Sequencer] Sequence Start finished, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
            //else we are either finished, or most likely waiting for commands to finish.
        }

        public void Step()
        {
            Logger.Log("[Sequencer] Sequence next step", Logger.Level.Debug);

            if (commands == null) return;

            if (isLocked)
            {
                Logger.Log ("[Sequencer] Cannot step sequence " + name + " as it is Locked", Logger.Level.Debug);
                return;
            }

            //find first unfinished command
            lastCommandIndex = commands.FindIndex(s => s.isFinished == false);
            Logger.Log("[Sequencer] First unfinished Index = " + lastCommandIndex, Logger.Level.Debug);

            if (lastCommandIndex == -1)
            {
                //there are no unfinished commands, loop if needed or SetFinished and exit
                if(isLooped)
                {
                    Reset();
                    lastCommandIndex = 0;
                }
                else
                {
                    SetFinished();
                    return;
                }
            }

            if (lastCommandIndex < commands.Count)
            {
                //execute one command and mark it as finished immidiately.
                commands[lastCommandIndex].Execute();
                commands [lastCommandIndex].isActive = false;
                commands [lastCommandIndex].isFinished = true;
                lastCommandIndex++;
                isWaiting = true;
                isActive = false;
                Logger.Log("[Sequencer] Sequence is waiting for next step, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
            }

            Logger.Log("[Sequencer] Sequence Step finished, lastCommandIndex = " + lastCommandIndex, Logger.Level.Debug);
            //else we are either finished, or most likely waiting for commands to finish.
        }

        public void Pause()
        {
            if (commands == null) return;

            // find the first Active command and stop it and everything that is after
            lastCommandIndex = commands.FindIndex(s => s.isActive);

            // if there are no Active commands - set the LastCommandIndex to last finished command + 1
            if (lastCommandIndex == -1)
                lastCommandIndex = commands.FindLastIndex(s => s.isFinished) + 1;

            //now we need to stop all the commands with index >= lastCommandIndex
            for (int i = lastCommandIndex; i < commands.Count; i++)
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

        public string Serialize()
        {
            var serializedSequence = name.Replace('<',' ').Replace('>',' ').Replace('|',' ') + "|" 
                + isLooped + "|" + keyShortcut.Replace ("<", "").Replace (">", "").Replace ("|", "") + "|" + autoStart;

            //begin states block
            if(startState == null || endState == null)
            {
                //somehow we got legacy Sequeces to serialize, just post an Error
                Logger.Log("In Sequencer 1.0 and higher Sequences must have non-empty startState and endState", Logger.Level.Warning);

            }
            else
            {
                serializedSequence += "|" + startState.stateID + "|" + endState.stateID;
            }
            //end states block

            //begin commands block;
            serializedSequence += "<";

            if (commands == null)
                return serializedSequence + ">";

            foreach (BasicCommand bc in commands)
            {
                serializedSequence += ":" + bc.Serialize ();
            }

            serializedSequence += ">";

            return serializedSequence;
        }
    }

}

