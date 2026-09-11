using UnityEngine;

public class GameplayManager : MonoBehaviour
{
    [SerializeField] private Joystick joystick;
    [SerializeField] private CreepSwarmController swarmController;

    private void OnEnable()
    {
        if (joystick != null)
        {
            joystick.AddInputListener(OnJoystickInput);
        }
    }

    private void OnDisable()
    {
        if (joystick != null)
        {
            joystick.RemoveInputListener(OnJoystickInput);
        }
    }

    private void OnJoystickInput(Vector2 input)
    {
        if (swarmController == null)
        {
            return;
        }

        swarmController.SetJoystickInput(input);
    }
}
