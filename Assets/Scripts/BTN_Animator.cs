using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class btn_Animator : MonoBehaviour
{
    public static event Action<ControlPanelButton> ButtonPressed;
    public static event Action<bool> MachinePowerChanged;

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

    private static btn_Animator inputOwner;
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

    private AudioSource audioSource;
    private AudioSource machineAudioSource;
    private AudioClip buttonSound;
    private AudioClip machineLoopSound;
    private bool machinePowered;
    private Coroutine machineAudioFadeCoroutine;

    private void Awake()
    {
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

        bool zPressed = keyboard.zKey.isPressed;
        bool shiftPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (!zPressed)
        {
            ResetAllButtons();
            return;
        }

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

    private void RegisterButtons()
    {
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
        if (button == ControlPanelButton.PowerSwitch)
        {
            SetMachinePowered(!machinePowered, notifyListeners: true);
            return;
        }

        if (button == ControlPanelButton.EmergencyStop)
        {
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
        return inputOwner != null && inputOwner.TrySimulateButtonPressInternal(button);
    }

    private bool TrySimulateButtonPressInternal(ControlPanelButton button)
    {
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
        if (machineAudioSource == null)
            return;

        machineAudioSource.volume = 0f;
        machineAudioSource.Stop();
    }
}
