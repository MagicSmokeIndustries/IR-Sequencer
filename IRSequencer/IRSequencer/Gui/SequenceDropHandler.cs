using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

namespace IRSequencer.Gui
{
    /// <summary>
    /// Handles the IR logic of group drop
    /// </summary>
    public class SequenceDropHandler : MonoBehaviour, IDropHandler
    {
        public IRSequencer.Core.SequencerState linkedState;

        public void OnDrop(PointerEventData eventData)
        {
            var droppedObject = eventData.pointerDrag;
            var dragHandler = droppedObject.GetComponent<SequenceDragHandler>();

            if(dragHandler == null)
            {
                Logger.Log("[SequenceDropHandler] No SequenceDragHandler on dropped object");
                return;
            }

            onSequenceDrop(dragHandler);
        }

        public void onSequenceDrop(SequenceDragHandler dragHandler)
        {
            var droppedSequence = dragHandler.linkedSequence;
            Logger.Log("[SequenceDropHandler] onSequenceDrop called");

            if (linkedState == null || droppedSequence == null)
                return;

            droppedSequence.startState = linkedState;

            Logger.Log("[SequenceDropHandler] onSequenceDrop finished, new startState = " + droppedSequence.startState.stateName);

        }
    }

}