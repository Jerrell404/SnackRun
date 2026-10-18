using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [SerializeField] private float interactRange = 2f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private Camera playerCamera;

    // The Player object has TWO PlayerInteract components in every scene -
    // only the first one to handle E each frame acts, so a single press
    // never triggers two interactions at once (e.g. opening the fridge AND
    // grabbing the milk).
    private static int lastHandledFrame = -1;

    void Update()
    {
        // Which food E would take right now (drives the "Press E to take ..." label).
        FoodItem.UpdateTarget(transform);

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (lastHandledFrame == Time.frameCount) return;
            lastHandledFrame = Time.frameCount;
            TryInteract();
        }
    }

    void TryInteract()
    {
        // Hiding? E always means "come back out" - nothing else.
        if (HidingSpot.ActiveHidingSpot != null)
        {
            HidingSpot.ActiveHidingSpot.Interact();
            return;
        }

        // Standing in front of the fridge? It handles E itself (open, grab
        // the next food on the list, close) - no precise aiming needed.
        if (Fridge.TryHandleNearby(transform.position))
            return;

        // Same for the cupboard with food in it.
        if (Cupboard.TryHandleNearby(transform.position))
            return;

        // Same for Toby - standing next to him is enough to talk.
        if (TobyNPC.TryHandleNearby(transform.position))
            return;

        // Food: if a "Press E to collect" label is showing, E collects that
        // food - no need to line the camera up with it exactly.
        if (FoodItem.TryCollectNearby(transform))
            return;

        // Same for ropes - if a "Press E to climb..." prompt is showing, E climbs/slides that rope. Ropes are thin.
        if (RopeClimb.TryHandleNearby(transform.position))
            return;

       // Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        Ray ray = new Ray(transform.position, playerCamera.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * interactRange, Color.red, 2f);

        float interactRadius = 0.6f; // Increase this to make the interaction area larger (bumped again - milk sitting inside the open fridge was still too fiddly to reliably reach)

        // SphereCastAll instead of the single-hit SphereCast.
        RaycastHit[] hits = Physics.SphereCastAll(ray, interactRadius, interactRange, interactableLayer);

        if (hits.Length == 0)
        {
            Debug.Log("Raycast missed everything.");
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        IInteractable best = null;
        Collider bestCollider = null;

        foreach (RaycastHit hit in hits)
        {
            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
            if (candidate == null) continue;

            if (candidate is FoodItem)
            {
                // Food always wins, regardless of distance - prioritise
                // collecting it over any other interactable (e.g. the
                // fridge door) that happens to be in range at the same time.
                best = candidate;
                bestCollider = hit.collider;
                break;
            }

            if (best == null)
            {
                best = candidate;
                bestCollider = hit.collider;
            }
        }

        if (best != null)
        {
            Debug.Log("Hit: " + bestCollider.name);
            best.Interact();
        }
        else
        {
            System.Text.StringBuilder names = new System.Text.StringBuilder();
            foreach (RaycastHit h in hits) names.Append(h.collider.name).Append(", ");
            Debug.Log("No IInteractable found on any hit collider (hit: " + names + ")");
        }
    }
}
