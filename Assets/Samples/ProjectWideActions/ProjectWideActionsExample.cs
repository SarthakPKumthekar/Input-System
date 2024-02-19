#if UNITY_INPUT_SYSTEM_PROJECT_WIDE_ACTIONS

namespace UnityEngine.InputSystem.Samples.ProjectWideActions
{
    public class ProjectWideActionsExample : MonoBehaviour
    {
        [SerializeField] public GameObject cube;

        InputAction move;
        InputAction look;
        InputAction attack;
        InputAction jump;
        InputAction interact;
        InputAction next;
        InputAction previous;
        InputAction sprint;
        InputAction crouch;

        // Start is called before the first frame update
        void Start()
        {
            // Project-Wide Actions
            if (InputSystem.actions)
            {
                move = InputSystem.actions.FindAction("Player/Move");
                look = InputSystem.actions.FindAction("Player/Look");
                attack = InputSystem.actions.FindAction("Player/Attack");
                jump = InputSystem.actions.FindAction("Player/Jump");
                interact = InputSystem.actions.FindAction("Player/Interact");
                next = InputSystem.actions.FindAction("Player/Next");
                previous = InputSystem.actions.FindAction("Player/Previous");
                sprint = InputSystem.actions.FindAction("Player/Sprint");
                crouch = InputSystem.actions.FindAction("Player/Crouch");

                if (!InputSystem.actions.enabled)
                {
                    Debug.Log("Project Wide Input Actions should be enabled by default by Unity but they are not - enabling to make sure the input works");
                    InputSystem.actions.Enable();
                }
            }
            else
            {
                Debug.Log("Setup Project Wide Input Actions in the Player Settings, Input System section");
            }

            // Handle input by responding to callbacks
            if (attack != null)
            {
                attack.performed += ctx => cube.GetComponent<Renderer>().material.color = Color.red;
                attack.canceled += ctx => cube.GetComponent<Renderer>().material.color = Color.green;
            }
        }

        // Update is called once per frame
        void Update()
        {
            // Handle input by polling each frame
            if (move != null)
            {
                var moveVal = move.ReadValue<Vector2>() * 10.0f * Time.deltaTime;
                cube.transform.Translate(new Vector3(moveVal.x, moveVal.y, 0));
            }
        }
    } // class ProjectWideActionsExample
} // namespace UnityEngine.InputSystem.Samples.ProjectWideActions

#endif
