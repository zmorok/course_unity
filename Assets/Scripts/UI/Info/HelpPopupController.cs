using UnityEngine;

public class HelpPopupController : MonoBehaviour
{
    [SerializeField] private GameObject helpPopup;

    public void ShowHelp()
    {
        if (helpPopup != null) helpPopup.SetActive(true);
    }

    public void HideHelp()
    {
        if (helpPopup != null) helpPopup.SetActive(false);
    }
}