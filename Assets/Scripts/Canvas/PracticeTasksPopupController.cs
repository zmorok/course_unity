using System.Collections.Generic;
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
        "Задание 8"
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
    [SerializeField] private int task2TargetPaperPointIndex = 3;

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
    private bool isPracticeModeActive;
    private bool hasPracticeLayoutSnapshot;
    private RectTransform buttonContainerRect;
    private RectTransform controlPanelRect;
    private RectTransformState buttonContainerState;
    private VerticalLayoutGroup buttonContainerLayout;
    private VerticalLayoutGroupState buttonContainerLayoutState;
    private ContentSizeFitter buttonContainerSizeFitter;
    private ContentSizeFitterState buttonContainerSizeFitterState;
    private LayoutElement practiceLayoutElement;
    private LayoutElementState practiceLayoutState;
    private float normalDropdownButtonHeight;
    private readonly Dictionary<GameObject, bool> practiceModeActiveStates = new();
#if UNITY_EDITOR
    private bool validateLayoutQueued;
#endif

    private struct RectTransformState
    {
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector2 SizeDelta;
        public Vector2 AnchoredPosition;
        public Vector2 OffsetMin;
        public Vector2 OffsetMax;
    }

    private struct VerticalLayoutGroupState
    {
        public RectOffset Padding;
        public float Spacing;
        public TextAnchor ChildAlignment;
        public bool ChildControlWidth;
        public bool ChildControlHeight;
        public bool ChildForceExpandWidth;
        public bool ChildForceExpandHeight;
    }

    private struct ContentSizeFitterState
    {
        public bool Enabled;
        public ContentSizeFitter.FitMode HorizontalFit;
        public ContentSizeFitter.FitMode VerticalFit;
    }

    private struct LayoutElementState
    {
        public bool IgnoreLayout;
        public float MinWidth;
        public float MinHeight;
        public float PreferredWidth;
        public float PreferredHeight;
        public float FlexibleWidth;
        public float FlexibleHeight;
        public int LayoutPriority;
    }

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
        if (isPracticeModeActive)
        {
            if (CanRestorePracticeModeFromOnDisable())
            {
                RestorePracticeLayout();
                RestorePracticeModeVisibility();
                isPracticeModeActive = false;
            }
            else
            {
                ClearPracticeModeState();
            }
        }

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

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            QueueValidateLayoutRefresh();
            return;
        }
#endif

        EnsureDropdown();
        UpdateButtonStates();
        RefreshLayout();
    }

#if UNITY_EDITOR
    private void QueueValidateLayoutRefresh()
    {
        if (validateLayoutQueued)
            return;

        validateLayoutQueued = true;
        UnityEditor.EditorApplication.delayCall += ApplyQueuedValidateLayoutRefresh;
    }

    private void ApplyQueuedValidateLayoutRefresh()
    {
        validateLayoutQueued = false;

        if (this == null)
            return;

        practiceRect = GetComponent<RectTransform>();
        fontAsset = ResolveFontAsset();
        EnsureDropdown();
        UpdateButtonStates();
        RefreshLayout();
    }
