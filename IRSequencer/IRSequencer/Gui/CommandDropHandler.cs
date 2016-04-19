using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

namespace IRSequencer.Gui
{
    /// <summary>
    /// Handles the IR logic of group drop
    /// </summary>
    public class CommandDropHandler : MonoBehaviour, IDropHandler
    {
        public void OnDrop(PointerEventData eventData)
        {
            var droppedObject = eventData.pointerDrag;
            var dragHandler = droppedObject.GetComponent<CommandDragHandler>();

            if(dragHandler == null)
            {
                Logger.Log("[CommandDropHandler] No CommandDragHandler on dropped object");
                return;
            }

            onCommandDrop(dragHandler);
        }

        public void onCommandDrop(CommandDragHandler dragHandler)
        {
            Logger.Log("[CommandDropHandler] onSequenceDrop called");

            //use SequencerGUI.openSequence to get sequence details

            Logger.Log("[CommandDropHandler] onSequenceDrop finished");

        }
    }

}