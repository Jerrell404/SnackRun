using UnityEngine;

// A zone at the edge of Rex's territory (RexBypassTrigger_Left/Right in the scene) where Bandit is trying to slip past him rather than confront him head-on.
public class RexBypassTrigger : MonoBehaviour
{
    [SerializeField] private DogNPC dog;
    [Tooltip("Whether this is one of the RIGHT-side bypass zones (RexBypassTrigger_Right) rather than a LEFT one.")]
    [SerializeField] private bool isRightSide = false;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (dog == null) return;
        dog.WakeUp();
        dog.EnterBypassZone(isRightSide);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (dog == null) return;
        dog.ExitBypassZone();
    }
}
