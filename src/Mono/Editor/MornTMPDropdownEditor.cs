#if USE_TEXTMESHPRO
using TMPro.EditorUtilities;
using UnityEditor;

namespace MornLib.Mono
{
    [CustomEditor(typeof(MornTMPDropdown))]
    public class MornTMPDropdownEditor : DropdownEditor
    {
        public override void OnInspectorGUI()
        {
            var dropdown = (MornTMPDropdown)target;
            dropdown.IsClickOnMouseRight = EditorGUILayout.Toggle("IsClickOnMouseRight", dropdown.IsClickOnMouseRight);
            dropdown.IsClickOnMouseMiddle =
                EditorGUILayout.Toggle("IsClickOnMouseMiddle", dropdown.IsClickOnMouseMiddle);
            dropdown.IsClickOnMouseLeft = EditorGUILayout.Toggle("IsClickOnMouseLeft", dropdown.IsClickOnMouseLeft);
            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }
    }
}
#endif
