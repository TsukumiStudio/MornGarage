#if USE_TEXTMESHPRO
using TMPro;
using UnityEngine.EventSystems;

namespace MornLib.Mono
{
    public class MornTMPDropdown : TMP_Dropdown
    {
        public bool IsClickOnMouseRight;
        public bool IsClickOnMouseMiddle;
        public bool IsClickOnMouseLeft;

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.IsLeftClick() && IsClickOnMouseLeft) Show();

            if (eventData.IsMiddleClick() && IsClickOnMouseMiddle) Show();

            if (eventData.IsRightClick() && IsClickOnMouseRight) Show();
        }
    }
}
#endif
