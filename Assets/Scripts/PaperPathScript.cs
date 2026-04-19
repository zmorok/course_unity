using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;

public class PaperPathMover : MonoBehaviour
{
    public const string InvalidCutSizeMessage =
        "Размеры бумаги не позволяют выполнить такую резку, попробуйте от 100 до 900мм";

    private enum MoveStage
    {
        WholePaper,
        SecPart,
        MainPart,
        Finished
    }

    [Serializable]
    private sealed class PaperCutVariant
    {
        public string Label = "A";
        public string RootObjectName = "paper_A";
        public float MinCutSize = 100f;
        public float MaxCutSize = 300f;
        public float CutForwardOffset = -0.015f;

        [NonSerialized] public Transform Root;
        [NonSerialized] public Transform Main;
        [NonSerialized] public Transform Sec;
        [NonSerialized] public Vector3 MainInitialLocalPosition;
        [NonSerialized] public Quaternion MainInitialLocalRotation;
        [NonSerialized] public Vector3 SecInitialLocalPosition;
        [NonSerialized] public Quaternion SecInitialLocalRotation;

        public bool IsResolved => Root != null && Main != null && Sec != null;

        public bool Contains(float cutSize)
        {
            return cutSize >= MinCutSize && cutSize <= MaxCutSize;
        }
    }

    [Header("Back Cut Holder")]
    [SerializeField] private Transform backHolder;
    private Vector3 backHolderInitialLocalPosition;
    private Quaternion backHolderInitialLocalRotation;

    [Header("Paper hierarchy")]
    [SerializeField] private Transform paper;
    [SerializeField]
    private PaperCutVariant[] cutVariants =
    {
        new() { Label = "A", RootObjectName = "paper_A", MinCutSize = 100f, MaxCutSize = 300f, CutForwardOffset = -0.015f },
        new() { Label = "B", RootObjectName = "paper_B", MinCutSize = 301f, MaxCutSize = 600f, CutForwardOffset = -0.18f },
        new() { Label = "C", RootObjectName = "paper_C", MinCutSize = 601f, MaxCutSize = 900f, CutForwardOffset = -0.365f }
    };

    private Transform main;
    private Transform sec;
    private PaperCutVariant activeVariant;

    [Header("Path points")]
    [SerializeField] private Transform[] paperPoints; // P_1 ... P_6
    [SerializeField] private Transform[] secPoints;   // P_11 ... P_14
    [SerializeField] private Transform[] mainPoints;  // P_7 ... P_10

    [Header("Movement")]
    [SerializeField] private float moveDuration = 0.35f;

    [Header("Cut synchronization")]
    [SerializeField] private CUT_Animator cutter;
    [SerializeField] private int cutWaitPaperPointIndex = 4; // P_5 в paperPoints

    private MoveStage stage = MoveStage.WholePaper;

    private int currentPaperIndex;
    private int currentSecIndex;
    private int currentMainIndex;

    private bool isMoving;
    private bool cutCommandApplied;
    private bool cutOffsetApplied;

    private void OnValidate()
    {
        EnsureDefaultCutVariants();
    }

    private void Start()
    {
        EnsureDefaultCutVariants();

        if (paper == null)
        {
            Debug.LogError("Не назначен paper");
            enabled = false;
            return;
        }

        if (backHolder != null)
        {
            backHolderInitialLocalPosition = backHolder.localPosition;
            backHolderInitialLocalRotation = backHolder.localRotation;
        }

        if (!ResolvePaperVariants())
        {
            enabled = false;
            return;
        }

        if (!ValidatePointsArray(paperPoints, "paperPoints", 2)) return;
        if (!ValidatePointsArray(secPoints, "secPoints", 1)) return;
        if (!ValidatePointsArray(mainPoints, "mainPoints", 1)) return;

        if (cutWaitPaperPointIndex < 0 || cutWaitPaperPointIndex >= paperPoints.Length)
        {
            Debug.LogError("cutWaitPaperPointIndex вне диапазона paperPoints");
            enabled = false;
            return;
        }

        ResetPaperToStart();
    }

    private void OnEnable()
    {
        btn_Animator.MachinePowerChanged += HandleMachinePowerChanged;
    }