#endif

    public void ToggleWindow()
    {
        if (isPracticeModeActive)
        {
            ExitPracticeMode();
            return;
        }

        EnterPracticeMode();
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

    private void EnterPracticeMode()
    {
        EnsureDropdown();

        if (dropdownRect == null)
            return;

        ResetPracticeToInitialState();
        isPracticeModeActive = true;

        ApplyPracticeLayout();
        ApplyPracticeModeVisibility();
        ShowWindow();
    }

    private void ExitPracticeMode()
    {
        isPracticeModeActive = false;

        ResetPracticeToInitialState();
        RestorePracticeModeVisibility();
        RestorePracticeLayout();
        HideWindow();
    }

    private void ApplyPracticeModeVisibility()
    {
        ResolvePracticeModeLayoutReferences();
        practiceModeActiveStates.Clear();

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform canvasTransform = canvas != null ? canvas.transform : null;
        Transform bottomTransform = canvasTransform != null ? canvasTransform.Find("bottom") : null;

        if (canvasTransform != null)
        {
            for (int i = 0; i < canvasTransform.childCount; i++)
            {
                Transform child = canvasTransform.GetChild(i);
                bool shouldRemainVisible = child == bottomTransform || child == controlPanelRect;
                SetCachedActive(child.gameObject, shouldRemainVisible);
            }
        }

        if (controlPanelRect != null)
        {
            for (int i = 0; i < controlPanelRect.childCount; i++)
            {
                Transform child = controlPanelRect.GetChild(i);
                SetCachedActive(child.gameObject, child == buttonContainerRect);
            }
        }

        if (buttonContainerRect != null)
        {
            for (int i = 0; i < buttonContainerRect.childCount; i++)
            {
                Transform child = buttonContainerRect.GetChild(i);
                SetCachedActive(child.gameObject, child == practiceRect);
            }
        }

        SetCachedActive(gameObject, true);

        if (dropdownRect != null)
            SetCachedActive(dropdownRect.gameObject, true);
    }

    private void RestorePracticeModeVisibility()
    {
        foreach (KeyValuePair<GameObject, bool> pair in practiceModeActiveStates)
        {
            if (pair.Key != null && pair.Key.activeSelf != pair.Value)
                pair.Key.SetActive(pair.Value);
        }

        practiceModeActiveStates.Clear();
    }

    private void SetCachedActive(GameObject target, bool active)
    {
        if (target == null)
            return;

        if (!practiceModeActiveStates.ContainsKey(target))
            practiceModeActiveStates.Add(target, target.activeSelf);

        if (target.activeSelf != active)
            target.SetActive(active);
    }

    private void ApplyPracticeLayout()
    {
        ResolvePracticeModeLayoutReferences();
        CapturePracticeLayoutState();

        if (buttonContainerSizeFitter != null)
        {
            buttonContainerSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            buttonContainerSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            buttonContainerSizeFitter.enabled = false;
        }

        if (buttonContainerRect != null)
        {
            buttonContainerRect.anchorMin = Vector2.zero;
            buttonContainerRect.anchorMax = Vector2.one;
            buttonContainerRect.pivot = new Vector2(0.5f, 0.5f);
            buttonContainerRect.offsetMin = Vector2.zero;
            buttonContainerRect.offsetMax = Vector2.zero;
        }

        if (buttonContainerLayout != null)
        {
            RectOffset sourcePadding = buttonContainerLayoutState.Padding;
            buttonContainerLayout.padding = new RectOffset(
                sourcePadding != null ? sourcePadding.left : 0,
                sourcePadding != null ? sourcePadding.right : 0,
                0,
                0);
            buttonContainerLayout.spacing = 0f;
            buttonContainerLayout.childAlignment = TextAnchor.UpperCenter;
            buttonContainerLayout.childControlWidth = true;
            buttonContainerLayout.childControlHeight = false;
            buttonContainerLayout.childForceExpandWidth = true;
            buttonContainerLayout.childForceExpandHeight = false;
        }

        Canvas.ForceUpdateCanvases();

        dropdownButtonHeight = CalculatePracticeModeButtonHeight();

        if (practiceLayoutElement != null)
        {
            practiceLayoutElement.minHeight = dropdownButtonHeight;
            practiceLayoutElement.preferredHeight = dropdownButtonHeight;
            practiceLayoutElement.flexibleHeight = 0f;
        }

        EnsureDropdown();
        SetDropdownButtonsPreferredHeight(dropdownButtonHeight);
        RefreshLayout();
    }

    private void RestorePracticeLayout()
    {
        if (!hasPracticeLayoutSnapshot)
            return;

        dropdownButtonHeight = normalDropdownButtonHeight;

        ResolvePracticeModeLayoutReferences();

        if (buttonContainerRect != null)
            ApplyRectTransformState(buttonContainerRect, buttonContainerState);

        if (buttonContainerLayout != null)
            ApplyVerticalLayoutGroupState(buttonContainerLayout, buttonContainerLayoutState);

        if (buttonContainerSizeFitter != null)
            ApplyContentSizeFitterState(buttonContainerSizeFitter, buttonContainerSizeFitterState);

        if (practiceLayoutElement != null)
            ApplyLayoutElementState(practiceLayoutElement, practiceLayoutState);

        hasPracticeLayoutSnapshot = false;
        EnsureDropdown();
        RefreshLayout();
    }

    private void ResolvePracticeModeLayoutReferences()
    {
        if (practiceRect == null)
            practiceRect = GetComponent<RectTransform>();

        buttonContainerRect = practiceRect != null ? practiceRect.parent as RectTransform : null;
        controlPanelRect = buttonContainerRect != null ? buttonContainerRect.parent as RectTransform : null;
        buttonContainerLayout = buttonContainerRect != null ? buttonContainerRect.GetComponent<VerticalLayoutGroup>() : null;
        buttonContainerSizeFitter = buttonContainerRect != null ? buttonContainerRect.GetComponent<ContentSizeFitter>() : null;
        practiceLayoutElement = GetComponent<LayoutElement>();
    }

    private void CapturePracticeLayoutState()
    {
        if (hasPracticeLayoutSnapshot)
            return;

        normalDropdownButtonHeight = dropdownButtonHeight;

        if (buttonContainerRect != null)
            buttonContainerState = CaptureRectTransformState(buttonContainerRect);

        if (buttonContainerLayout != null)
            buttonContainerLayoutState = CaptureVerticalLayoutGroupState(buttonContainerLayout);

        if (buttonContainerSizeFitter != null)
            buttonContainerSizeFitterState = CaptureContentSizeFitterState(buttonContainerSizeFitter);

        if (practiceLayoutElement != null)
            practiceLayoutState = CaptureLayoutElementState(practiceLayoutElement);

        hasPracticeLayoutSnapshot = true;
    }

    private bool CanRestorePracticeModeFromOnDisable()
    {
        if (Application.isPlaying)
            return false;

#if UNITY_EDITOR
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return false;
#endif

        return true;
    }

    private void ClearPracticeModeState()
    {
        isPracticeModeActive = false;
        hasPracticeLayoutSnapshot = false;
        practiceModeActiveStates.Clear();
    }

    private float CalculatePracticeModeButtonHeight()
    {
        float availableHeight = 0f;

        if (buttonContainerRect != null)
            availableHeight = buttonContainerRect.rect.height;

        if (availableHeight <= 0f && controlPanelRect != null)
            availableHeight = controlPanelRect.rect.height;

        if (availableHeight <= 0f)
            return normalDropdownButtonHeight > 0f ? normalDropdownButtonHeight : dropdownButtonHeight;

        int buttonCount = TaskLabels.Length + 1;
        float reservedHeight =
            dropdownTopOffset +
            dropdownPadding * 2f +
            Mathf.Max(0, TaskLabels.Length - 1) * dropdownSpacing;

        float targetHeight = (availableHeight - reservedHeight) / buttonCount;
        return Mathf.Max(28f, targetHeight);
    }

    private void SetDropdownButtonsPreferredHeight(float preferredHeight)
    {
        if (dropdownRect == null)
            return;

        dropdownRect.sizeDelta = new Vector2(dropdownRect.sizeDelta.x, CalculateDropdownHeight());

        for (int i = 0; i < TaskLabels.Length; i++)
        {
            Transform taskTransform = dropdownRect.Find($"task_{i + 1}");
            if (taskTransform == null)
                continue;

            LayoutElement layout = taskTransform.GetComponent<LayoutElement>();
            if (layout == null)
                continue;

            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
            layout.flexibleHeight = 0f;
        }
    }

    private static RectTransformState CaptureRectTransformState(RectTransform rectTransform)
    {
        return new RectTransformState
        {
            AnchorMin = rectTransform.anchorMin,
            AnchorMax = rectTransform.anchorMax,
            Pivot = rectTransform.pivot,
            SizeDelta = rectTransform.sizeDelta,
            AnchoredPosition = rectTransform.anchoredPosition,
            OffsetMin = rectTransform.offsetMin,
            OffsetMax = rectTransform.offsetMax
        };
    }

    private static void ApplyRectTransformState(RectTransform rectTransform, RectTransformState state)
    {
        rectTransform.anchorMin = state.AnchorMin;
        rectTransform.anchorMax = state.AnchorMax;
        rectTransform.pivot = state.Pivot;
        rectTransform.sizeDelta = state.SizeDelta;
        rectTransform.anchoredPosition = state.AnchoredPosition;
        rectTransform.offsetMin = state.OffsetMin;
        rectTransform.offsetMax = state.OffsetMax;
    }

    private static VerticalLayoutGroupState CaptureVerticalLayoutGroupState(VerticalLayoutGroup layoutGroup)
    {
        return new VerticalLayoutGroupState
        {
            Padding = CloneRectOffset(layoutGroup.padding),
            Spacing = layoutGroup.spacing,
            ChildAlignment = layoutGroup.childAlignment,
            ChildControlWidth = layoutGroup.childControlWidth,
            ChildControlHeight = layoutGroup.childControlHeight,
            ChildForceExpandWidth = layoutGroup.childForceExpandWidth,
            ChildForceExpandHeight = layoutGroup.childForceExpandHeight
        };
    }

    private static void ApplyVerticalLayoutGroupState(
        VerticalLayoutGroup layoutGroup,
        VerticalLayoutGroupState state)
    {
        layoutGroup.padding = CloneRectOffset(state.Padding);
        layoutGroup.spacing = state.Spacing;
        layoutGroup.childAlignment = state.ChildAlignment;
        layoutGroup.childControlWidth = state.ChildControlWidth;
        layoutGroup.childControlHeight = state.ChildControlHeight;
        layoutGroup.childForceExpandWidth = state.ChildForceExpandWidth;
        layoutGroup.childForceExpandHeight = state.ChildForceExpandHeight;
    }

    private static ContentSizeFitterState CaptureContentSizeFitterState(ContentSizeFitter sizeFitter)
    {
        return new ContentSizeFitterState
        {
            Enabled = sizeFitter.enabled,
            HorizontalFit = sizeFitter.horizontalFit,
            VerticalFit = sizeFitter.verticalFit
        };
    }

    private static void ApplyContentSizeFitterState(
        ContentSizeFitter sizeFitter,
        ContentSizeFitterState state)
    {
        sizeFitter.horizontalFit = state.HorizontalFit;
        sizeFitter.verticalFit = state.VerticalFit;
        sizeFitter.enabled = state.Enabled;
    }

    private static LayoutElementState CaptureLayoutElementState(LayoutElement layoutElement)
    {
        return new LayoutElementState
        {
            IgnoreLayout = layoutElement.ignoreLayout,
            MinWidth = layoutElement.minWidth,
            MinHeight = layoutElement.minHeight,
            PreferredWidth = layoutElement.preferredWidth,
            PreferredHeight = layoutElement.preferredHeight,
            FlexibleWidth = layoutElement.flexibleWidth,
            FlexibleHeight = layoutElement.flexibleHeight,
            LayoutPriority = layoutElement.layoutPriority
        };
    }

    private static void ApplyLayoutElementState(LayoutElement layoutElement, LayoutElementState state)
    {
        layoutElement.ignoreLayout = state.IgnoreLayout;
        layoutElement.minWidth = state.MinWidth;
        layoutElement.minHeight = state.MinHeight;
        layoutElement.preferredWidth = state.PreferredWidth;
        layoutElement.preferredHeight = state.PreferredHeight;
        layoutElement.flexibleWidth = state.FlexibleWidth;
        layoutElement.flexibleHeight = state.FlexibleHeight;
        layoutElement.layoutPriority = state.LayoutPriority;
    }

    private static RectOffset CloneRectOffset(RectOffset source)
    {
        return source == null
            ? new RectOffset()
            : new RectOffset(source.left, source.right, source.top, source.bottom);
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
        if (!Application.isPlaying || activeTaskIndex != 4)
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
        if (!Application.isPlaying || activeTaskIndex != 5)
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
            3 => paperMover != null && paperMover.CurrentPaperPointIndex >= paperMover.CutWaitPaperPointIndex,
            4 => requiredCommandSequence != null &&
                 requiredCommandSequence.Length > 0 &&
                 commandSequenceProgress >= requiredCommandSequence.Length,
            5 => (cutObservedDuringActiveTask && !CUT_Animator.IsCutting) ||
                 (cutter != null && cutter.CutCompleted) ||
                 (paperMover != null && (paperMover.IsSecStage || paperMover.IsMainStage || paperMover.IsFinished)),
            6 => paperMover != null && (paperMover.IsMainStage || paperMover.IsFinished),
            7 => paperMover != null && paperMover.IsFinished,
            8 => !btn_Animator.IsMachinePowered,
            _ => false
        };

        if (!completed)
            return;

        CompleteActiveTask(activeTaskIndex);
    }

    private bool IsPracticeFlowActive()
    {
        return isPracticeModeActive ||
               activeTaskIndex > 0 ||
               (highestUnlockedTask > 1 && highestUnlockedTask <= TaskLabels.Length);
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
            4 => button != ControlPanelButton.PowerSwitch && button != ControlPanelButton.DualStart,
            5 => button == ControlPanelButton.DualStart,
            8 => button == ControlPanelButton.PowerSwitch,
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
            3 => paperMover.CurrentPaperPointIndex < paperMover.CutWaitPaperPointIndex,
            6 => !paperMover.IsMainStage && !paperMover.IsFinished,
            7 => !paperMover.IsFinished,
            _ => false
        };
    }

    private bool IsCutStartAllowedInternal()
    {
        if (!IsPracticeFlowActive())
            return true;

        if (activeTaskIndex <= 0)
            return false;

        return activeTaskIndex == 5;
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
        {
            bool wasPracticeModeActive = isPracticeModeActive;
            isPracticeModeActive = false;
            btn_Animator.TrySimulateButtonPress(ControlPanelButton.PowerSwitch);
            isPracticeModeActive = wasPracticeModeActive;
        }

        ResolveInfoPanel();
        infoPanel?.ShowDefault();
        UpdateButtonStates();
    }

    private string BuildInstructionText(int taskIndex)
    {
        return taskIndex switch
        {
            1 => "Задание 1. Запустить станок.\nКак выполнить: удерживайте Z и нажмите O, чтобы включить питание станка.",
            2 => "Задание 2. Достать бумагу и положить её на стол.\nКак выполнить: удерживайте N и нажмите M один раз, чтобы переместить бумагу по траектории до стола.",
            3 => "Задание 3. Разместить бумагу в зоне реза.\nКак выполнить: удерживайте N и нажмите M ещё раз, чтобы переместить бумагу со стола в зону реза.",
            4 => "Задание 4. Ввести команду на панели управления.\nКак выполнить: удерживайте Z и последовательно нажмите 1, 0, 0, затем Enter.",
            5 => "Задание 5. Запустить рез бумаги.\nКак выполнить: удерживайте Z и нажмите V. Эта комбинация запускает двойной пуск и сам рез одним действием.",
            6 => "Задание 6. Убрать отрезанный отход.\nКак выполнить: удерживайте N и нажимайте M, пока отрезанный отход не уйдёт в левый мусорный бокс.",
            7 => "Задание 7. Убрать готовую часть бумаги.\nКак выполнить: удерживайте N и нажимайте M, пока готовая часть бумаги не уйдёт в правый ящик.",
            8 => "Задание 8. Выключить станок.\nКак выполнить: удерживайте Z и нажмите O ещё раз, чтобы выключить питание.",
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
