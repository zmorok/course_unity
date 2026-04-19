using UnityEngine;
using UnityEngine.InputSystem;

public class CameraViewController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private Transform startCameraAnchor;

    [Header("Animated parts")]
    [SerializeField] private Animator bladeAnimator;
    [SerializeField] private Animator holderAnimator;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool isMoving = false;
    private CameraDualModeController dualModeController;

    [Header("Screen")]
    [SerializeField] private float xScreen = 0.15f;
    [SerializeField] private float yScreen = 2.1f;
    [SerializeField] private float zScreen = 0.2f;

    [Header("Keyboard")]
    [SerializeField] private float xKeyboard = -0.9f;
    [SerializeField] private float yKeyboard = 2.1f;
    [SerializeField] private float zKeyboard = 0.4f;

    [Header("CutZone")]
    [SerializeField] private float xCutzone = 0f;
    [SerializeField] private float yCutzone = 1.6f;
    [SerializeField] private float zCutzone = 1.2f;

    [Header("Worktable")]
    [SerializeField] private float xWorktable = 0f;
    [SerializeField] private float yWorktable = 3.2f;
    [SerializeField] private float zWorktable = 1f;

    [Header("StartButton")]
    [SerializeField] private float xStartButton = -0.15f;
    [SerializeField] private float yStartButton = 1.02f;
    [SerializeField] private float zStartButton = 3.1f;

    [Header("EStop")]
    [SerializeField] private float xEStop = 1.6f;
    [SerializeField] private float yEStop = 2.3f;
    [SerializeField] private float zEStop = 0.27f;

    [Header("Blade")]
    [SerializeField] private float xBlade = -0.13f;
    [SerializeField] private float yBlade = 1.43f;
    [SerializeField] private float zBlade = 1.02f;

    [Header("Holder")]
    [SerializeField] private float xHolder = -0.13f;
    [SerializeField] private float yHolder = 1.43f;
    [SerializeField] private float zHolder = 1.02f;

    private void Start()
    {
        dualModeController = GetComponent<CameraDualModeController>();
        targetPosition = transform.position;
        targetRotation = transform.rotation;

        ResetPartAnimations();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            SnapToStart();
        }

        if (!isMoving) return;

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
        transform.rotation = targetRotation;

        bool positionReached = Vector3.Distance(transform.position, targetPosition) < 0.01f;
        bool rotationReached = Quaternion.Angle(transform.rotation, targetRotation) < 0.1f;

        if (positionReached && rotationReached)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
            isMoving = false;

            if (dualModeController != null)
                dualModeController.EndScriptedControl();
        }
    }

    public void MoveToView(Vector3 newPosition, Quaternion newRotation)
    {
        if (dualModeController != null)
            dualModeController.BeginScriptedControl();

        targetPosition = newPosition;
        targetRotation = newRotation;
        transform.rotation = targetRotation;
        isMoving = true;
    }

    public void MoveToAnchor(Transform anchor)
    {
        if (anchor == null) return;

        MoveToView(anchor.position, anchor.rotation);
    }

    private void MoveBackZ(float x, float y, float z)
    {
        MoveToView(
            new Vector3(x, y, z),
            Quaternion.LookRotation(Vector3.back, Vector3.up)
        );
    }

    private void MoveDownY(float x, float y, float z)
    {
        MoveToView(
            new Vector3(x, y, z),
            Quaternion.LookRotation(Vector3.down, Vector3.back)
        );
    }

    private void ResetPartAnimations()
    {
        if (bladeAnimator != null)
            bladeAnimator.SetBool("h", false);

        if (holderAnimator != null)
            holderAnimator.SetBool("h", false);
    }

    private void FocusBlade()
    {
        if (bladeAnimator != null)
            bladeAnimator.SetBool("h", true);

        if (holderAnimator != null)
            holderAnimator.SetBool("h", false);
    }

    private void FocusHolder()
    {
        if (bladeAnimator != null)
            bladeAnimator.SetBool("h", false);

        if (holderAnimator != null)
            holderAnimator.SetBool("h", true);
    }

    public void ReturnToStart()
    {
        ResetPartAnimations();
        MoveToAnchor(startCameraAnchor);
    }

    public void SnapToStart()
    {
        if (startCameraAnchor == null) return;

        ResetPartAnimations();

        if (dualModeController != null)
            dualModeController.BeginScriptedControl();

        isMoving = false;
        transform.position = startCameraAnchor.position;
        transform.rotation = startCameraAnchor.rotation;

        targetPosition = transform.position;
        targetRotation = transform.rotation;

        if (dualModeController != null)
            dualModeController.EndScriptedControl();
    }

    public void MoveToScreen()
    {
        ResetPartAnimations();
        MoveBackZ(xScreen, yScreen, zScreen);
    }

    public void MoveToKeyboard()
    {
        ResetPartAnimations();
        MoveBackZ(xKeyboard, yKeyboard, zKeyboard);
    }

    public void MoveToCutZone()
    {
        ResetPartAnimations();
        MoveBackZ(xCutzone, yCutzone, zCutzone);
    }

    public void MoveToWorktable()
    {
        ResetPartAnimations();
        MoveDownY(xWorktable, yWorktable, zWorktable);
    }

    public void MoveToStartButton()
    {
        ResetPartAnimations();
        MoveBackZ(xStartButton, yStartButton, zStartButton);
    }

    public void MoveToEStop()
    {
        ResetPartAnimations();
        MoveBackZ(xEStop, yEStop, zEStop);
    }

    public void MoveToBlade()
    {
        FocusBlade();
        MoveBackZ(xBlade, yBlade, zBlade);
    }

    public void MoveToHolder()
    {
        FocusHolder();
        MoveBackZ(xHolder, yHolder, zHolder);
    }
}
