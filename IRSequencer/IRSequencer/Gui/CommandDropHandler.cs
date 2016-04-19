using UnityEngine;
using System.Collections;
using UnityEngine.UI;
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
            Logger.Log("[CommandDropHandler] onCommandDrop called");

            if (!SequencerGUI.Instance)
                return;

            if (SequencerGUI.Instance.openSequence == null)
                return;

            var bc = dragHandler.linkedCommand;
            int insertAt = dragHandler.placeholder.transform.GetSiblingIndex();

            SequencerGUI.Instance.openSequence.commands.Remove(bc);
            SequencerGUI.Instance.openSequence.commands.Insert(insertAt, bc);

            //SequencerGUI.guiRebuildPending = true;

            /*var commandNumberText = dragHandler.draggedItem.GetChild("CommandNumberLabel").GetComponent<Text>();
            commandNumberText.text = string.Format("{0:#0}", SequencerGUI.Instance.openSequence.commands.FindIndex(c => c == bc));
            */
            Logger.Log("[CommandDropHandler] onCommandDrop finished");

        }
    }

}