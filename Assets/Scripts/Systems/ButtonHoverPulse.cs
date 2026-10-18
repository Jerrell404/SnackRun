using UnityEngine;
using UnityEngine.EventSystems;

// Attached to every main-menu icon button.
[RequireComponent(typeof(RectTransform))]
public class ButtonHoverPulse : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("How big the pulse gets at full hover, as a scale fraction (0.01 = 1%).")]
    [SerializeField] private float pulseAmount = 0.012f;
    [Tooltip("How fast the pulse breathes while hovered.")]
    [SerializeField] private float pulseSpeed = 3.2f;
    [Tooltip("How quickly the effect eases in on hover and back out when the cursor leaves.")]
    [SerializeField] private float fadeSpeed = 8f;

    private RectTransform rt;
    private Vector3 baseScale = Vector3.one;
    private bool hovering;
    private float hoverT; // 0 = idle/still, 1 = fully eased into the hover pulse

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        baseScale = rt.localScale;
    }

    private void OnDisable()
    {
        hovering = false;
        hoverT = 0f;
        if (rt != null) rt.localScale = baseScale;
    }

    private void Update()
    {
        hoverT = Mathf.MoveTowards(hoverT, hovering ? 1f : 0f, Time.unscaledDeltaTime * fadeSpeed);
        if (hoverT <= 0f)
        {
            rt.localScale = baseScale;
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount * hoverT;
        rt.localScale = baseScale * pulse;
    }

    public void OnPointerEnter(PointerEventData eventData) => hovering = true;
    public void OnPointerExit(PointerEventData eventData) => hovering = false;
}
