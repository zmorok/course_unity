using UnityEngine;
using UnityEngine.EventSystems;

public class ElementInfoButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea(2, 5)]
    [SerializeField] private string infoMessage;

    [SerializeField] private BottomInfoPanel infoPanel;
    [SerializeField] private HighlightTarget highlightTarget;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (infoPanel != null)
            infoPanel.ShowInfo(infoMessage);

        if (highlightTarget != null)
            highlightTarget.Highlight();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (infoPanel != null)
            infoPanel.ShowDefault();

        if (highlightTarget != null)
            highlightTarget.ResetHighlight();
    }
}