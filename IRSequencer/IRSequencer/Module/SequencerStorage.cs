﻿using System;
using System.Collections.Generic;
using UnityEngine;
using IRSequencer.API;
using IRSequencer.Gui;
using IRSequencer.Core;

namespace IRSequencer.Module
{
    public class SequencerStorage : PartModule
    {
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "serializedSequences")]
        public string serializedSequences = "";

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Save Sequences")]
        public void SaveSequencesEvent ()
        {
            if (Sequencer.Instance)
            {
                var message = "";
                foreach(Sequence s in Sequencer.Instance.sequences)
                {
                    message += s.Serialize () + "$";
                }

                //ScreenMessages.PostScreenMessage (message);

                serializedSequences = message;
                Logger.Log(string.Format("Successfully Saved {0} Sequences", Sequencer.Instance.sequences.Count), Logger.Level.Debug);
                //ScreenMessages.PostScreenMessage(string.Format("Successfully Saved {0} Sequences", Sequencer.Instance.sequences.Count), 3, ScreenMessageStyle.UPPER_CENTER);
            }
            else 
            {
                //ScreenMessages.PostScreenMessage("Sequencer is not Initialised", 3, ScreenMessageStyle.UPPER_CENTER);
            }
            
        }

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Load Sequences")]
        public void LoadSequencesEvent ()
        {

            if (Sequencer.Instance == null)
                return;

            if (Sequencer.Instance.sequences == null)
                return;

            Sequencer.Instance.sequences.Clear ();

            var chunks = serializedSequences.Split ('$');

            int counter = 0;
            foreach (string serializedSequence in chunks)
            {
                Sequence s;
                if (TryParseSequence(serializedSequence, out s))
                {
                    Sequencer.Instance.sequences.Add(s);
                    counter++;
                }
            }
            Logger.Log(string.Format("Successfully Loaded {0} Sequences", counter), Logger.Level.Debug);
            //ScreenMessages.PostScreenMessage(string.Format("Successfully Loaded {0} Sequences", counter), 3, ScreenMessageStyle.UPPER_CENTER);
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

            Logger.Log("Successfully parsed BasicCommand, bc.gotoIndex = " + bc.gotoIndex + ", bc.ag=" + bc.ag);

            return true;
        }

        public SequencerStorage ()
        {
        }

        /*private void FixedUpdate()
        {
            if (Sequencer.Instance == null)
                return;

            if (Sequencer.Instance.sequences == null)
                return;


        }*/

        //returns basic information on kOSProcessor module in Editor
        public override string GetInfo()
        {
            string moduleInfo = "Provides Storage for Seqences";

            return moduleInfo;
        }

        public override void OnStart(StartState state)
        {
            
            if (state == StartState.Editor)
            {
                return;
            }

            //only load sequences for active craft
            if (vessel == FlightGlobals.ActiveVessel)            
                LoadSequencesEvent();

            Logger.Log(string.Format("OnStart: {0}", state), Logger.Level.Debug);
        }

        public override void OnSave(ConfigNode node)
        {
            try
            {
                //if (vessel == FlightGlobals.ActiveVessel)
                //    SaveSequencesEvent();

                base.OnSave(node);

            }
            catch (Exception ex) //Intentional Pokemon, if exceptions get out of here it can kill the craft
            {
                Logger.Log("ONSAVE Exception: " + ex.TargetSite);
            }

            Logger.Log("[SeqeuncerStorage] OnSave finished.");
        }

    }
}

