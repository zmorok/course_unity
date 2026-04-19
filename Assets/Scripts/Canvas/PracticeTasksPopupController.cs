using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
[RequireComponent(typeof(Button))]
public class PracticeTasksPopupController : MonoBehaviour
{
    private static PracticeTasksPopupController activeInstance;
    private static Sprite fallbackSprite;

    private static readonly string[] TaskLabels =
    {
        "Задание 1",
        "Задание 2",
        "Задание 3",
        "Задание 4",
        "Задание 5",
        "Задание 6",
        "Задание 7",
        "Задание 8",
        "Задание 9"
    };

    [Header("Dropdown")]
    [SerializeField] private string dropdownObjectName = "practice_tasks_column";
    [SerializeField] private float dropdownSpacing = 8f;
    [SerializeField] private float dropdownButtonHeight = 42f;
    [SerializeField] private float dropdownPadding = 10f;
    [SerializeField] private float dropdownTopOffset = 6f;

    [Header("Progress")]
    [SerializeField] private bool resetProgressOnEnable = true;

    [Header("Task 5")]
    [SerializeField] private ControlPanelButton[] requiredCommandSequence =
    {
        ControlPanelButton.Digit1,
        ControlPanelButton.Digit0,
        ControlPanelButton.Digit0,
        ControlPanelButton.Enter
    };

    [Header("Paper Targets")]
    [SerializeField] private int task2TargetPaperPointIndex = 1;
    [SerializeField] private int task3TargetPaperPointIndex = 3;

    private Button practiceButton;
    private RectTransform practiceRect;
    private RectTransform dropdownRect;
    private TMP_FontAsset fontAsset;
    private BottomInfoPanel infoPanel;
    private PaperPathMover paperMover;
    private CUT_Animator cutter;

    private int highestUnlockedTask = 1;
    private int activeTaskIndex;
    private int commandSequenceProgress;
    private bool cutObservedDuringActiveTask;
    private bool runtimeSubscribed;

    private void OnEnable()
    {
        activeInstance = this;
        practiceButton = GetComponent<Button>();
        practiceRect = GetComponent<RectTransform>();
        fontAsset = ResolveFontAsset();
        ResolveInfoPanel();
        ResolveSceneControllers();

        if (resetProgressOnEnable)
        {
            highestUnlockedTask = 1;
            activeTaskIndex = 0;
            commandSequenceProgress = 0;
            cutObservedDuringActiveTask = false;
        }
        else
        {
            highestUnlockedTask = Mathf.Clamp(highestUnlockedTask, 1, TaskLabels.Length + 1);
            activeTaskIndex = Mathf.Clamp(activeTaskIndex, 0, TaskLabels.Length);
        }

        if (practiceButton != null)
        {
            practiceButton.onClick.RemoveListener(ToggleWindow);
            practiceButton.onClick.AddListener(ToggleWindow);
        }

        EnsureDropdown();

        if (!Application.isPlaying && dropdownRect != null)
            dropdownRect.gameObject.SetActive(false);

        SubscribeRuntimeEvents();
        UpdateButtonStates();
        RefreshLayout();
    }

    private void OnDisable()
    {
        if (practiceButton != null)
            practiceButton.onClick.RemoveListener(ToggleWindow);

        UnsubscribeRuntimeEvents();

        if (activeInstance == this)
            activeInstance = null;
    }

    private void Update()
    {
        TryHandleResetShortcut();

        if (!Application.isPlaying)
            return;

        if (activeTaskIndex <= 0)
            return;

        ResolveSceneControllers();
        TryCompleteActiveTaskFromState();
    }

    private void TryHandleResetShortcut()
    {
        if (!Application.isPlaying)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!keyboard.rKey.isPressed || !keyboard.tKey.wasPressedThisFrame)
            return;

