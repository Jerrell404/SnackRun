using UnityEngine;

public class TVFlickerController : MonoBehaviour
{
    [SerializeField] private Renderer tvScreenRenderer; // drag televisionModern here
    [SerializeField] private int tvScreenMaterialIndex = 1; // index 1 = TVScreenMaterial slot

    // Read by other scripts that need the TV screen renderer.
    public Renderer ScreenRenderer => tvScreenRenderer;
    public int ScreenMaterialIndex => tvScreenMaterialIndex;
    public bool IsFlickering { get; private set; }

    public void StartFlicker()
    {
        IsFlickering = true;
        tvScreenRenderer.materials[tvScreenMaterialIndex].SetFloat("_EnableFlicker", 1f);
    }

    public void StopFlicker()
    {
        IsFlickering = false;
        tvScreenRenderer.materials[tvScreenMaterialIndex].SetFloat("_EnableFlicker", 0f);
    }
}