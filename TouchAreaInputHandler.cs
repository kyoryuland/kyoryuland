using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class TouchAreaInputHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public OsawariGameController controller;
    public TouchArea area;
    public float longPressSeconds = 0.4f;

    private const int NoPointer = int.MinValue;

    private bool inputEnabled = true;
    private bool isPointerDown;
    private bool longPressFired;
    private int activePointerId = NoPointer;
    private PointerEventData.InputButton activeButton = PointerEventData.InputButton.Left;
    private Coroutine longPressCoroutine;

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            ResetPointerState(false);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!inputEnabled || controller == null)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left && eventData.button != PointerEventData.InputButton.Right)
        {
            return;
        }

        ResetPointerState(false);

        isPointerDown = true;
        longPressFired = false;
        activePointerId = eventData.pointerId;
        activeButton = eventData.button;

        controller.NotifyPointerDown(area, eventData.position);

        longPressCoroutine = StartCoroutine(LongPressCoroutine());
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!inputEnabled || controller == null || !isPointerDown || eventData.pointerId != activePointerId)
        {
            return;
        }

        controller.NotifyPointerDrag(eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        bool shouldDispatchClick = inputEnabled && isPointerDown && eventData.pointerId == activePointerId && !longPressFired;
        AreaInputTrigger trigger = activeButton == PointerEventData.InputButton.Right
            ? AreaInputTrigger.RightClick
            : AreaInputTrigger.LeftClick;

        ResetPointerState(false);
        controller.NotifyPointerUp();

        if (shouldDispatchClick)
        {
            controller.HandleAreaInput(area, trigger);
        }
    }

    private IEnumerator LongPressCoroutine()
    {
        float startTime = Time.unscaledTime;
        float threshold = Mathf.Max(0f, longPressSeconds);

        while (isPointerDown && !longPressFired)
        {
            if (Time.unscaledTime - startTime >= threshold)
            {
                longPressFired = true;
                if (inputEnabled && controller != null)
                {
                    controller.HandleAreaInput(area, AreaInputTrigger.LongPress);
                }

                yield break;
            }

            yield return null;
        }
    }

    private void ResetPointerState(bool notifyPointerUp)
    {
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
            longPressCoroutine = null;
        }

        bool hadPointer = isPointerDown;
        isPointerDown = false;
        longPressFired = false;
        activePointerId = NoPointer;

        if (notifyPointerUp && hadPointer && controller != null)
        {
            controller.NotifyPointerUp();
        }
    }

    private void OnDisable()
    {
        ResetPointerState(true);
    }
}
