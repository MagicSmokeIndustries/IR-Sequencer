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

            Debug.Log("Group OnDrop: " + droppedObject.name);
        }

        public void onSequenceDrop(SequenceDragHandler dragHandler)
        {
            
            
        }
    }

}