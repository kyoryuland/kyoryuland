using UnityEngine;
using UnityEngine.EventSystems;

public class TouchAreaInputHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public OsawariGameController controller;
    public TouchArea area;
    public float longPressSeconds = 0.4f;

    private bool isPressing;
    private bool firedLong;
    private float pressStartTime;
    private int activePointerId = int.MinValue;
    private PointerEventData.InputButton pressedButton;

    private void Update()
    {
        if (!isPressing || firedLong || controller == null || controller.IsGameplayInputBlocked)
        {
            return;
        }

        if (Time.unscaledTime - pressStartTime >= Mathf.Max(0.01f, longPressSeconds))
        {
            controller.HandleAreaInput(area, AreaInputTrigger.Long);
            firedLong = true;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressing = true;
        firedLong = false;
        pressStartTime = Time.unscaledTime;
        activePointerId = eventData.pointerId;
        pressedButton = eventData.button;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPressing || eventData.pointerId != activePointerId)
        {
            return;
        }

        isPressing = false;
        activePointerId = int.MinValue;

        if (firedLong || controller == null || controller.IsGameplayInputBlocked)
        {
            firedLong = false;
            return;
        }

        AreaInputTrigger trigger = pressedButton == PointerEventData.InputButton.Right
            ? AreaInputTrigger.Right
            : AreaInputTrigger.Left;
        controller.HandleAreaInput(area, trigger);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Intentionally handled by down/up to suppress click when long press already fired.
    }

    private void OnDisable()
    {
        isPressing = false;
        firedLong = false;
        activePointerId = int.MinValue;
    }
}