    private void OnDisable()
    {
        btn_Animator.MachinePowerChanged -= HandleMachinePowerChanged;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Рестарт всего сценария: удерживаем R, нажимаем T
        if (keyboard.rKey.isPressed && keyboard.tKey.wasPressedThisFrame)
        {
            ResetPaperToStart();
            return;
        }

        if (isMoving) return;
        if (!btn_Animator.IsMachinePowered) return;
        if (!PracticeTasksPopupController.IsPaperAdvanceAllowed()) return;

        // Движение по маршруту: удерживаем N, нажимаем M
        if (keyboard.nKey.isPressed && keyboard.mKey.wasPressedThisFrame)
        {
            TryMoveNext();
        }
    }

    private void EnsureDefaultCutVariants()
    {
        if (cutVariants != null && cutVariants.Length > 0)
            return;

        cutVariants = new[]
        {
            new PaperCutVariant
            {
                Label = "A",
                RootObjectName = "paper_A",
                MinCutSize = 100f,
                MaxCutSize = 300f,
                CutForwardOffset = -0.015f
            },
            new PaperCutVariant
            {
                Label = "B",
                RootObjectName = "paper_B",
                MinCutSize = 301f,
                MaxCutSize = 600f,
                CutForwardOffset = -0.18f
            },
            new PaperCutVariant
            {
                Label = "C",
                RootObjectName = "paper_C",
                MinCutSize = 601f,
                MaxCutSize = 900f,
                CutForwardOffset = -0.365f
            }
        };
    }

    private bool ResolvePaperVariants()
    {
        if (cutVariants == null || cutVariants.Length == 0)
        {
            Debug.LogError("Не настроены варианты бумаги");
            return false;
        }

        bool hasResolvedVariant = false;

        for (int i = 0; i < cutVariants.Length; i++)
        {
            PaperCutVariant variant = cutVariants[i];
            if (variant == null)
                continue;

            variant.Root = paper.Find(variant.RootObjectName);
            if (variant.Root == null)
            {
                Debug.LogError($"Не найден вариант бумаги '{variant.RootObjectName}'");
                continue;
            }

            variant.Main = variant.Root.Find("main");
            variant.Sec = variant.Root.Find("sec");

            if (variant.Main == null || variant.Sec == null)
            {
                Debug.LogError($"В '{variant.RootObjectName}' должны быть дочерние объекты main и sec");
                continue;
            }

            variant.MainInitialLocalPosition = variant.Main.localPosition;
            variant.MainInitialLocalRotation = variant.Main.localRotation;
            variant.SecInitialLocalPosition = variant.Sec.localPosition;
            variant.SecInitialLocalRotation = variant.Sec.localRotation;
            hasResolvedVariant = true;
        }

        if (!hasResolvedVariant)
            Debug.LogError("Нет ни одного корректного варианта бумаги");

        return hasResolvedVariant;
    }

    private void RestoreVariantPartTransforms()
    {
        if (cutVariants == null)
            return;

        for (int i = 0; i < cutVariants.Length; i++)
        {
            PaperCutVariant variant = cutVariants[i];
            if (variant == null || !variant.IsResolved)
                continue;

            variant.Main.localPosition = variant.MainInitialLocalPosition;
            variant.Main.localRotation = variant.MainInitialLocalRotation;
            variant.Sec.localPosition = variant.SecInitialLocalPosition;
            variant.Sec.localRotation = variant.SecInitialLocalRotation;
        }
    }

    private void SelectDefaultVariant()
    {
        SelectVariant(FindFirstResolvedVariant());
    }

    private PaperCutVariant FindFirstResolvedVariant()
    {
        if (cutVariants == null)
            return null;

        for (int i = 0; i < cutVariants.Length; i++)
        {
            PaperCutVariant variant = cutVariants[i];
            if (variant != null && variant.IsResolved)
                return variant;
        }

        return null;
    }

    private PaperCutVariant FindVariantForCutSize(float cutSize)
    {
        if (cutVariants == null)
            return null;

        for (int i = 0; i < cutVariants.Length; i++)
        {
            PaperCutVariant variant = cutVariants[i];
            if (variant != null && variant.IsResolved && variant.Contains(cutSize))
                return variant;
        }

        return null;
    }

