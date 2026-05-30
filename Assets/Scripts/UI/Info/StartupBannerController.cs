using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class StartupBannerController : MonoBehaviour, IPointerClickHandler
{
    private static readonly object InputLockOwner = typeof(StartupBannerController);

    private bool dismissed;

    private void OnEnable()
    {
        if (!Application.isPlaying || dismissed)
            return;

        SimulationInputGate.Lock(InputLockOwner);
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        SimulationInputGate.Unlock(InputLockOwner);
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying)
            return;

        SimulationInputGate.Unlock(InputLockOwner);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        dismissed = true;
        SimulationInputGate.Unlock(InputLockOwner);
        gameObject.SetActive(false);
    }
}
