using System;
using System.Collections.Generic;

namespace IRSequencer.Core
{
    /// <summary>
    /// Implements State as in Finite State Machine for Sequencer. 
    /// Each Sequence is supposed to begin and end at a certain State  (could be the same State).
    /// Ideally a Vessel could have multiple Sequencers and thus multiple States, but for now we assume it has one.
    /// This will be remedied when we tie Sequencer to ModuleSequencer and allow multiple sequencers per vessel.
    /// Each Vessel/Sequencer is supposed to have at least one SequencerState (Default/Idle).
    /// </summary>
    public class SequencerState
    {

        public string stateName = "Default";

        public readonly Guid stateID;

        public SequencerState ()
        {
            stateID = Guid.NewGuid ();
        }

        public SequencerState (string newID)
        {
            stateID = new Guid (newID);
        }

        public string Serialize()
        {
            return  stateID.ToString () + ":" + stateName.Replace (':', ' ');
        }
    }
}