    private void SelectVariant(PaperCutVariant selectedVariant)
    {
        activeVariant = selectedVariant;
        main = selectedVariant != null ? selectedVariant.Main : null;
        sec = selectedVariant != null ? selectedVariant.Sec : null;

        if (cutVariants == null)
            return;

        for (int i = 0; i < cutVariants.Length; i++)
        {
            PaperCutVariant variant = cutVariants[i];
            if (variant == null || variant.Root == null)
                continue;

            variant.Root.gameObject.SetActive(variant == selectedVariant);
        }
    }

    private void TryMoveNext()
    {
        switch (stage)
        {
            case MoveStage.WholePaper:
                TryMoveWholePaperNext();
                break;

            case MoveStage.SecPart:
                TryMoveSecNext();
                break;

            case MoveStage.MainPart:
                TryMoveMainNext();
                break;

            case MoveStage.Finished:
                Debug.Log("Маршрут завершён");
                break;
        }
    }

    public bool TryMoveNextStep()
    {
        if (isMoving)
            return false;

        if (!btn_Animator.IsMachinePowered)
            return false;

        if (!PracticeTasksPopupController.IsPaperAdvanceAllowed())
            return false;

        MoveStage previousStage = stage;
        int previousPaperIndex = currentPaperIndex;
        int previousSecIndex = currentSecIndex;
        int previousMainIndex = currentMainIndex;

        TryMoveNext();

        return isMoving ||
               stage != previousStage ||
               currentPaperIndex != previousPaperIndex ||
               currentSecIndex != previousSecIndex ||
               currentMainIndex != previousMainIndex;
    }

    public bool CanAcceptCutCommand =>
        btn_Animator.IsMachinePowered &&
        stage == MoveStage.WholePaper &&
        currentPaperIndex == cutWaitPaperPointIndex &&
        !isMoving &&
        !IsFinished;

    public bool TryApplyCutCommand(float cutSize, out string statusLabel, out string errorMessage)
    {
        statusLabel = string.Empty;
        errorMessage = string.Empty;

        if (!CanAcceptCutCommand)
            return false;

        PaperCutVariant selectedVariant = FindVariantForCutSize(cutSize);
        if (selectedVariant == null)
        {
            CancelCutPreparation(moveBackToCutPoint: true);
            errorMessage = InvalidCutSizeMessage;
            return false;
        }

        SelectVariant(selectedVariant);
        cutCommandApplied = true;
        cutOffsetApplied = false;
        RefreshCutAvailability();

        StartCoroutine(MovePaperToCutOffset(selectedVariant));
        statusLabel = $"PAPER {selectedVariant.Label}";
        return true;
    }

    public bool ClearCutOffsetFromPanel(out string statusLabel)
    {
        statusLabel = string.Empty;

        if (!CanAcceptCutCommand)
            return false;

        if (!cutCommandApplied && !cutOffsetApplied)
            return false;

        CancelCutPreparation(moveBackToCutPoint: true);
        statusLabel = "OFFSET CLEAR";
        return true;
    }

    private void TryMoveWholePaperNext()
    {
        if (currentPaperIndex >= paperPoints.Length - 1)
        {
            stage = MoveStage.SecPart;
            Debug.Log("Маршрут whole paper завершён, дальше двигается sec");
            return;
        }

        // Если стоим на P_5 и хотим идти дальше, сначала нужен рез
        if (currentPaperIndex == cutWaitPaperPointIndex)
        {
            if (cutter == null)
            {
                Debug.LogWarning("Не назначен cutter, движение после P_5 заблокировано");
                return;
            }

            if (!cutter.TryConsumeCutCompleted())
            {
                Debug.Log("На P_5 ждём завершения реза");
                return;
            }

            StartCoroutine(FinishCutAndBeginSeparatedRoute());
            return;
        }

        if (currentPaperIndex == 0 && paperPoints.Length > 3)
        {
            StartCoroutine(MoveWholePaperThroughInitialPickupTrajectory());
            return;
        }

        int nextIndex = currentPaperIndex + 1;
        StartCoroutine(MoveWholePaperToPoint(nextIndex));
    }

