using System;
using System.Collections.Generic;
using IRSequencer.API;

namespace IRSequencer.Core
{
    public class BasicCommand
    {
        internal IRWrapper.IServo servo;
        public float timeStarted;
        public bool wait;
        public float waitTime=0f;
        public float position;
        public float speedMultiplier;
        public bool isActive = false;
        public bool isFinished = false;
        public KSPActionGroup ag = KSPActionGroup.None;
        public int agX = -1;
        public int gotoIndex = -1;
        public int gotoCounter = -1;
        public int gotoCommandCounter = -1;

        public BasicCommand(IRWrapper.IServo s, float p, float sp)
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

        public BasicCommand(int x) : this(false)
        {
            agX = x;
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

            if (wait)
            {
                //wait commands are dealt with separately in FixedUpdate
                //all we had to do is mark it as Active and set Timestamp, as we did above
            }
            else if (ag != KSPActionGroup.None)
            {
                if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
                {
                    FlightGlobals.ActiveVessel.ActionGroups.ToggleGroup (ag);
                }
                isActive = false;
                isFinished = true;
                Logger.Log("[Sequencer] Firing ActionGroup = " + ag.ToString(), Logger.Level.Debug);
            }
            else if (agX > -1)
            {
                if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null 
                    && ActionGroupsExtendedAPI.Instance != null && ActionGroupsExtendedAPI.Instance.Installed())
                {
                    var curState = ActionGroupsExtendedAPI.Instance.GetGroupState(FlightGlobals.ActiveVessel, agX);
                    ActionGroupsExtendedAPI.Instance.ActivateGroup(FlightGlobals.ActiveVessel, agX, !curState);
                }
                isActive = false;
                isFinished = true;
                Logger.Log("[Sequencer] Firing ActionGroup = " + ag.ToString(), Logger.Level.Debug);
            }
            else if (servo != null)
            {
                Logger.Log("[Sequencer] Executing command, servoName= " + servo.Name + ", pos=" + position, Logger.Level.Debug);
                servo.MoveTo(position, speedMultiplier);
            }
        }

        public void Stop()
        {
            timeStarted = 0f;
            gotoCounter = gotoCommandCounter;

            isActive = false;
            isFinished = false;

            if (wait)
                return;
            else if (servo != null)
            {
                servo.Stop();
            }

        }

        public string Serialize()
        {
            var serializedCommand = "";

            if (servo == null)
            {
                serializedCommand += "null|0|0|";
            }
            else
            {
                serializedCommand += servo.UID + "|" + position + "|" + speedMultiplier + "|";
            }
            serializedCommand += wait + "|" + waitTime + "|";
            serializedCommand += gotoIndex + "|" + gotoCommandCounter + "|";
            serializedCommand += (int)ag + "|";
            serializedCommand += agX;

            return serializedCommand;
        }

    }

}

