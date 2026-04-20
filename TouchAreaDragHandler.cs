using UnityEngine;
using UnityEngine.EventSystems;

public class TouchAreaDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
{
    public OsawariGameController controller;
    public TouchArea area;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        controller.NotifyPointerDown(area, eventData.position);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        controller.NotifyDrag(area, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        controller.NotifyDrag(area, eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        controller.NotifyPointerUp(area, eventData.position);
    }
}