    private IEnumerator FinishCutAndBeginSeparatedRoute()
    {
        isMoving = true;
        cutCommandApplied = false;
        cutOffsetApplied = false;

        if (cutter != null)
            cutter.CanBeCutted = false;

        RefreshCutAvailability();

        if (backHolder != null)
        {
            yield return MoveBackHolderToInitialPose();
        }

        stage = MoveStage.SecPart;
        currentSecIndex = -1;
        currentMainIndex = -1;

        isMoving = false;
        RefreshCutAvailability();

        Debug.Log("Рез завершён. Задний упор возвращён. Дальше двигается только sec");

        TryMoveSecNext();
    }

    private void CancelCutPreparation(bool moveBackToCutPoint)
    {
        bool hadCutOffset = cutOffsetApplied;
        cutCommandApplied = false;
        cutOffsetApplied = false;

        if (cutter != null)
            cutter.CanBeCutted = false;

        bool shouldMoveBack = moveBackToCutPoint && hadCutOffset;
        if (!shouldMoveBack || !IsAtCutWaitPoint() || isMoving)
        {
            RefreshCutAvailability();
            return;
        }

        StartCoroutine(MovePaperToCutWaitPoint());
    }

    private bool IsAtCutWaitPoint()
    {
        return stage == MoveStage.WholePaper && currentPaperIndex == cutWaitPaperPointIndex;
    }

    private IEnumerator MovePaperToCutOffset(PaperCutVariant selectedVariant)
    {
        isMoving = true;
        RefreshCutAvailability();

        Transform cutPoint = paperPoints[cutWaitPaperPointIndex];
        Vector3 paperEndPos = CalculateCutOffsetPosition(selectedVariant);
        Quaternion paperEndRot = cutPoint.rotation;

        Vector3? holderEndPos = null;
        Quaternion? holderEndRot = null;

        if (backHolder != null)
        {
            holderEndPos = backHolderInitialLocalPosition - new Vector3(0f, 0f, selectedVariant.CutForwardOffset);
            holderEndRot = backHolderInitialLocalRotation;
        }

        yield return MovePaperAndBackHolder(
            paperEndPos,
            paperEndRot,
            holderEndPos,
            holderEndRot
        );

        cutOffsetApplied = true;
        isMoving = false;
        RefreshCutAvailability();

        Debug.Log($"Выбран тип бумаги {selectedVariant.Label}, бумага смещена к линии реза");
    }

    private IEnumerator MovePaperToCutWaitPoint()
    {
        isMoving = true;
        RefreshCutAvailability();

        Transform cutPoint = paperPoints[cutWaitPaperPointIndex];

        Vector3? holderEndPos = null;
        Quaternion? holderEndRot = null;

        if (backHolder != null)
        {
            holderEndPos = backHolderInitialLocalPosition;
            holderEndRot = backHolderInitialLocalRotation;
        }

        yield return MovePaperAndBackHolder(
            cutPoint.position,
            cutPoint.rotation,
            holderEndPos,
            holderEndRot
        );

        isMoving = false;
        RefreshCutAvailability();
        Debug.Log("Смещение бумаги сброшено");
    }

    private IEnumerator MoveBackHolderToInitialPose()
    {
        Vector3 startPos = backHolder.localPosition;
        Quaternion startRot = backHolder.localRotation;

        float time = 0f;

        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / moveDuration);

            backHolder.localPosition = Vector3.Lerp(startPos, backHolderInitialLocalPosition, t);
            backHolder.localRotation = Quaternion.Slerp(startRot, backHolderInitialLocalRotation, t);