        ResetPracticeToInitialState();
    }

    private void OnTransformParentChanged()
    {
        EnsureDropdown();
        RefreshLayout();
    }

    private void OnValidate()
    {
        practiceRect = GetComponent<RectTransform>();
        fontAsset = ResolveFontAsset();
        highestUnlockedTask = Mathf.Clamp(highestUnlockedTask, 1, TaskLabels.Length + 1);
        activeTaskIndex = Mathf.Clamp(activeTaskIndex, 0, TaskLabels.Length);

        EnsureDropdown();
        UpdateButtonStates();
        RefreshLayout();
    }

    public void ToggleWindow()
    {
        EnsureDropdown();

        if (dropdownRect == null)
            return;

        dropdownRect.gameObject.SetActive(!dropdownRect.gameObject.activeSelf);
        RefreshDropdownPosition();
        RefreshLayout();
    }

    public void ShowWindow()
    {
        EnsureDropdown();

        if (dropdownRect == null)
            return;

        dropdownRect.gameObject.SetActive(true);
        RefreshDropdownPosition();
        RefreshLayout();
    }

    public void HideWindow()
    {
        if (dropdownRect == null)
            return;

        dropdownRect.gameObject.SetActive(false);
        RefreshLayout();
    }

    private void SubscribeRuntimeEvents()
    {
        if (!Application.isPlaying || runtimeSubscribed)
            return;

        btn_Animator.ButtonPressed += HandlePanelButtonPressed;
        btn_Animator.MachinePowerChanged += HandleMachinePowerChanged;
        CUT_Animator.CuttingStateChanged += HandleCuttingStateChanged;
        runtimeSubscribed = true;
    }

    private void UnsubscribeRuntimeEvents()
    {
        if (!runtimeSubscribed)
            return;

        btn_Animator.ButtonPressed -= HandlePanelButtonPressed;
        btn_Animator.MachinePowerChanged -= HandleMachinePowerChanged;
        CUT_Animator.CuttingStateChanged -= HandleCuttingStateChanged;
        runtimeSubscribed = false;
    }

    private void EnsureDropdown()
    {
        if (practiceRect == null || practiceRect.parent == null)
            return;

        RectTransform existingDropdown = FindDropdown();
        bool created = false;

        if (existingDropdown == null)
        {
            existingDropdown = CreateRectTransform(dropdownObjectName, practiceRect);
            created = true;
        }

        dropdownRect = existingDropdown;

        ConfigureDropdownContainer(dropdownRect);
        EnsureDropdownButtons(dropdownRect, ref created);
        RefreshDropdownPosition();
        UpdateButtonStates();

        if (created)
            MarkSceneDirty();
    }

    private RectTransform FindDropdown()
    {
        Transform existing = practiceRect.Find(dropdownObjectName);
        return existing as RectTransform;
    }

    private void ConfigureDropdownContainer(RectTransform targetDropdownRect)
    {
        targetDropdownRect.anchorMin = new Vector2(0f, 1f);
        targetDropdownRect.anchorMax = new Vector2(1f, 1f);
        targetDropdownRect.pivot = new Vector2(0.5f, 1f);
        targetDropdownRect.localScale = Vector3.one;
        targetDropdownRect.localRotation = Quaternion.identity;
        targetDropdownRect.sizeDelta = new Vector2(0f, CalculateDropdownHeight());

        Image dropdownImage = GetOrAddComponent<Image>(targetDropdownRect.gameObject, out _);
        dropdownImage.sprite = null;
        dropdownImage.type = Image.Type.Simple;
        dropdownImage.color = new Color(1f, 1f, 1f, 0f);
        dropdownImage.raycastTarget = false;

        Outline dropdownOutline = targetDropdownRect.GetComponent<Outline>();
        if (dropdownOutline != null)
            DestroyImmediateSafe(dropdownOutline);

        VerticalLayoutGroup layoutGroup = GetOrAddComponent<VerticalLayoutGroup>(targetDropdownRect.gameObject, out _);
        layoutGroup.padding = new RectOffset(
            Mathf.RoundToInt(dropdownPadding),
            Mathf.RoundToInt(dropdownPadding),
            Mathf.RoundToInt(dropdownPadding),
            Mathf.RoundToInt(dropdownPadding));
        layoutGroup.spacing = dropdownSpacing;
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false;

        LayoutElement rootLayout = targetDropdownRect.GetComponent<LayoutElement>();
        if (rootLayout != null)
            DestroyImmediateSafe(rootLayout);
    }

    private void EnsureDropdownButtons(RectTransform targetDropdownRect, ref bool created)
    {
        for (int i = 0; i < TaskLabels.Length; i++)
        {
            int taskIndex = i + 1;
            string objectName = $"task_{taskIndex}";

            Transform existing = targetDropdownRect.Find(objectName);
            RectTransform buttonRect = existing as RectTransform;

            if (buttonRect == null)
            {
                buttonRect = CreateRectTransform(objectName, targetDropdownRect);
                created = true;
            }

            ConfigureTaskButton(buttonRect, TaskLabels[i], taskIndex);
        }
    }

    private void ConfigureTaskButton(RectTransform buttonRect, string label, int taskIndex)
    {
        LayoutElement hostLayout = GetComponent<LayoutElement>();
        LayoutElement layout = GetOrAddComponent<LayoutElement>(buttonRect.gameObject, out _);
        layout.minWidth = hostLayout != null ? hostLayout.minWidth : -1f;
        layout.minHeight = hostLayout != null ? hostLayout.minHeight : -1f;
        layout.preferredWidth = hostLayout != null ? hostLayout.preferredWidth : -1f;
        layout.preferredHeight = hostLayout != null && hostLayout.preferredHeight > 0f
            ? hostLayout.preferredHeight
            : dropdownButtonHeight;
        layout.flexibleWidth = hostLayout != null ? hostLayout.flexibleWidth : -1f;
        layout.flexibleHeight = hostLayout != null ? hostLayout.flexibleHeight : -1f;
        layout.layoutPriority = hostLayout != null ? hostLayout.layoutPriority : 1;

        Image sourceImage = ResolveSourceImage();
        Image buttonImage = GetOrAddComponent<Image>(buttonRect.gameObject, out _);
        if (sourceImage != null)
        {
            buttonImage.sprite = sourceImage.sprite;
            buttonImage.type = sourceImage.type;
            buttonImage.preserveAspect = sourceImage.preserveAspect;
            buttonImage.fillCenter = sourceImage.fillCenter;
            buttonImage.fillMethod = sourceImage.fillMethod;
            buttonImage.fillAmount = sourceImage.fillAmount;
            buttonImage.fillClockwise = sourceImage.fillClockwise;
            buttonImage.fillOrigin = sourceImage.fillOrigin;
            buttonImage.useSpriteMesh = sourceImage.useSpriteMesh;
            buttonImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
            buttonImage.material = sourceImage.material;
            buttonImage.color = sourceImage.color;
        }
        else
        {
            buttonImage.sprite = GetFallbackSprite();
            buttonImage.type = Image.Type.Simple;
            buttonImage.color = Color.white;
        }

        Button button = GetOrAddComponent<Button>(buttonRect.gameObject, out _);
        button.transition = practiceButton != null ? practiceButton.transition : Selectable.Transition.ColorTint;
        button.colors = practiceButton != null ? practiceButton.colors : button.colors;
        button.spriteState = practiceButton != null ? practiceButton.spriteState : button.spriteState;
        button.navigation = practiceButton != null ? practiceButton.navigation : button.navigation;
        button.targetGraphic = buttonImage;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => HandleTaskClicked(taskIndex));

        RectTransform textRect = buttonRect.Find("label") as RectTransform;
        if (textRect == null)
            textRect = CreateRectTransform("label", buttonRect);

        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 0f);
        textRect.offsetMax = new Vector2(-12f, 0f);

        TextMeshProUGUI buttonText = GetOrAddComponent<TextMeshProUGUI>(textRect.gameObject, out _);
        TextMeshProUGUI sourceLabel = ResolveSourceLabel();
        buttonText.font = sourceLabel != null ? sourceLabel.font : fontAsset;
        buttonText.fontSharedMaterial = sourceLabel != null ? sourceLabel.fontSharedMaterial : buttonText.fontSharedMaterial;
        buttonText.text = label;
        buttonText.fontSize = sourceLabel != null ? sourceLabel.fontSize : 24f;
        buttonText.enableAutoSizing = sourceLabel != null && sourceLabel.enableAutoSizing;
        buttonText.fontSizeMin = sourceLabel != null ? sourceLabel.fontSizeMin : buttonText.fontSizeMin;
        buttonText.fontSizeMax = sourceLabel != null ? sourceLabel.fontSizeMax : buttonText.fontSizeMax;
        buttonText.alignment = sourceLabel != null ? sourceLabel.alignment : TextAlignmentOptions.Center;
        buttonText.color = sourceLabel != null ? sourceLabel.color : new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
        buttonText.raycastTarget = false;
        buttonText.margin = sourceLabel != null ? sourceLabel.margin : buttonText.margin;
    }

    private void HandleTaskClicked(int taskIndex)
    {
        if (taskIndex < 1 || taskIndex > TaskLabels.Length)
            return;

        if (taskIndex != highestUnlockedTask || highestUnlockedTask > TaskLabels.Length)
            return;

        ResolveInfoPanel();
        ResolveSceneControllers();
        ActivateTask(taskIndex);
    }

    private void ActivateTask(int taskIndex)
    {
        activeTaskIndex = taskIndex;
        commandSequenceProgress = 0;
        cutObservedDuringActiveTask = false;

        ShowTaskInstruction(taskIndex);
        UpdateButtonStates();
        TryCompleteActiveTaskFromState();
    }

    public static bool IsButtonInteractionAllowed(ControlPanelButton button)
    {
        if (!Application.isPlaying || activeInstance == null)
            return true;

        return activeInstance.IsButtonInteractionAllowedInternal(button);
    }

    public static bool IsPaperAdvanceAllowed()
    {
        if (!Application.isPlaying || activeInstance == null)
            return true;

        return activeInstance.IsPaperAdvanceAllowedInternal();
    }

    public static bool IsCutStartAllowed()
    {
        if (!Application.isPlaying || activeInstance == null)
            return true;

        return activeInstance.IsCutStartAllowedInternal();
    }

    private void HandlePanelButtonPressed(ControlPanelButton button)
    {
        if (!Application.isPlaying || activeTaskIndex != 5)
            return;

        if (requiredCommandSequence == null || requiredCommandSequence.Length == 0)
            return;

        ControlPanelButton expectedButton = requiredCommandSequence[commandSequenceProgress];
        if (button == expectedButton)
        {
            commandSequenceProgress++;

            if (commandSequenceProgress >= requiredCommandSequence.Length)
            {
                TryCompleteActiveTaskFromState();
            }
            else
            {
                ShowTaskInstruction(
                    activeTaskIndex,
                    $"Прогресс ввода: {commandSequenceProgress}/{requiredCommandSequence.Length}");
            }

            return;
        }

        if (!IsTask5RelevantButton(button))
            return;

        commandSequenceProgress = 0;
        ShowTaskInstruction(
            activeTaskIndex,
            "Команда введена неверно. Начните заново: 1, 0, 0, Enter.");
    }

    private void HandleMachinePowerChanged(bool isPowered)
    {
        if (!Application.isPlaying || activeTaskIndex <= 0)
            return;

        TryCompleteActiveTaskFromState();
    }

    private void HandleCuttingStateChanged(bool isCutting)
    {
        if (!Application.isPlaying || activeTaskIndex != 6)
            return;

        if (isCutting)
            cutObservedDuringActiveTask = true;

        TryCompleteActiveTaskFromState();
    }

    private void TryCompleteActiveTaskFromState()
    {
        if (activeTaskIndex <= 0)
            return;

        bool completed = activeTaskIndex switch
        {
            1 => btn_Animator.IsMachinePowered,
            2 => paperMover != null && paperMover.CurrentPaperPointIndex >= task2TargetPaperPointIndex,
            3 => paperMover != null && paperMover.CurrentPaperPointIndex >= task3TargetPaperPointIndex,
            4 => paperMover != null && paperMover.CurrentPaperPointIndex >= paperMover.CutWaitPaperPointIndex,
            5 => requiredCommandSequence != null &&
                 requiredCommandSequence.Length > 0 &&
                 commandSequenceProgress >= requiredCommandSequence.Length,
            6 => (cutObservedDuringActiveTask && !CUT_Animator.IsCutting) ||
                 (cutter != null && cutter.CutCompleted) ||
                 (paperMover != null && (paperMover.IsSecStage || paperMover.IsMainStage || paperMover.IsFinished)),
            7 => paperMover != null && (paperMover.IsMainStage || paperMover.IsFinished),
            8 => paperMover != null && paperMover.IsFinished,
            9 => !btn_Animator.IsMachinePowered,
            _ => false
        };

        if (!completed)
            return;

        CompleteActiveTask(activeTaskIndex);
    }

    private bool IsPracticeFlowActive()
    {
        return activeTaskIndex > 0 || (highestUnlockedTask > 1 && highestUnlockedTask <= TaskLabels.Length);
    }

    private bool IsButtonInteractionAllowedInternal(ControlPanelButton button)
    {
        if (!IsPracticeFlowActive())
            return true;

        if (button == ControlPanelButton.EmergencyStop)
            return true;

        if (activeTaskIndex <= 0)
            return false;

        return activeTaskIndex switch
        {
            1 => button == ControlPanelButton.PowerSwitch,
            5 => button != ControlPanelButton.PowerSwitch && button != ControlPanelButton.DualStart,
            6 => button == ControlPanelButton.DualStart,
            9 => button == ControlPanelButton.PowerSwitch,
            _ => false
        };
    }

    private bool IsPaperAdvanceAllowedInternal()
    {
        if (!IsPracticeFlowActive())
            return true;

        if (activeTaskIndex <= 0 || paperMover == null)
            return false;

        return activeTaskIndex switch
        {
            2 => paperMover.CurrentPaperPointIndex < task2TargetPaperPointIndex,
            3 => paperMover.CurrentPaperPointIndex < task3TargetPaperPointIndex,
            4 => paperMover.CurrentPaperPointIndex < paperMover.CutWaitPaperPointIndex,
            7 => !paperMover.IsMainStage && !paperMover.IsFinished,
            8 => !paperMover.IsFinished,
            _ => false
        };
    }

    private bool IsCutStartAllowedInternal()
    {
        if (!IsPracticeFlowActive())
            return true;

        if (activeTaskIndex <= 0)
            return false;

        return activeTaskIndex == 6;
    }

    private void CompleteActiveTask(int taskIndex)
    {
        activeTaskIndex = 0;
        commandSequenceProgress = 0;
        cutObservedDuringActiveTask = false;
        highestUnlockedTask = Mathf.Clamp(taskIndex + 1, 1, TaskLabels.Length + 1);

        ResolveInfoPanel();
        if (infoPanel != null)
            infoPanel.ShowInfo(BuildCompletionText(taskIndex));

        UpdateButtonStates();
    }

    public void ResetPracticeToInitialState()
    {
        activeTaskIndex = 0;
        commandSequenceProgress = 0;
        cutObservedDuringActiveTask = false;
        highestUnlockedTask = 1;

        ResolveSceneControllers();

        if (paperMover != null)
            paperMover.ResetPaperToStart();

        if (cutter != null)
            cutter.ResetCutState();

        if (btn_Animator.IsMachinePowered)
            btn_Animator.TrySimulateButtonPress(ControlPanelButton.PowerSwitch);

        ResolveInfoPanel();
        infoPanel?.ShowDefault();
        UpdateButtonStates();
    }

    private string BuildInstructionText(int taskIndex)
    {
        return taskIndex switch
        {
            1 => "Задание 1. Запустить станок.\nКак выполнить: удерживайте Z и нажмите O, чтобы включить питание станка.",
            2 => "Задание 2. Достать бумагу.\nКак выполнить: удерживайте N и нажмите M один раз, чтобы достать бумагу из исходного положения.",
            3 => "Задание 3. Положить бумагу на стол.\nКак выполнить: удерживайте N и нажмите M два раза, чтобы перевести бумагу из положения в воздухе на стол.",
            4 => "Задание 4. Разместить бумагу в зоне реза.\nКак выполнить: удерживайте N и нажмите M ещё раз, чтобы переместить бумагу со стола в зону реза.",
            5 => "Задание 5. Ввести команду на панели управления.\nКак выполнить: удерживайте Z и последовательно нажмите 1, 0, 0, затем Enter.",
            6 => "Задание 6. Запустить рез бумаги.\nКак выполнить: удерживайте Z и нажмите V. Эта комбинация запускает двойной пуск и сам рез одним действием.",
            7 => "Задание 7. Убрать отрезанную часть sec.\nКак выполнить: удерживайте N и нажимайте M, пока часть sec не уйдёт в левый мусорный бокс.",
            8 => "Задание 8. Убрать нужную часть main.\nКак выполнить: удерживайте N и нажимайте M, пока часть main не уйдёт в правый ящик.",
            9 => "Задание 9. Выключить станок.\nКак выполнить: удерживайте Z и нажмите O ещё раз, чтобы выключить питание.",
            _ => string.Empty
        };
    }

    private string BuildCompletionText(int taskIndex)
    {
        if (taskIndex >= TaskLabels.Length)
            return "Все задания практики выполнены.";

        return $"{TaskLabels[taskIndex - 1]} выполнено.\n\nТеперь доступно {TaskLabels[taskIndex]}.";
    }

    private void ShowTaskInstruction(int taskIndex, string extraLine = null)
    {
        ResolveInfoPanel();
        if (infoPanel == null)
            return;

        string message = BuildInstructionText(taskIndex);

        if (!string.IsNullOrWhiteSpace(extraLine))
            message += $"\n\n{extraLine}";

        infoPanel.ShowInfo(message);
    }

    private static bool IsTask5RelevantButton(ControlPanelButton button)
    {
        return button is not ControlPanelButton.PowerSwitch and
               not ControlPanelButton.EmergencyStop and
               not ControlPanelButton.DualStart;
    }

    private float CalculateDropdownHeight()
    {
        float buttonsHeight = TaskLabels.Length * dropdownButtonHeight;
        float spacingHeight = Mathf.Max(0, TaskLabels.Length - 1) * dropdownSpacing;
        return buttonsHeight + spacingHeight + dropdownPadding * 2f;
    }

    private void RefreshLayout()
    {
        if (practiceRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        RefreshDropdownPosition();
        LayoutRebuilder.ForceRebuildLayoutImmediate(practiceRect);

        if (dropdownRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(dropdownRect);
    }

    private void RefreshDropdownPosition()
    {
        if (practiceRect == null || dropdownRect == null)
            return;

        float hostHeight = GetHostHeight();
        dropdownRect.anchoredPosition = new Vector2(0f, -(hostHeight + dropdownTopOffset));
    }

    private float GetHostHeight()
    {
        LayoutElement layoutElement = GetComponent<LayoutElement>();
        if (layoutElement != null && layoutElement.preferredHeight > 0f)
            return layoutElement.preferredHeight;

        if (practiceRect != null && practiceRect.rect.height > 0f)
            return practiceRect.rect.height;

        return dropdownButtonHeight;
    }

    private static RectTransform CreateRectTransform(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.layer = LayerMask.NameToLayer("UI");

        RectTransform rectTransform = uiObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        return rectTransform;
    }

    private static T GetOrAddComponent<T>(GameObject target, out bool created) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
        {
            component = target.AddComponent<T>();
            created = true;
            return component;
        }

        created = false;
        return component;
    }

    private static void DestroyImmediateSafe(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private void MarkSceneDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    private void ResolveInfoPanel()
    {
        if (infoPanel == null)
            infoPanel = Object.FindFirstObjectByType<BottomInfoPanel>();
    }

    private void ResolveSceneControllers()
    {
        if (paperMover == null)
            paperMover = Object.FindFirstObjectByType<PaperPathMover>();

        if (cutter == null)
            cutter = Object.FindFirstObjectByType<CUT_Animator>();
    }

    private void UpdateButtonStates()
    {
        if (dropdownRect == null)
            return;

        TextMeshProUGUI sourceLabel = ResolveSourceLabel();
        Color enabledTextColor = sourceLabel != null
            ? sourceLabel.color
            : new Color(0.19607843f, 0.19607843f, 0.19607843f, 1f);
        Color completedTextColor = new(enabledTextColor.r, enabledTextColor.g, enabledTextColor.b, 0.78f);
        Color lockedTextColor = new(enabledTextColor.r, enabledTextColor.g, enabledTextColor.b, 0.55f);
        Color activeTextColor = Color.Lerp(enabledTextColor, Color.white, 0.2f);

        for (int i = 0; i < TaskLabels.Length; i++)
        {
            int taskIndex = i + 1;
            Transform taskTransform = dropdownRect.Find($"task_{taskIndex}");
            if (taskTransform == null)
                continue;

            Button button = taskTransform.GetComponent<Button>();
            TextMeshProUGUI label = taskTransform.GetComponentInChildren<TextMeshProUGUI>(true);

            bool isCompletedTask = taskIndex < highestUnlockedTask;
            bool isAvailableTask = taskIndex == highestUnlockedTask && highestUnlockedTask <= TaskLabels.Length;
            bool isActiveTask = taskIndex == activeTaskIndex;

            if (button != null)
                button.interactable = isAvailableTask;

            if (label == null)
                continue;

            if (isActiveTask)
                label.color = activeTextColor;
            else if (isCompletedTask)
                label.color = completedTextColor;
            else if (isAvailableTask)
                label.color = enabledTextColor;
            else
                label.color = lockedTextColor;
        }
    }

    private static TMP_FontAsset ResolveFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private Image ResolveSourceImage()
    {
        if (practiceButton != null && practiceButton.targetGraphic is Image targetImage)
            return targetImage;

        return GetComponent<Image>();
    }

    private TextMeshProUGUI ResolveSourceLabel()
    {
        if (practiceRect == null)
            return null;

        return practiceRect.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private static Sprite GetFallbackSprite()
    {
        if (fallbackSprite != null)
            return fallbackSprite;

        fallbackSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        fallbackSprite.name = "PracticeTasksFallbackSprite";
        fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
        return fallbackSprite;
    }
}
