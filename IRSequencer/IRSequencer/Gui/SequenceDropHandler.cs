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
            var dropppedSequence = dragHandler.linkedSequence;

            if (linkedState == null || dropppedSequence == null)
                return;

            dropppedSequence.startState = linkedState;

        }
    }

}