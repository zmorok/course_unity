using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;

public class PaperPathMover : MonoBehaviour
{
    private enum MoveStage
    {
        WholePaper,
        SecPart,
        MainPart,
        Finished
    }

    [Header("Paper hierarchy")]
    [SerializeField] private Transform paper;
    private Transform main;
    private Transform sec;

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

    private Vector3 mainInitialLocalPos;
    private Quaternion mainInitialLocalRot;

    private Vector3 secInitialLocalPos;
    private Quaternion secInitialLocalRot;

    private void Start()
    {
        if (paper == null)
        {
            Debug.LogError("Не назначен paper");
            enabled = false;
            return;
        }

        main = paper.Find("paper_for_cut/main");
        sec = paper.Find("paper_for_cut/sec");

        if (main == null)
        {
            Debug.LogError("Не найден main");
            enabled = false;
            return;
        }

        if (sec == null)
        {
            Debug.LogError("Не найден sec");
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

        mainInitialLocalPos = main.localPosition;
        mainInitialLocalRot = main.localRotation;

        secInitialLocalPos = sec.localPosition;
        secInitialLocalRot = sec.localRotation;

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

            cutter.CanBeCutted = false;
            BeginSeparatedPartsRoute();
            TryMoveSecNext();
            return;
        }

        int nextIndex = currentPaperIndex + 1;
        StartCoroutine(MoveWholePaperToPoint(nextIndex));
    }

    private void BeginSeparatedPartsRoute()
    {
        stage = MoveStage.SecPart;
        currentSecIndex = -1;
        currentMainIndex = -1;
        RefreshCutAvailability();
        Debug.Log("Рез завершён. Дальше двигается только sec");
    }

    private void TryMoveSecNext()
    {
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

        Vector3 startPos = paper.position;
        Quaternion startRot = paper.rotation;

        Vector3 endPos = paperPoints[targetIndex].position;
        Quaternion endRot = paperPoints[targetIndex].rotation;

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
            currentPaperIndex == cutWaitPaperPointIndex;

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

        paper.SetPositionAndRotation(paperPoints[0].position, paperPoints[0].rotation);

        main.localPosition = mainInitialLocalPos;
        main.localRotation = mainInitialLocalRot;

        sec.localPosition = secInitialLocalPos;
        sec.localRotation = secInitialLocalRot;

        if (cutter != null)
        {
            cutter.ResetCutState();
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
}
