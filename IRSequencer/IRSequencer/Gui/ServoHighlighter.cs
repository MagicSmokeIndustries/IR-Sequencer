using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using IRSequencer;
using IRSequencer.API;
using System;


namespace IRSequencer.Gui
{
    public class ServoHighlighter : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public IRWrapper.IServo servo;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (servo != null)
                servo.Highlight = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (servo != null)
                servo.Highlight = false;
        }

        public void OnDestroy()
        {
            if (servo != null)
                servo.Highlight = false;
        }
        
    }
}
