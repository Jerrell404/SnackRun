using UnityEngine;

// Lets Bandit walk up real step-by-step stair colliders. Each step on the backyard stairs is ~0.14 units tall.
[RequireComponent(typeof(Collider))]
public class StairStepAssist : MonoBehaviour
{
    [Tooltip("Step Offset (in the CharacterController's own units, as shown in its Inspector) used while Bandit is on/near the stairs.")]
    [SerializeField] private float stepOffsetOnStairs = 0.8f;

    private CharacterController controller;
    private float originalStepOffset;
    private bool applied;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        CharacterController cc = other.GetComponent<CharacterController>();
        if (cc == null) cc = other.GetComponentInParent<CharacterController>();
        if (cc == null || applied) return;

        controller = cc;
        originalStepOffset = cc.stepOffset;

        // Unity requires Step Offset <= Height + 2 * Radius.
        float maxAllowed = cc.height + cc.radius * 2f;
        cc.stepOffset = Mathf.Min(Mathf.Max(originalStepOffset, stepOffsetOnStairs), maxAllowed);
        applied = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        Restore();
    }

    private void OnDisable()
    {
        Restore();
    }

    private void Restore()
    {
        if (!applied || controller == null) return;
        controller.stepOffset = originalStepOffset;
        applied = false;
    }
}
