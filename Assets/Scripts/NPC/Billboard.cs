using UnityEngine;

// Keeps a world-space UI element (e.g. Toby's nameplate/prompt/speech
// bubble) facing the camera at all times - world-space canvases don't do
// this on their own, unlike screen-space overlay UI.
public class Billboard : MonoBehaviour
{
    private Transform cam;

    void LateUpdate()
    {
        if (cam == null)
        {
            if (Camera.main == null) return;
            cam = Camera.main.transform;
        }

        transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
    }
}
