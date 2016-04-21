using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using IRSequencer;
using IRSequencer.API;
using System;


namespace IRSequencer.Gui
{
    public class EditorLocker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public IRWrapper.IServo servo;

        public void OnPointerEnter(PointerEventData eventData)
        {
            EditorLock(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            EditorLock(false);
        }
        /*
        public void OnDestroy()
        {
            EditorLock(false);
        }
        */
        internal static void EditorLock(Boolean apply)
        {
            //only do this lock in the editor - no point elsewhere
            if (HighLogic.LoadedSceneIsEditor && apply)
            {
                //only add a new lock if there isnt already one there
                if (InputLockManager.GetControlLock("IRSGUILockOfEditor") != ControlTypes.EDITOR_LOCK)
                {
                    Logger.Log(String.Format("[GUI] AddingLock-{0}", "IRSGUILockOfEditor"), Logger.Level.SuperVerbose);

                    InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, "IRSGUILockOfEditor");
                }
            }
            //Otherwise make sure the lock is removed
            else
            {
                //Only try and remove it if there was one there in the first place
                if (InputLockManager.GetControlLock("IRSGUILockOfEditor") == ControlTypes.EDITOR_LOCK)
                {
                    Logger.Log(String.Format("[GUI] Removing-{0}", "IRSGUILockOfEditor"), Logger.Level.SuperVerbose);
                    InputLockManager.RemoveControlLock("IRSGUILockOfEditor");
                }
            }
        }

    }
}