            yield return null;
        }

        backHolder.localPosition = backHolderInitialLocalPosition;
        backHolder.localRotation = backHolderInitialLocalRotation;
    }

    private IEnumerator MovePaperAndBackHolder(
        Vector3 paperEndPos,
        Quaternion paperEndRot,
        Vector3? holderEndLocalPos = null,
        Quaternion? holderEndLocalRot = null)
    {
        Vector3 paperStartPos = paper.position;
        Quaternion paperStartRot = paper.rotation;

        bool moveHolder =
            backHolder != null &&
            holderEndLocalPos.HasValue &&
            holderEndLocalRot.HasValue;

        Vector3 holderStartPos = Vector3.zero;
        Quaternion holderStartRot = Quaternion.identity;

        if (moveHolder)
        {
            holderStartPos = backHolder.localPosition;
            holderStartRot = backHolder.localRotation;
        }

        float time = 0f;

        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / moveDuration);

            paper.position = Vector3.Lerp(paperStartPos, paperEndPos, t);
            paper.rotation = Quaternion.Slerp(paperStartRot, paperEndRot, t);

            if (moveHolder)
            {
                backHolder.localPosition = Vector3.Lerp(holderStartPos, holderEndLocalPos.Value, t);
                backHolder.localRotation = Quaternion.Slerp(holderStartRot, holderEndLocalRot.Value, t);
            }

            yield return null;
        }

        paper.SetPositionAndRotation(paperEndPos, paperEndRot);

        if (moveHolder)
        {
            backHolder.localPosition = holderEndLocalPos.Value;
            backHolder.localRotation = holderEndLocalRot.Value;
        }
    }

    private Vector3 CalculateCutOffsetPosition(PaperCutVariant selectedVariant)
    {
        Transform cutPoint = paperPoints[cutWaitPaperPointIndex];
        Vector3 direction = GetCutFeedDirection();
        float offset = selectedVariant != null ? selectedVariant.CutForwardOffset : 0f;
        return cutPoint.position + direction * offset;
    }

    private Vector3 GetCutFeedDirection()
    {
        if (cutWaitPaperPointIndex > 0 && cutWaitPaperPointIndex < paperPoints.Length)
        {
            Vector3 direction = paperPoints[cutWaitPaperPointIndex].position -
                                paperPoints[cutWaitPaperPointIndex - 1].position;

            if (direction.sqrMagnitude > 0.0001f)
                return direction.normalized;
        }

        return paperPoints[cutWaitPaperPointIndex].forward;
    }

    private void TryMoveSecNext()
    {
        if (sec == null)
            return;

        int nextIndex = currentSecIndex + 1;

        if (nextIndex >= secPoints.Length)
        {
            stage = MoveStage.MainPart;
            Debug.Log("Маршрут sec завершён, дальше двигается main");
            return;
        }

        StartCoroutine(MoveTransformToPoint(sec, secPoints[nextIndex], () =>
        {
            currentSecIndex = nextIndex;

            if (currentSecIndex >= secPoints.Length - 1)
            {
                stage = MoveStage.MainPart;
                Debug.Log("sec дошёл до конца. Теперь двигаем main.");
            }
        }));
    }

    private void TryMoveMainNext()
    {
        if (main == null)
            return;

        int nextIndex = currentMainIndex + 1;

        if (nextIndex >= mainPoints.Length)
        {
            stage = MoveStage.Finished;
            Debug.Log("Маршрут main завершён. Всё закончено.");
            return;
        }

        StartCoroutine(MoveTransformToPoint(main, mainPoints[nextIndex], () =>
        {
            currentMainIndex = nextIndex;

            if (currentMainIndex >= mainPoints.Length - 1)
            {
                stage = MoveStage.Finished;
                Debug.Log("main дошёл до конца. Всё закончено.");
            }
        }));
    }

    private IEnumerator MoveWholePaperToPoint(int targetIndex)
    {
        isMoving = true;
        cutCommandApplied = false;
        cutOffsetApplied = false;
        RefreshCutAvailability();

        Vector3 endPos = paperPoints[targetIndex].position;
        Quaternion endRot = paperPoints[targetIndex].rotation;

        yield return MovePaperToPose(endPos, endRot);

        currentPaperIndex = targetIndex;
        isMoving = false;

        Debug.Log($"Весь лист перемещён в paper P_{currentPaperIndex + 1}");

        if (currentPaperIndex >= paperPoints.Length - 1)
        {
            stage = MoveStage.SecPart;
            Debug.Log("Дальше будет двигаться только sec");
        }

        RefreshCutAvailability();
    }

    private IEnumerator MoveWholePaperThroughInitialPickupTrajectory()
    {
        isMoving = true;
        cutCommandApplied = false;
        cutOffsetApplied = false;
        RefreshCutAvailability();

        yield return MovePaperToPose(paperPoints[1].position, paperPoints[1].rotation);
        yield return MovePaperToPose(paperPoints[2].position, paperPoints[2].rotation);
        yield return MovePaperToPose(paperPoints[3].position, paperPoints[3].rotation);

        currentPaperIndex = 3;
        isMoving = false;

        Debug.Log("Весь лист перемещён по траектории P_1 -> P_2 -> P_3 -> P_4");
        RefreshCutAvailability();
    }

    private IEnumerator MovePaperToPose(Vector3 endPos, Quaternion endRot)
    {
        Vector3 startPos = paper.position;
        Quaternion startRot = paper.rotation;

        float time = 0f;

        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / moveDuration);

            paper.position = Vector3.Lerp(startPos, endPos, t);
            paper.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        paper.SetPositionAndRotation(endPos, endRot);
    }

    private IEnumerator MoveTransformToPoint(Transform target, Transform point, Action onComplete)
    {
        isMoving = true;

        Vector3 startPos = target.position;
        Quaternion startRot = target.rotation;

        Vector3 endPos = point.position;
        Quaternion endRot = point.rotation;

        float time = 0f;

        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / moveDuration);

            target.position = Vector3.Lerp(startPos, endPos, t);
            target.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        target.SetPositionAndRotation(endPos, endRot);

        onComplete?.Invoke();
        isMoving = false;
        RefreshCutAvailability();
    }

    private bool ValidatePointsArray(Transform[] arr, string arrayName, int minCount)
    {
        if (arr == null || arr.Length < minCount)
        {
            Debug.LogError($"{arrayName} должен содержать минимум {minCount} точек");
            enabled = false;
            return false;
        }

        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == null)
            {
                Debug.LogError($"Точка {arrayName}[{i}] не назначена");
                enabled = false;
                return false;
            }
        }

        return true;
    }

    private void HandleMachinePowerChanged(bool isPowered)
    {
        if (!isPowered)
        {
            StopAllCoroutines();
            isMoving = false;
            cutCommandApplied = false;
            cutOffsetApplied = false;

            if (backHolder != null)
            {
                backHolder.localPosition = backHolderInitialLocalPosition;
                backHolder.localRotation = backHolderInitialLocalRotation;
            }
        }

        RefreshCutAvailability();
    }

    private void RefreshCutAvailability()
    {
        if (cutter == null)
            return;

        bool canCutNow =
            btn_Animator.IsMachinePowered &&
            !isMoving &&
            stage == MoveStage.WholePaper &&
            currentPaperIndex == cutWaitPaperPointIndex &&
            cutCommandApplied &&
            cutOffsetApplied &&
            activeVariant != null;

        cutter.CanBeCutted = canCutNow;
    }

    public void ResetPaperToStart()
    {
        StopAllCoroutines();

        isMoving = false;

        currentPaperIndex = 0;
        currentSecIndex = -1;
        currentMainIndex = -1;

        stage = MoveStage.WholePaper;
        cutCommandApplied = false;
        cutOffsetApplied = false;

        paper.SetPositionAndRotation(paperPoints[0].position, paperPoints[0].rotation);

        RestoreVariantPartTransforms();
        SelectDefaultVariant();

        if (cutter != null)
        {
            cutter.ResetCutState();
        }

        if (backHolder != null)
        {
            backHolder.localPosition = backHolderInitialLocalPosition;
            backHolder.localRotation = backHolderInitialLocalRotation;
        }

        RefreshCutAvailability();

        Debug.Log("Сценарий сброшен в начальное состояние");
    }

    public string GetCurrentStage()
    {
        return stage.ToString();
    }

    public bool IsMoving => isMoving;
    public bool IsWholePaperStage => stage == MoveStage.WholePaper;
    public bool IsSecStage => stage == MoveStage.SecPart;
    public bool IsMainStage => stage == MoveStage.MainPart;
    public bool IsFinished => stage == MoveStage.Finished;
    public int CurrentPaperPointIndex => currentPaperIndex;
    public int CurrentSecPointIndex => currentSecIndex;
    public int CurrentMainPointIndex => currentMainIndex;
    public int CutWaitPaperPointIndex => cutWaitPaperPointIndex;
    public bool IsCutOffsetApplied => cutOffsetApplied;
    public string ActivePaperVariantLabel => activeVariant != null ? activeVariant.Label : string.Empty;
}
