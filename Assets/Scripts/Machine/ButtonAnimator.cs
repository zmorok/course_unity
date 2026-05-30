using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ButtonAnimator : MonoBehaviour
{
    public static event Action<ControlPanelButton> ButtonPressed;
    public static event Action<bool> MachinePowerChanged;
    public static event Action EmergencyStopPressed;

    public static bool IsMachinePowered { get; private set; }

    private enum AnimatorPlaybackMode
    {
        HoldBool,
        OneShotClip
    }

    private sealed class AnimatorTargetBinding
    {
        public AnimatorTargetBinding(Animator animator, AnimatorPlaybackMode playbackMode, string stateName, float clipLength)
        {
            Animator = animator;
            PlaybackMode = playbackMode;
            StateName = stateName;
            ClipLength = clipLength;
        }

        public Animator Animator { get; }
        public AnimatorPlaybackMode PlaybackMode { get; }
        public string StateName { get; }
        public float ClipLength { get; }
    }

    private sealed class ButtonAnimatorBinding
    {
        public ButtonAnimatorBinding(ControlPanelButton button, AnimatorTargetBinding[] targets)
        {
            Button = button;
            Targets = targets;
        }

        public ControlPanelButton Button { get; }
        public AnimatorTargetBinding[] Targets { get; }
    }

    private static ButtonAnimator inputOwner;
    private static readonly object UiHideConfirmationLockOwner = new();
    private const string PressParameterName = "isPressed";
    private const string MachineLoopSoundPath = "Sounds/POWER_bg";
    private const string ButtonSmallSoundPath = "Sounds/BTN_Small_sound";

    private Keyboard keyboard;

    private readonly Dictionary<Key, ButtonAnimatorBinding> zOnlyMap = new();
    private readonly Dictionary<Key, ButtonAnimatorBinding> zShiftMap = new();
    private readonly Dictionary<ControlPanelButton, ButtonAnimatorBinding> bindingsByButton = new();
    private readonly Dictionary<Animator, Coroutine> activeClipCoroutines = new();

    [Header("Настройки звука")]
    [Range(0f, 1f)]
    [SerializeField] private float buttonVolume = 1f;
    [SerializeField] private float simulatedButtonHoldDuration = 0.12f;

    [Header("Звук станка")]
    [Range(0f, 1f)]
    [SerializeField] private float machineVolume = 0.35f;
    [SerializeField] private float machinePitch = 0.85f;
    [SerializeField] private float machineFadeInDuration = 0.35f;
    [SerializeField] private float machineFadeOutDuration = 0.5f;

    [Header("UI Toggle")]
    [SerializeField] private string uiRootObjectName = "Canvas";
    [SerializeField] private Key uiToggleKey = Key.U;

    [Header("UI Hide Confirmation")]
    [SerializeField] private string uiHideConfirmationObjectName = "ui_hide_confirmation";
    [SerializeField] private string uiHideConfirmationTitle = "Скрыть интерфейс?";
    [TextArea(2, 5)]
    [SerializeField] private string uiHideConfirmationMessage = "Интерфейс будет скрыт, чтобы не мешать просмотру 3D-панели. Чтобы вернуть его, снова удерживайте Z и нажмите U.";
    [SerializeField] private string uiHideConfirmButtonText = "Продолжить";
    [SerializeField] private string uiHideCancelButtonText = "Отмена";

    private AudioSource audioSource;
    private AudioSource machineAudioSource;
    private AudioClip buttonSound;
    private AudioClip machineLoopSound;
    private bool machinePowered;
    private Coroutine machineAudioFadeCoroutine;
    private GameObject uiRoot;
    private CanvasGroup uiRootCanvasGroup;
    private bool uiHiddenByShortcut;
    private bool hasUiGroupSnapshot;
    private float uiGroupAlphaBeforeHide = 1f;
    private bool uiGroupInteractableBeforeHide = true;
    private bool uiGroupBlocksRaycastsBeforeHide = true;
    private GameObject uiHideConfirmationRoot;

    private void Awake()
    {
        // один владелец ввода нужен, чтобы несколько ButtonAnimator на кнопках префаба не дублировали нажатия
        if (inputOwner != null && inputOwner != this)
        {
            enabled = false;
            return;
        }

        inputOwner = this;
    }

    private void Start()
    {
        if (inputOwner != this)
            return;

        keyboard = Keyboard.current;

        RegisterButtons();
        InitializeAudio();
        SetMachinePowered(false, notifyListeners: false);
    }

    private void OnDestroy()
    {
        SimulationInputGate.Unlock(UiHideConfirmationLockOwner);
        StopAllClipAnimations();
        StopMachineAudioFade();
        StopMachineLoopAudioImmediate();

        if (inputOwner == this)
        {
            SetMachinePowered(false, notifyListeners: false);
            inputOwner = null;
        }
    }

    private void Update()
    {
        if (inputOwner != this || keyboard == null) return;

        if (SimulationInputGate.IsLocked)
        {
            ResetAllButtons();
            return;
        }

        // Z включает режим работы с панелью, а Shift переключает второй слой символов вроде +, /, =
        bool zPressed = keyboard.zKey.isPressed;
        bool shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (!zPressed)
        {
            ResetAllButtons();
            return;
        }

        if (!shiftPressed && TryHandleUiToggleShortcut())
            return;

        if (shiftPressed)
        {
            ResetButtons(zOnlyMap);
            PlayFromMap(zShiftMap);
        }
        else
        {
            ResetButtons(zShiftMap);
            PlayFromMap(zOnlyMap);
        }
    }

    private bool TryHandleUiToggleShortcut()
    {
        // скрытие UI оставлено на Z+U, чтобы можно было смотреть на 3D-панель без интерфейсных подсказок
        if (uiToggleKey == Key.None || !keyboard[uiToggleKey].wasPressedThisFrame)
            return false;

        if (uiHiddenByShortcut)
            ShowUi();
        else
            ShowUiHideConfirmation();

        return true;
    }

    private void ShowUiHideConfirmation()
    {
        if (!ResolveUiCanvasGroup())
            return;

        EnsureUiHideConfirmation();
        uiHideConfirmationRoot.transform.SetAsLastSibling();
        uiHideConfirmationRoot.SetActive(true);
        SimulationInputGate.Lock(UiHideConfirmationLockOwner);
        ResetAllButtons();
    }

    private void HideUiHideConfirmation()
    {
        if (uiHideConfirmationRoot != null)
            uiHideConfirmationRoot.SetActive(false);

        SimulationInputGate.Unlock(UiHideConfirmationLockOwner);
    }

    private void ConfirmHideUi()
    {
        HideUiHideConfirmation();
        HideUi();
    }

    private void CancelHideUi()
    {
        HideUiHideConfirmation();
    }

    private void HideUi()
    {
        if (!ResolveUiCanvasGroup())
            return;

        uiGroupAlphaBeforeHide = uiRootCanvasGroup.alpha;
        uiGroupInteractableBeforeHide = uiRootCanvasGroup.interactable;
        uiGroupBlocksRaycastsBeforeHide = uiRootCanvasGroup.blocksRaycasts;
        hasUiGroupSnapshot = true;

        uiRootCanvasGroup.alpha = 0f;
        uiRootCanvasGroup.interactable = false;
        uiRootCanvasGroup.blocksRaycasts = false;
        uiHiddenByShortcut = true;
    }

    private void ShowUi()
    {
        if (!ResolveUiCanvasGroup())
            return;

        uiRootCanvasGroup.alpha = hasUiGroupSnapshot ? uiGroupAlphaBeforeHide : 1f;
        uiRootCanvasGroup.interactable = hasUiGroupSnapshot ? uiGroupInteractableBeforeHide : true;
        uiRootCanvasGroup.blocksRaycasts = hasUiGroupSnapshot ? uiGroupBlocksRaycastsBeforeHide : true;
        uiHiddenByShortcut = false;
        hasUiGroupSnapshot = false;
    }

    private void EnsureUiHideConfirmation()
    {
        if (uiHideConfirmationRoot != null)
            return;

        RectTransform overlayRect = CreateRect(uiHideConfirmationObjectName, uiRoot.transform);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayImage = overlayRect.gameObject.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.58f);
        overlayImage.raycastTarget = true;

        RectTransform panelRect = CreateRect("panel", overlayRect);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(620f, 280f);

        Image panelImage = panelRect.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.09f, 0.1f, 0.12f, 0.98f);
        panelImage.raycastTarget = true;

        TextMeshProUGUI title = CreateText("title", panelRect, uiHideConfirmationTitle, 30f, FontStyles.Bold, TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.offsetMin = new Vector2(32f, -74f);
        title.rectTransform.offsetMax = new Vector2(-32f, -24f);

        TextMeshProUGUI message = CreateText("message", panelRect, uiHideConfirmationMessage, 20f, FontStyles.Normal, TextAlignmentOptions.Center);
        message.rectTransform.anchorMin = new Vector2(0f, 0f);
        message.rectTransform.anchorMax = new Vector2(1f, 1f);
        message.rectTransform.offsetMin = new Vector2(42f, 92f);
        message.rectTransform.offsetMax = new Vector2(-42f, -86f);

        Button confirmButton = CreateButton("confirm_button", panelRect, uiHideConfirmButtonText, new Vector2(-108f, 34f), new Color(0.88f, 0.92f, 1f, 1f), new Color(0.08f, 0.1f, 0.16f, 1f));
        confirmButton.onClick.AddListener(ConfirmHideUi);

        Button cancelButton = CreateButton("cancel_button", panelRect, uiHideCancelButtonText, new Vector2(108f, 34f), new Color(0.22f, 0.24f, 0.28f, 1f), Color.white);
        cancelButton.onClick.AddListener(CancelHideUi);

        uiHideConfirmationRoot = overlayRect.gameObject;
        uiHideConfirmationRoot.SetActive(false);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject obj = new(objectName, typeof(RectTransform));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(string objectName, Transform parent, string value, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = CreateRect(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string objectName, Transform parent, string label, Vector2 anchoredPosition, Color backgroundColor, Color textColor)
    {
        RectTransform buttonRect = CreateRect(objectName, parent);
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(180f, 46f);

        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = backgroundColor;

        Button button = buttonRect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = backgroundColor;
        colors.highlightedColor = Color.Lerp(backgroundColor, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(backgroundColor, Color.black, 0.12f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = Color.Lerp(backgroundColor, Color.gray, 0.45f);
        button.colors = colors;

        TextMeshProUGUI buttonText = CreateText("label", buttonRect, label, 19f, FontStyles.Bold, TextAlignmentOptions.Center);
        buttonText.color = textColor;
        buttonText.rectTransform.anchorMin = Vector2.zero;
        buttonText.rectTransform.anchorMax = Vector2.one;
        buttonText.rectTransform.offsetMin = Vector2.zero;
        buttonText.rectTransform.offsetMax = Vector2.zero;

        return button;
    }

    private bool ResolveUiCanvasGroup()
    {
        if (uiRoot == null)
        {
            if (!string.IsNullOrWhiteSpace(uiRootObjectName))
                uiRoot = GameObject.Find(uiRootObjectName);

            if (uiRoot == null)
            {
                Canvas canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
                uiRoot = canvas != null ? canvas.gameObject : null;
            }
        }

        if (uiRoot == null)
        {
            Debug.LogWarning("Корневой объект UI не найден, переключение видимости UI невозможно.");
            return false;
        }

        if (uiRootCanvasGroup == null)
            uiRootCanvasGroup = uiRoot.GetComponent<CanvasGroup>();

        if (uiRootCanvasGroup == null)
            uiRootCanvasGroup = uiRoot.AddComponent<CanvasGroup>();

        return true;
    }

    private void RegisterButtons()
    {
        // здесь только поиск 3D-кнопок и их Animator, а раскладка в ControlPanelInputLayout 
        foreach (ControlPanelBinding binding in ControlPanelInputLayout.Bindings)
        {
            Dictionary<Key, ButtonAnimatorBinding> map = binding.RequiresShift ? zShiftMap : zOnlyMap;
            Register(map, binding);
        }
    }

    private void Register(Dictionary<Key, ButtonAnimatorBinding> map, ControlPanelBinding binding)
    {
        IReadOnlyList<string> objectNames = ControlPanelInputLayout.GetObjectNames(binding.Button);
        if (objectNames.Count == 0)
            return;

        AnimatorPlaybackMode playbackMode = ControlPanelInputLayout.UsesOneShotClip(binding.Button)
            ? AnimatorPlaybackMode.OneShotClip
            : AnimatorPlaybackMode.HoldBool;

        List<AnimatorTargetBinding> targets = new();

        foreach (string objectName in objectNames)
        {
            GameObject obj = GameObject.Find(objectName);

            if (obj == null)
            {
                Debug.LogWarning($"Объект '{objectName}' не найден.");
                continue;
            }

            Animator animator = obj.GetComponent<Animator>();

            if (animator == null)
            {
                Debug.LogWarning($"На объекте '{objectName}' нет компонента Animator.");
                continue;
            }

            if (playbackMode == AnimatorPlaybackMode.OneShotClip)
                PrepareOneShotAnimator(animator);

            string stateName = ResolvePrimaryStateName(animator);
            float clipLength = ResolvePrimaryClipLength(animator);

            targets.Add(new AnimatorTargetBinding(animator, playbackMode, stateName, clipLength));
        }

        if (targets.Count == 0)
            return;

        ButtonAnimatorBinding buttonBinding = new(binding.Button, targets.ToArray());
        map[binding.Key] = buttonBinding;
        bindingsByButton[binding.Button] = buttonBinding;
    }

    private void PlayFromMap(Dictionary<Key, ButtonAnimatorBinding> map)
    {
        foreach (var pair in map)
        {
            Key key = pair.Key;
            ButtonAnimatorBinding binding = pair.Value;

            if (keyboard[key].wasPressedThisFrame)
            {
                if (!CanHandleButton(binding.Button))
                    continue;

                // порядок важен: сначала визуально нажимаем кнопку, потом звук/спецдействие, затем событие для остальных систем
                PressBinding(binding);
                PlayButtonSound();
                HandleSpecialButtons(binding.Button);
                ButtonPressed?.Invoke(binding.Button);
            }

            if (keyboard[key].wasReleasedThisFrame)
            {
                ReleaseBinding(binding);
            }
        }
    }

    private void ResetAllButtons()
    {
        ResetButtons(zOnlyMap);
        ResetButtons(zShiftMap);
    }

    private void ResetButtons(Dictionary<Key, ButtonAnimatorBinding> map)
    {
        foreach (ButtonAnimatorBinding binding in map.Values)
        {
            ReleaseBinding(binding);
        }
    }

    private void InitializeAudio()
    {
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.volume = 1f;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.loop = false;

        machineAudioSource = gameObject.AddComponent<AudioSource>();
        machineAudioSource.playOnAwake = false;
        machineAudioSource.spatialBlend = 0f;
        machineAudioSource.loop = true;
        machineAudioSource.volume = 0f;
        machineAudioSource.pitch = machinePitch;

        buttonSound = Resources.Load<AudioClip>(ButtonSmallSoundPath);
        machineLoopSound = Resources.Load<AudioClip>(MachineLoopSoundPath);

        if (buttonSound == null) Debug.LogError($"Не найден звук: Resources/{ButtonSmallSoundPath}");
        if (machineLoopSound == null) Debug.LogWarning($"Не найден звук: Resources/{MachineLoopSoundPath}");

        machineAudioSource.clip = machineLoopSound;
    }

    private void PlayButtonSound()
    {
        if (audioSource == null || buttonSound == null) return;
        audioSource.PlayOneShot(buttonSound, buttonVolume);
    }

    private void PressBinding(ButtonAnimatorBinding binding)
    {
        foreach (AnimatorTargetBinding target in binding.Targets)
        {
            if (target.Animator == null)
                continue;

            if (target.PlaybackMode == AnimatorPlaybackMode.HoldBool)
            {
                target.Animator.SetBool(PressParameterName, true);
                continue;
            }

            PlayOneShotClip(target);
        }
    }

    private void ReleaseBinding(ButtonAnimatorBinding binding)
    {
        foreach (AnimatorTargetBinding target in binding.Targets)
        {
            if (target.Animator == null || target.PlaybackMode != AnimatorPlaybackMode.HoldBool)
                continue;

            target.Animator.SetBool(PressParameterName, false);
        }
    }

    private void HandleSpecialButtons(ControlPanelButton button)
    {
        // питание меняется здесь, потому что именно кнопки панели являются источником состояния станка
        if (button == ControlPanelButton.PowerSwitch)
        {
            SetMachinePowered(!machinePowered, notifyListeners: true);
            return;
        }

        if (button == ControlPanelButton.EmergencyStop)
        {
            EmergencyStopPressed?.Invoke();
            SetMachinePowered(false, notifyListeners: true);
        }
    }

    private bool CanHandleButton(ControlPanelButton button)
    {
        if (!PracticeTasksPopupController.IsButtonInteractionAllowed(button))
            return false;

        return machinePowered || IsAlwaysAvailableButton(button);
    }

    private static bool IsAlwaysAvailableButton(ControlPanelButton button)
    {
        return button == ControlPanelButton.PowerSwitch || button == ControlPanelButton.EmergencyStop;
    }

    public static bool TrySimulateButtonPress(ControlPanelButton button)
    {
        // используется практикой для аккуратного выключения станка тем же путём, что и реальное нажатие кнопки
        return inputOwner != null && inputOwner.TrySimulateButtonPressInternal(button);
    }

    private bool TrySimulateButtonPressInternal(ControlPanelButton button)
    {
        if (SimulationInputGate.IsLocked)
            return false;

        if (!bindingsByButton.TryGetValue(button, out ButtonAnimatorBinding binding))
            return false;

        if (!CanHandleButton(button))
            return false;

        StartCoroutine(SimulateButtonPressRoutine(binding));
        return true;
    }

    private void SetMachinePowered(bool powered, bool notifyListeners)
    {
        bool changed = machinePowered != powered;

        machinePowered = powered;
        IsMachinePowered = powered;

        if (machinePowered)
            StartMachineLoopAudio();
        else
            StopMachineLoopAudio();

        // при выключении все удерживаемые кнопки отпускаются, чтобы анимации не зависли в нажатом состоянии
        if (changed && !machinePowered)
            ResetAllButtons();

        if (notifyListeners && changed)
            MachinePowerChanged?.Invoke(machinePowered);
    }

    private IEnumerator SimulateButtonPressRoutine(ButtonAnimatorBinding binding)
    {
        PressBinding(binding);
        PlayButtonSound();
        HandleSpecialButtons(binding.Button);
        ButtonPressed?.Invoke(binding.Button);

        if (!UsesHoldBool(binding))
            yield break;

        float holdDuration = Mathf.Max(0.02f, simulatedButtonHoldDuration);
        yield return new WaitForSeconds(holdDuration);
        ReleaseBinding(binding);
    }

    private static bool UsesHoldBool(ButtonAnimatorBinding binding)
    {
        if (binding == null || binding.Targets == null || binding.Targets.Length == 0)
            return false;

        for (int i = 0; i < binding.Targets.Length; i++)
        {
            if (binding.Targets[i].PlaybackMode == AnimatorPlaybackMode.HoldBool)
                return true;
        }

        return false;
    }

    private void PlayOneShotClip(AnimatorTargetBinding target)
    {
        if (target.Animator == null || string.IsNullOrEmpty(target.StateName) || target.ClipLength <= 0f)
            return;

        if (activeClipCoroutines.TryGetValue(target.Animator, out Coroutine runningCoroutine) && runningCoroutine != null)
            StopCoroutine(runningCoroutine);

        activeClipCoroutines[target.Animator] = StartCoroutine(PlayClipRoutine(target));
    }

    private IEnumerator PlayClipRoutine(AnimatorTargetBinding target)
    {
        Animator animator = target.Animator;

        animator.enabled = true;
        animator.SetLayerWeight(0, 1f);
        animator.speed = 1f;
        animator.Play(target.StateName, 0, 0f);
        animator.Update(0f);

        yield return new WaitForSeconds(target.ClipLength);

        PrepareOneShotAnimator(animator);
        activeClipCoroutines.Remove(animator);
    }

    private void StopAllClipAnimations()
    {
        foreach (Coroutine coroutine in activeClipCoroutines.Values)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }

        activeClipCoroutines.Clear();
    }

    private static void PrepareOneShotAnimator(Animator animator)
    {
        if (animator == null)
            return;

        // one-shot кнопки должны стоять на первом кадре и проигрываться только при нажатии, а не циклом
        animator.enabled = true;
        animator.SetLayerWeight(0, 1f);
        animator.speed = 0f;
        animator.Rebind();
        animator.Update(0f);
    }

    private static string ResolvePrimaryStateName(Animator animator)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null || controller.animationClips.Length == 0)
            return string.Empty;

        return controller.animationClips[0].name;
    }

    private static float ResolvePrimaryClipLength(Animator animator)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null || controller.animationClips.Length == 0)
            return 0f;

        return controller.animationClips[0].length;
    }

    private void StartMachineLoopAudio()
    {
        if (machineAudioSource == null || machineLoopSound == null)
            return;

        StopMachineAudioFade();

        machineAudioSource.pitch = machinePitch;

        if (!machineAudioSource.isPlaying)
        {
            machineAudioSource.volume = 0f;
            machineAudioSource.Play();
        }

        machineAudioFadeCoroutine = StartCoroutine(FadeMachineLoopAudio(machineVolume, machineFadeInDuration, stopAfterFade: false));
    }

    private void StopMachineLoopAudio()
    {
        if (machineAudioSource == null)
            return;

        StopMachineAudioFade();

        if (!machineAudioSource.isPlaying)
        {
            machineAudioSource.volume = 0f;
            return;
        }

        machineAudioFadeCoroutine = StartCoroutine(FadeMachineLoopAudio(0f, machineFadeOutDuration, stopAfterFade: true));
    }

    private IEnumerator FadeMachineLoopAudio(float targetVolume, float duration, bool stopAfterFade)
    {
        if (machineAudioSource == null)
            yield break;

        float startVolume = machineAudioSource.volume;
        float clampedTarget = Mathf.Clamp01(targetVolume);

        if (duration <= 0f)
        {
            machineAudioSource.volume = clampedTarget;

            if (stopAfterFade && Mathf.Approximately(clampedTarget, 0f))
                machineAudioSource.Stop();

            machineAudioFadeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            machineAudioSource.volume = Mathf.Lerp(startVolume, clampedTarget, t);
            yield return null;
        }

        machineAudioSource.volume = clampedTarget;

        if (stopAfterFade && Mathf.Approximately(clampedTarget, 0f))
            machineAudioSource.Stop();

        machineAudioFadeCoroutine = null;
    }

    private void StopMachineAudioFade()
    {
        if (machineAudioFadeCoroutine == null)
            return;

        StopCoroutine(machineAudioFadeCoroutine);
        machineAudioFadeCoroutine = null;
    }

    private void StopMachineLoopAudioImmediate()
    {
        if (machineAudioSource == null) return;
        machineAudioSource.volume = 0f;
        machineAudioSource.Stop();
    }
}
