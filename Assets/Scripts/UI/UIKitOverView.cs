using UnityEngine;

// marks a UI object drawn over a 3D view outside the main viewport (the Starport's door name over the hall): it and everything under it take the scheme's
// dark theme in either mode (UIKitTheme.IsOverView), as what is in the viewport does, since the 3D views stay dark in the light modes
[DisallowMultipleComponent]
public class UIKitOverView : MonoBehaviour
{
}
