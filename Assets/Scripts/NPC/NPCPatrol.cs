using UnityEngine;

public class NPCPatrol : MonoBehaviour
{
    [SerializeField] protected Transform[] waypoints;
    [SerializeField] protected float moveSpeed = 2f;
    protected int currentWaypoint = 0;
    protected bool isPatrolling = true;

    // Guards against spamming the console every frame when a waypoint slot
    // was left unassigned in the Inspector (e.g. array size > 0 but an
    // element still set to "None").
    private bool loggedMissingWaypoint = false;

    protected virtual void Update()
    {
        if (!isPatrolling || waypoints == null || waypoints.Length == 0) return;

        Transform target = waypoints[currentWaypoint];
        if (target == null)
        {
            if (!loggedMissingWaypoint)
            {
                Debug.LogWarning($"{name}: waypoints[{currentWaypoint}] has not been assigned " +
                                  "in the Inspector. Drag a waypoint Transform into that slot on " +
                                  "the Waypoints array - patrol movement is paused until then.", this);
                loggedMissingWaypoint = true;
            }
            return;
        }

        StepTowards(target.position, moveSpeed);

        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
        }
    }

    // One movement step towards a point. NPCs that path-find (Edgar) override this.
    protected virtual void StepTowards(Vector3 target, float speed)
    {
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        transform.LookAt(new Vector3(target.x, transform.position.y, target.z));
    }
}
