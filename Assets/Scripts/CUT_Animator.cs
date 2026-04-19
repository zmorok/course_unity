using System;
using UnityEngine;
using System.Collections;

public class CUT_Animator : MonoBehaviour
{
    public static event Action<bool> CuttingStateChanged;
    public static event Action CutCompletedAfterLift;
    public static bool IsCutting { get; private set; }

    [SerializeField] private Animator holderAnimator;
    [SerializeField] private Animator bladeAnimator;

    [SerializeField] private float holdDuration = 0.5f;
    [SerializeField] private float cutDuration = 0.6f;
    [SerializeField] private float liftDuration = 0.4f;
    [SerializeField] private float soundDuration = 1.2f;

    private bool isCutInProgress;
    private bool isHoldInProgress;

    private AudioSource audioSource;
    private AudioClip cutSound;

    public bool CanBeCutted { get; set; }
    public bool CutCompleted { get; private set; }

    private void Start()
    {
        InitializeAudio();

        CanBeCutted = false;
        CutCompleted = false;
        SetCuttingState(false, notifyListeners: false);
    }

    private void OnEnable()
    {
        btn_Animator.MachinePowerChanged += HandleMachinePowerChanged;
        btn_Animator.ButtonPressed += HandleButtonPressed;
    }

    private void OnDisable()
    {
        btn_Animator.MachinePowerChanged -= HandleMachinePowerChanged;
        btn_Animator.ButtonPressed -= HandleButtonPressed;
        SetCuttingState(false, notifyListeners: true);
    }

    private void InitializeAudio()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        cutSound = Resources.Load<AudioClip>("Sounds/CUT_Cut");
        if (cutSound == null)
            Debug.LogError("Не найден звук: Resources/Sounds/CUT_Cut");

        audioSource.clip = cutSound;
        audioSource.pitch = 1f;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.volume = 1f;
        audioSource.mute = false;
        audioSource.spatialBlend = 0f;
    }

    private IEnumerator StartCut()
    {
        if (!CanBeCutted) yield break;

        isCutInProgress = true;
        isHoldInProgress = true;
        SetCuttingState(true, notifyListeners: true);

        // Сразу запрещаем повторный рез, пока бумага не будет заново подана
        CanBeCutted = false;
        CutCompleted = false;

        if (audioSource != null && cutSound != null)
        {
            audioSource.Stop();
            audioSource.time = 0f;
            audioSource.Play();
        }

        holderAnimator.SetBool("isHolding", true);
        yield return new WaitForSeconds(holdDuration);

        bladeAnimator.SetBool("isCutting", true);
        yield return new WaitForSeconds(cutDuration);

        holderAnimator.SetBool("isHolding", false);
        bladeAnimator.SetBool("isCutting", false);

        float clampedLiftDuration = Mathf.Max(0f, liftDuration);
        if (clampedLiftDuration > 0f)
            yield return new WaitForSeconds(clampedLiftDuration);

        CutCompleted = true;
        isCutInProgress = false;
        isHoldInProgress = false;
        SetCuttingState(false, notifyListeners: true);
        CutCompletedAfterLift?.Invoke();

        float extraSoundTime = Mathf.Max(0f, soundDuration - holdDuration - cutDuration - clampedLiftDuration);
        if (extraSoundTime > 0f)
            yield return new WaitForSeconds(extraSoundTime + 1.3f);

        if (audioSource != null)
            audioSource.Stop();
    }

    private void HandleMachinePowerChanged(bool isPowered)
    {
        if (!isPowered)
            ResetCutState();
    }

    private void HandleButtonPressed(ControlPanelButton button)
    {
        if (button != ControlPanelButton.DualStart)
            return;

        if (!CanStartCut())
            return;

        StartCoroutine(StartCut());
    }

    public bool TryConsumeCutCompleted()
    {
        if (!CutCompleted) return false;

        CutCompleted = false;
        return true;
    }

    public void ResetCutState()
    {
        StopAllCoroutines();

        isCutInProgress = false;
        isHoldInProgress = false;
        SetCuttingState(false, notifyListeners: true);
        CanBeCutted = false;
        CutCompleted = false;

        if (holderAnimator != null)
            holderAnimator.SetBool("isHolding", false);

        if (bladeAnimator != null)
            bladeAnimator.SetBool("isCutting", false);

        if (audioSource != null)
            audioSource.Stop();
    }

    public bool TryStartCutExternally()
    {
        if (!CanStartCut())
            return false;

        StartCoroutine(StartCut());
        return true;
    }

    public bool IsBusy => isCutInProgress || isHoldInProgress;

    private bool CanStartCut()
    {
        if (!btn_Animator.IsMachinePowered)
            return false;

        if (!PracticeTasksPopupController.IsCutStartAllowed())
            return false;

        if (isCutInProgress || isHoldInProgress)
            return false;

        if (!CanBeCutted)
            return false;

        if (holderAnimator == null || bladeAnimator == null)
        {
            Debug.LogWarning("Не назначены аниматоры holderAnimator и/или bladeAnimator");
            return false;
        }

        if (!holderAnimator.isActiveAndEnabled || !bladeAnimator.isActiveAndEnabled)
            return false;

        return true;
    }

    private static void SetCuttingState(bool isCutting, bool notifyListeners)
    {
        bool changed = IsCutting != isCutting;
        IsCutting = isCutting;

        if (notifyListeners && changed)
            CuttingStateChanged?.Invoke(IsCutting);
    }
}
