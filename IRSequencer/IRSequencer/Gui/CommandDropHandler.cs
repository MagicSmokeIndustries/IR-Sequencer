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
            if (bc == null)
                return;

            SequencerGUI.Instance.openSequence.commands.Remove(bc);
            SequencerGUI.Instance.openSequence.commands.Insert(insertAt, bc);

            //change the line numbers in lables after drop
            for (int i = 0; i < SequencerGUI.Instance.openSequence.commands.Count; i++)
            {
                var commandUIControls = SequencerGUI.Instance._openSequenceCommandControls[SequencerGUI.Instance.openSequence.commands[i]];
                if (!commandUIControls)
                    continue;

                var commandLineNumberText = commandUIControls.GetChild("CommandNumberLabel").GetComponent<Text>();
                commandLineNumberText.text = string.Format("{0:#0}", i);
            }
            Logger.Log("[CommandDropHandler] onCommandDrop finished");

        }
    }

}