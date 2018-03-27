using UnityEngine;

using UnityEngine.UI;
using UnityEngine.Experimental.Input;

public class TouchReporter : MonoBehaviour
{
    public Image image;
    public Text coordinateText;

    [Tooltip("This is the index of the touch that will be reported.  Appropriate values are between zero and TouchscreenState.kMaxTouches - 1")]
    public int touchIndex = 0;

    void Update()
    {
        Touchscreen touchscreen = UnityEngine.Experimental.Input.Touchscreen.current;

        coordinateText.text = touchscreen.allTouchControls[touchIndex].ReadValue().position.x.ToString("0000") + ", " +
            touchscreen.allTouchControls[touchIndex].ReadValue().position.y.ToString("0000");

        if (touchscreen.allTouchControls[touchIndex].ReadValue().phase != PointerPhase.None &&
            touchscreen.allTouchControls[touchIndex].ReadValue().phase != PointerPhase.Ended)
        {
            image.color = Color.red;
        }
        else
        {
            image.color = Color.white;
        }
    }
}
