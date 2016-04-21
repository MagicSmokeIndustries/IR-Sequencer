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

            var repeatPlaceholder = dragHandler.dropZone.gameObject.GetChild("RepeatCommandPlaceholder").transform;

            int insertAt = dragHandler.placeholder.transform.GetSiblingIndex();

            //repeat placeholder is supposed to be last sibling, we need to ignore it
            if (insertAt >= repeatPlaceholder.GetSiblingIndex())
                insertAt--;

            if (bc == null)
                return;

            SequencerGUI.Instance.openSequence.commands.Remove(bc);
            SequencerGUI.Instance.openSequence.commands.Insert(insertAt, bc);

            //change the line numbers in lables after drop
            for (int i = 0; i < SequencerGUI.Instance.openSequence.commands.Count; i++)
            {
                var c = SequencerGUI.Instance.openSequence.commands[i];
                var commandUIControls = SequencerGUI.Instance._openSequenceCommandControls[c];
                if (!commandUIControls)
                    continue;

                var commandLineNumberText = commandUIControls.GetChild("CommandNumberLabel").GetComponent<Text>();
                commandLineNumberText.text = string.Format("{0:#0}", i);

                if(c.gotoIndex != -1)
                {
                    //need to reposition command's placeholder
                    if (repeatPlaceholder)
                    {
                        repeatPlaceholder.SetSiblingIndex(c.gotoIndex);
                        repeatPlaceholder.gameObject.SetActive(true);
                    }
                        
                }
            }
            Logger.Log("[CommandDropHandler] onCommandDrop finished");

        }
    }

}