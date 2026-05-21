using System;
using System.Collections;
using UnityEngine;

public class CutAnimator : MonoBehaviour
{
    public static event Action<bool> CuttingStateChanged;
    public static event Action CutCompletedAfterLift;
    public static bool IsCutting { get; private set; }

    [Header("Animation")]
    [SerializeField] private Animator holderAnimator;
    [SerializeField] private Animator bladeAnimator;

    [Header("Timing")]
    [SerializeField] private float holdDuration = 0.5f;
    [SerializeField] private float cutDuration = 0.6f;
    [SerializeField] private float liftDuration = 0.4f;
    [SerializeField] private float soundDuration = 1.2f;

    private AudioSource audioSource;
    private AudioClip cutSound;
    private bool isCutInProgress;
    private bool isHoldInProgress;

    public bool CanBeCutted { get; set; }
    public bool CutCompleted { get; private set; }
    public bool IsBusy => isCutInProgress || isHoldInProgress;

    private void Start()
    {
        InitializeAudio();
        ResetCutFlags();
        SetCuttingState(false, notifyListeners: false);
    }

    private void OnEnable()
    {
        ButtonAnimator.MachinePowerChanged += HandleMachinePowerChanged;
        ButtonAnimator.ButtonPressed += HandleButtonPressed;
    }

    private void OnDisable()
    {
        ButtonAnimator.MachinePowerChanged -= HandleMachinePowerChanged;
        ButtonAnimator.ButtonPressed -= HandleButtonPressed;
        SetCuttingState(false, notifyListeners: true);
    }

    public bool TryConsumeCutCompleted()
    {
        if (!CutCompleted)
            return false;

        CutCompleted = false;
        return true;
    }

    public void ResetCutState()
    {
        StopAllCoroutines();
        ResetCutFlags();
        SetCuttingState(false, notifyListeners: true);
        ResetAnimatorBools();

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

    private void HandleMachinePowerChanged(bool isPowered)
    {
        if (!isPowered)
            ResetCutState();
    }

    private void HandleButtonPressed(ControlPanelButton button)
    {
        if (button != ControlPanelButton.DualStart)
            return;

        if (CanStartCut())
            StartCoroutine(StartCut());
    }

    private IEnumerator StartCut()
    {
        if (!CanBeCutted)
            yield break;

        isCutInProgress = true;
        isHoldInProgress = true;
        SetCuttingState(true, notifyListeners: true);

        // сразу запрещаем повторный рез, пока PaperPathMover заново не подаст бумагу к линии реза
        CanBeCutted = false;
        CutCompleted = false;

        PlayCutSound();

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

        // событие отправляется после подъёма ножа, чтобы PaperPathMover начал разделять части только после безопасного состояния
        CutCompletedAfterLift?.Invoke();

        yield return WaitForSoundTail(clampedLiftDuration);

        if (audioSource != null)
            audioSource.Stop();
    }

    private bool CanStartCut()
    {
        if (!ButtonAnimator.IsMachinePowered)
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

        return holderAnimator.isActiveAndEnabled && bladeAnimator.isActiveAndEnabled;
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

    private void PlayCutSound()
    {
        if (audioSource == null || cutSound == null)
            return;

        audioSource.Stop();
        audioSource.time = 0f;
        audioSource.Play();
    }

    private IEnumerator WaitForSoundTail(float clampedLiftDuration)
    {
        float extraSoundTime = Mathf.Max(0f, soundDuration - holdDuration - cutDuration - clampedLiftDuration);
        if (extraSoundTime > 0f)
            yield return new WaitForSeconds(extraSoundTime + 1.3f);
    }

    private void ResetCutFlags()
    {
        isCutInProgress = false;
        isHoldInProgress = false;
        CanBeCutted = false;
        CutCompleted = false;
    }

    private void ResetAnimatorBools()
    {
        if (holderAnimator != null)
            holderAnimator.SetBool("isHolding", false);

        if (bladeAnimator != null)
            bladeAnimator.SetBool("isCutting", false);
    }

    private static void SetCuttingState(bool isCutting, bool notifyListeners)
    {
        bool changed = IsCutting != isCutting;
        IsCutting = isCutting;

        if (notifyListeners && changed)
            CuttingStateChanged?.Invoke(IsCutting);
    }
}
