using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

namespace IRSequencer.Gui
{
    /// <summary>
    /// Handles the IR logic of group drop
    /// </summary>
    public class StateDropHandler : MonoBehaviour, IDropHandler
    {
        public void OnDrop(PointerEventData eventData)
        {
            var droppedObject = eventData.pointerDrag;
            var dragHandler = droppedObject.GetComponent<StateDragHandler>();

            if(dragHandler == null)
            {
                Logger.Log("[StateDropHandler] No SequenceDragHandler on dropped object");
                return;
            }
               
            onStateDrop (dragHandler);
        }

        public void onStateDrop(StateDragHandler dragHandler)
        {
            var droppedState = dragHandler.linkedState;
            if(droppedState == null)
            {
                return;
                //error
            }

            var module = SequencerGUI.Instance.sequencers.Find (s => s.states.Contains (droppedState));
            if(module == null)
            {
                //error
                return;
            }

            int insertAt = dragHandler.placeholder.transform.GetSiblingIndex();

            module.states.Remove (droppedState);
            module.states.Insert (insertAt, droppedState);
        }
    }

}