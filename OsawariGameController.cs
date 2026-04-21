using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class OsawariGameController : MonoBehaviour
{
    [Header("Layered Visual Images")]
    public Image backgroundImage;
    public Image bodyImage;
    public Image faceImage;
    public Image manMouthImage;
    public Image manRightHandImage;
    public Image manLeftHandImage;
    public Image manCrotchImage;
    public Image subImage;

    [Header("Area Actions (configure 12 keys: Face/LeftBreast/RightBreast/Mata x Left/Right/Long)")]
    public List<AreaActionDefinition> areaActions = new List<AreaActionDefinition>();

    [Header("Controls")]
    public Button autoToggleButton;
    public Button stopActionButton;
    public Button backgroundChangeButton;
    public Slider speedSlider;

    [Header("Backgrounds")]
    public List<Sprite> backgrounds = new List<Sprite>();

    [Header("Outfit State")]
    public TopOutfit currentTopOutfit = TopOutfit.Sweater;
    public BottomOutfit currentBottomOutfit = BottomOutfit.Skirt;

    [Header("Face Thresholds (fixed)")]
    public float faceLevel2Threshold = 34f;
    public float faceLevel3Threshold = 67f;

    [Header("Status Values")]
    public float kandoValue;
    // Minimal scaffold for upcoming game logic; currently updated per-action in ValueTickCoroutine.
    public float excitementValue;
    public float minValue = 0f;
    public float maxValue = 100f;

    [Header("Timing")]
    public float fallbackAutoBaseInterval = 0.5f;
    public float stoppedImageDuration = 1.5f;

    [Header("Stopped Visual Sets")]
    public StoppedVisualSet stoppedVisual;
    public StoppedVisualSet stoppedAfterVisual;

    [Header("Optional Auto Toggle Indicator")]
    public Image autoToggleIndicator;
    public Sprite autoOffSprite;
    public Sprite autoOnSprite;

    private AreaActionDefinition currentAction;
    private TouchArea? currentArea;
    private bool autoToggleOn;
    private bool isStopped;
    private bool isGameplayInputLocked;
    private bool isOutfitInputPaused;
    private int backgroundIndex;
    private Coroutine valueCoroutine;
    private Coroutine stopTransitionCoroutine;
    private Coroutine randomOnomatopoeiaCoroutine;
    private int stopTransitionToken;
    private readonly Dictionary<MenSlot, SlotRuntimeState> slotStates = new Dictionary<MenSlot, SlotRuntimeState>();

    private AudioSource sfxAudioSource;
    private AudioSource stoppedLoopAudioSource;

    private void Awake()
    {
        sfxAudioSource = gameObject.AddComponent<AudioSource>();

        stoppedLoopAudioSource = gameObject.AddComponent<AudioSource>();
        stoppedLoopAudioSource.playOnAwake = false;
        stoppedLoopAudioSource.loop = true;

        if (autoToggleButton != null)
        {
            autoToggleButton.onClick.AddListener(HandleAutoToggleButton);
        }

        if (stopActionButton != null)
        {
            stopActionButton.onClick.AddListener(HandleStopActionButton);
        }

        if (backgroundChangeButton != null)
        {
            backgroundChangeButton.onClick.AddListener(HandleBackgroundChangeButton);
        }

        InitializeSlotStates();
        HideAllMenSlotImages();
        UpdateAutoToggleIndicator();
        ApplyBackground();
    }

    // Backward-compatible helper: treat legacy area click as left input.
    public void HandleAreaClick(TouchArea area)
    {
        HandleAreaInput(area, AreaInputTrigger.Left);
    }

    public void HandleAreaInput(TouchArea area, AreaInputTrigger trigger)
    {
        if (IsGameplayInputBlocked)
        {
            return;
        }

        AreaActionDefinition action = FindAreaAction(area, trigger);
        if (action == null)
        {
            return;
        }

        if (isStopped)
        {
            ExitStoppedState();
        }

        TouchArea normalizedArea = NormalizeArea(area);
        bool actionChanged = currentAction != action;
        currentAction = action;
        currentArea = normalizedArea;
        isStopped = false;

        StopStoppedTransition();
        StopStoppedLoopAudio();

        PlayOneShot(action.startClip, action.audioMixerGroup);
        bool forceAutoFromLongPress = trigger == AreaInputTrigger.Long;
        StartOrUpdateSlot(action, normalizedArea, forceAutoFromLongPress);
        ApplyPose();

        if (actionChanged || forceAutoFromLongPress)
        {
            StartValueTickerIfNeeded();
            StartRandomOnomatopoeiaIfNeeded();
        }
    }

    public void HandleAutoToggleButton()
    {
        if (IsGameplayInputBlocked)
        {
            return;
        }

        autoToggleOn = !autoToggleOn;
        UpdateAutoToggleIndicator();

        if (autoToggleOn)
        {
            return;
        }

        foreach (var pair in slotStates)
        {
            MenSlot slot = pair.Key;
            SlotRuntimeState state = pair.Value;
            if (state.isAutoRunning && !state.isForcedAuto)
            {
                StopSlotAuto(slot, false);
            }
        }
    }

    public void HandleStopActionButton()
    {
        if (IsGameplayInputBlocked)
        {
            return;
        }

        EnterStoppedState();
    }

    public void HandleBackgroundChangeButton()
    {
        if (backgrounds == null || backgrounds.Count == 0)
        {
            Debug.LogWarning("Background change requested but no backgrounds are assigned.");
            return;
        }

        backgroundIndex = (backgroundIndex + 1) % backgrounds.Count;
        ApplyBackground();
    }

    // Optional hook for dialogue/event systems.
    public void SetBackground(Sprite sprite)
    {
        if (backgroundImage == null)
        {
            return;
        }

        backgroundImage.sprite = sprite;
    }

    public bool IsGameplayInputBlocked
    {
        get { return isGameplayInputLocked || isOutfitInputPaused; }
    }

    public void SetGameplayInputLock(bool isLocked)
    {
        isGameplayInputLocked = isLocked;
    }

    public void SetOutfitInputPause(bool isPaused)
    {
        isOutfitInputPaused = isPaused;
    }

    private void EnterStoppedState()
    {
        isStopped = true;
        StopAllSlots();
        StopValueTicker();
        StopRandomOnomatopoeia();

        ApplyStoppedVisual(stoppedVisual);

        if (stopTransitionCoroutine != null)
        {
            StopCoroutine(stopTransitionCoroutine);
        }

        stopTransitionToken++;
        int token = stopTransitionToken;
        stopTransitionCoroutine = StartCoroutine(StoppedAfterTransitionCoroutine(token));
    }

    private void ExitStoppedState()
    {
        isStopped = false;
        stopTransitionToken++;
        StopStoppedTransition();
        StopStoppedLoopAudio();

        HideAllMenSlotImages();
        ApplyPose();
        StartValueTickerIfNeeded();
        StartRandomOnomatopoeiaIfNeeded();
    }

    private IEnumerator StoppedAfterTransitionCoroutine(int token)
    {
        yield return new WaitForSeconds(stoppedImageDuration);

        if (!isStopped || token != stopTransitionToken)
        {
            yield break;
        }

        ApplyStoppedVisual(stoppedAfterVisual);
    }

    private void ApplyStoppedVisual(StoppedVisualSet visual)
    {
        if (visual == null)
        {
            return;
        }

        ApplyFrameToImage(bodyImage, visual.bodySprite);
        ApplyFrameToImage(faceImage, visual.faceSprite);
        SetSlotImageSprite(MenSlot.Mouth, visual.manMouthSprite ?? visual.manSprite);
        SetSlotImageSprite(MenSlot.RightHand, visual.manRightHandSprite ?? visual.manSprite);
        SetSlotImageSprite(MenSlot.LeftHand, visual.manLeftHandSprite ?? visual.manSprite);
        SetSlotImageSprite(MenSlot.Crotch, visual.manCrotchSprite ?? visual.manSprite);
        ApplyFrameToImage(subImage, visual.subSprite);

        PlayOneShot(visual.oneShotClip, visual.audioMixerGroup);
        PlayLoop(stoppedLoopAudioSource, visual.loopClip, visual.audioMixerGroup);
    }

    private void ApplyPose()
    {
        if (currentAction == null || !currentArea.HasValue || isStopped)
        {
            return;
        }

        PoseSet pose = FindPose(currentAction, currentArea.Value);
        if (pose == null)
        {
            return;
        }

        int displayFrameIndex = GetDisplayFrameIndexForLastArea();
        ApplyFrameToImage(bodyImage, SelectFrame(pose.bodyFrames, displayFrameIndex));
        ApplyFrameToImage(subImage, SelectFrame(pose.subFrames, displayFrameIndex));
        ApplyFrameToImage(faceImage, SelectFaceByKando(pose));
        ApplyAllSlotMenImages();
    }

    private void ApplyBackground()
    {
        if (backgroundImage == null || backgrounds == null || backgrounds.Count == 0)
        {
            return;
        }

        backgroundIndex = Mathf.Clamp(backgroundIndex, 0, backgrounds.Count - 1);
        backgroundImage.sprite = backgrounds[backgroundIndex];
    }

    private void ApplyFrameToImage(Image image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
    }

    private Sprite SelectFaceByKando(PoseSet pose)
    {
        int faceLevel = ResolveFaceLevel();
        if (faceLevel == 3)
        {
            return FirstAvailableByPriority(pose.faceLevel3, pose.faceLevel2, pose.faceLevel1);
        }

        if (faceLevel == 2)
        {
            return FirstAvailableByPriority(pose.faceLevel2, pose.faceLevel3, pose.faceLevel1);
        }

        return FirstAvailableByPriority(pose.faceLevel1, pose.faceLevel2, pose.faceLevel3);
    }

    private Sprite FirstAvailableByPriority(Sprite primary, Sprite secondary, Sprite tertiary)
    {
        return primary ?? secondary ?? tertiary;
    }

    private int ResolveFaceLevel()
    {
        if (kandoValue >= faceLevel3Threshold)
        {
            return 3;
        }

        if (kandoValue >= faceLevel2Threshold)
        {
            return 2;
        }

        return 1;
    }

    private void StartValueTickerIfNeeded()
    {
        if (valueCoroutine != null)
        {
            StopCoroutine(valueCoroutine);
        }

        if (currentAction == null)
        {
            valueCoroutine = null;
            return;
        }

        valueCoroutine = StartCoroutine(ValueTickCoroutine());
    }

    private void StopValueTicker()
    {
        if (valueCoroutine != null)
        {
            StopCoroutine(valueCoroutine);
            valueCoroutine = null;
        }
    }

    private IEnumerator ValueTickCoroutine()
    {
        while (currentAction != null)
        {
            float waitSeconds = Mathf.Max(0.01f, currentAction.valueTickSeconds);
            yield return new WaitForSeconds(waitSeconds);

            if (isStopped)
            {
                continue;
            }

            kandoValue = Mathf.Clamp(kandoValue + currentAction.kandoDeltaPerTick, minValue, maxValue);
            excitementValue = Mathf.Clamp(excitementValue + currentAction.excitementDeltaPerTick, minValue, maxValue);

            ApplyPose();
        }
    }

    private void StartRandomOnomatopoeiaIfNeeded()
    {
        StopRandomOnomatopoeia();

        if (currentAction == null || currentAction.randomChannels == null || currentAction.randomChannels.Count == 0)
        {
            return;
        }

        randomOnomatopoeiaCoroutine = StartCoroutine(RandomOnomatopoeiaCoroutine());
    }

    private void StopRandomOnomatopoeia()
    {
        if (randomOnomatopoeiaCoroutine != null)
        {
            StopCoroutine(randomOnomatopoeiaCoroutine);
            randomOnomatopoeiaCoroutine = null;
        }

        if (currentAction?.randomChannels == null)
        {
            return;
        }

        foreach (var channel in currentAction.randomChannels)
        {
            if (channel?.targetImage == null)
            {
                continue;
            }

            channel.targetImage.gameObject.SetActive(false);
        }
    }

    private IEnumerator RandomOnomatopoeiaCoroutine()
    {
        while (!isStopped)
        {
            AreaActionDefinition activeAction = currentAction;
            if (activeAction == null || !currentArea.HasValue)
            {
                yield break;
            }

            TouchArea activeArea = currentArea.Value;

            foreach (var channel in activeAction.randomChannels)
            {
                if (channel == null || channel.targetImage == null || channel.sprites == null || channel.sprites.Length == 0)
                {
                    continue;
                }

                if (channel.filterByArea && channel.area != activeArea)
                {
                    channel.targetImage.gameObject.SetActive(false);
                    continue;
                }

                channel.targetImage.gameObject.SetActive(true);
                channel.targetImage.sprite = channel.sprites[UnityEngine.Random.Range(0, channel.sprites.Length)];
            }

            yield return new WaitForSeconds(activeAction.randomSpriteInterval);
        }
    }

    private void InitializeSlotStates()
    {
        slotStates.Clear();
        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            slotStates[slot] = new SlotRuntimeState();
        }
    }

    private void StartOrUpdateSlot(AreaActionDefinition action, TouchArea area, bool forceAuto)
    {
        if (action == null)
        {
            return;
        }

        if (action.onlyExclusive)
        {
            StopAllSlots();
        }
        else
        {
            MenSlot? exclusiveSlot = GetActiveExclusiveSlot();
            if (exclusiveSlot.HasValue)
            {
                StopSlot(exclusiveSlot.Value);
            }
        }

        MenSlot targetSlot = action.targetMenSlot;
        SlotRuntimeState state = slotStates[targetSlot];
        AreaPlayMode mode = forceAuto ? AreaPlayMode.AutoOnly : GetAreaPlayMode(action, area);
        bool areaChanged = !state.area.HasValue || state.area.Value != area;
        bool actionChanged = state.action != action;

        if (forceAuto || actionChanged || areaChanged || !state.isActive)
        {
            StartSlot(targetSlot, action, area, mode, forceAuto);
            return;
        }

        if (mode == AreaPlayMode.AutoOnly)
        {
            if (!state.isAutoRunning)
            {
                StartSlotAuto(targetSlot, action, true);
            }

            return;
        }

        if (mode == AreaPlayMode.Both && state.isAutoRunning && !state.isForcedAuto)
        {
            autoToggleOn = false;
            UpdateAutoToggleIndicator();
            StopSlotAuto(targetSlot, false);
            return;
        }

        AdvanceSlotFrame(targetSlot, action);
    }

    private void StartSlot(MenSlot slot, AreaActionDefinition action, TouchArea area, AreaPlayMode mode, bool forceAuto)
    {
        StopSlot(slot);

        SlotRuntimeState state = slotStates[slot];
        state.action = action;
        state.area = area;
        state.frameIndex = 0;
        state.isActive = true;
        state.isAutoRunning = false;
        state.isForcedAuto = false;
        slotStates[slot] = state;

        if (forceAuto || mode == AreaPlayMode.AutoOnly)
        {
            StartSlotAuto(slot, action, true);
        }
        else if (mode == AreaPlayMode.Both && autoToggleOn)
        {
            StartSlotAuto(slot, action, false);
        }
    }

    private void StartSlotAuto(MenSlot slot, AreaActionDefinition action, bool forced)
    {
        StopSlotAuto(slot, false);

        SlotRuntimeState state = slotStates[slot];
        state.isAutoRunning = true;
        state.isForcedAuto = forced;
        state.autoCoroutine = StartCoroutine(SlotAutoFrameCoroutine(slot));
        slotStates[slot] = state;
    }

    private void StopSlotAuto(MenSlot slot, bool clearForced)
    {
        SlotRuntimeState state = slotStates[slot];
        if (state.autoCoroutine != null)
        {
            StopCoroutine(state.autoCoroutine);
            state.autoCoroutine = null;
        }

        state.isAutoRunning = false;
        if (clearForced)
        {
            state.isForcedAuto = false;
        }

        slotStates[slot] = state;
    }

    private void StopSlot(MenSlot slot)
    {
        StopSlotAuto(slot, true);

        SlotRuntimeState state = slotStates[slot];
        state.action = null;
        state.area = null;
        state.frameIndex = 0;
        state.isActive = false;
        slotStates[slot] = state;

        SetSlotImageSprite(slot, null);
    }

    private void StopAllSlots()
    {
        foreach (var pair in slotStates)
        {
            StopSlot(pair.Key);
        }
    }

    private MenSlot? GetActiveExclusiveSlot()
    {
        foreach (var pair in slotStates)
        {
            SlotRuntimeState state = pair.Value;
            if (state.isActive && state.action != null && state.action.onlyExclusive)
            {
                return pair.Key;
            }
        }

        return null;
    }

    private IEnumerator SlotAutoFrameCoroutine(MenSlot slot)
    {
        while (true)
        {
            SlotRuntimeState state = slotStates[slot];
            if (!state.isAutoRunning || state.action == null || !state.area.HasValue)
            {
                yield break;
            }

            yield return new WaitForSeconds(GetAutoInterval(state.action));

            if (isStopped)
            {
                continue;
            }

            state = slotStates[slot];
            if (!state.isAutoRunning || state.action == null)
            {
                continue;
            }

            AdvanceSlotFrame(slot, state.action);
        }
    }

    private void AdvanceSlotFrame(MenSlot slot, AreaActionDefinition action)
    {
        SlotRuntimeState state = slotStates[slot];
        if (action == null || !state.area.HasValue)
        {
            return;
        }

        PoseSet pose = FindPose(action, state.area.Value);
        int frameCount = GetMaxFrameCountForSlot(pose, slot);
        state.frameIndex = (state.frameIndex + 1) % frameCount;
        slotStates[slot] = state;
        ApplyPose();
    }

    private int GetDisplayFrameIndexForLastArea()
    {
        if (currentAction == null)
        {
            return 0;
        }

        MenSlot slot = currentAction.targetMenSlot;
        SlotRuntimeState state = slotStates[slot];
        return state.isActive ? state.frameIndex : 0;
    }

    private void ApplyAllSlotMenImages()
    {
        foreach (var pair in slotStates)
        {
            MenSlot slot = pair.Key;
            SlotRuntimeState state = pair.Value;
            if (!state.isActive || state.action == null || !state.area.HasValue)
            {
                SetSlotImageSprite(slot, null);
                continue;
            }

            PoseSet pose = FindPose(state.action, state.area.Value);
            Sprite frame = SelectFrame(GetSlotFrames(pose, slot), state.frameIndex);
            SetSlotImageSprite(slot, frame);
        }
    }

    private float GetAutoInterval(AreaActionDefinition action)
    {
        float baseInterval = fallbackAutoBaseInterval;
        if (action != null && action.autoBaseInterval > 0f)
        {
            baseInterval = action.autoBaseInterval;
        }

        float speedMultiplier = 1f;
        if (speedSlider != null)
        {
            speedMultiplier = Mathf.Max(0.01f, speedSlider.value);
        }

        return baseInterval / speedMultiplier;
    }

    private PoseSet FindPose(AreaActionDefinition action, TouchArea area)
    {
        if (action?.poseEntries != null)
        {
            for (int i = 0; i < action.poseEntries.Count; i++)
            {
                PoseKeyEntry entry = action.poseEntries[i];
                if (entry == null || entry.poseSet == null)
                {
                    continue;
                }

                if (entry.area == area && entry.topOutfit == currentTopOutfit && entry.bottomOutfit == currentBottomOutfit)
                {
                    return entry.poseSet;
                }
            }
        }

        return action != null ? action.fallbackPose : null;
    }

    private AreaPlayMode GetAreaPlayMode(AreaActionDefinition action, TouchArea area)
    {
        if (action?.areaModes != null)
        {
            for (int i = 0; i < action.areaModes.Count; i++)
            {
                AreaPlayModeEntry modeEntry = action.areaModes[i];
                if (modeEntry != null && modeEntry.area == area)
                {
                    return modeEntry.playMode;
                }
            }
        }

        return action != null ? action.defaultPlayMode : AreaPlayMode.Both;
    }

    private void StopStoppedTransition()
    {
        if (stopTransitionCoroutine != null)
        {
            StopCoroutine(stopTransitionCoroutine);
            stopTransitionCoroutine = null;
        }
    }

    private Sprite SelectFrame(Sprite[] frames, int index)
    {
        if (frames == null || frames.Length == 0)
        {
            return null;
        }

        int safeIndex = Mathf.Clamp(index, 0, frames.Length - 1);
        return frames[safeIndex];
    }

    private int SafeLength(Array array)
    {
        return array == null ? 0 : array.Length;
    }

    private Sprite[] GetSlotFrames(PoseSet pose, MenSlot slot)
    {
        if (pose == null)
        {
            return null;
        }

        switch (slot)
        {
            case MenSlot.Mouth:
                return pose.manMouthFrames ?? pose.manFrames;
            case MenSlot.RightHand:
                return pose.manRightHandFrames ?? pose.manFrames;
            case MenSlot.LeftHand:
                return pose.manLeftHandFrames ?? pose.manFrames;
            case MenSlot.Crotch:
                return pose.manCrotchFrames ?? pose.manFrames;
            default:
                return pose.manFrames;
        }
    }

    private int GetMaxFrameCountForSlot(PoseSet pose, MenSlot slot)
    {
        return Mathf.Max(
            SafeLength(GetSlotFrames(pose, slot)),
            SafeLength(pose?.bodyFrames),
            SafeLength(pose?.subFrames),
            1
        );
    }

    private Image GetSlotImage(MenSlot slot)
    {
        switch (slot)
        {
            case MenSlot.Mouth:
                return manMouthImage;
            case MenSlot.RightHand:
                return manRightHandImage;
            case MenSlot.LeftHand:
                return manLeftHandImage;
            case MenSlot.Crotch:
                return manCrotchImage;
            default:
                return null;
        }
    }

    private void SetSlotImageSprite(MenSlot slot, Sprite sprite)
    {
        Image image = GetSlotImage(slot);
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.gameObject.SetActive(sprite != null);
    }

    private void HideAllMenSlotImages()
    {
        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            SetSlotImageSprite(slot, null);
        }
    }

    private void PlayOneShot(AudioClip clip, AudioMixerGroup mixerGroup)
    {
        if (clip == null)
        {
            return;
        }

        sfxAudioSource.outputAudioMixerGroup = mixerGroup;
        sfxAudioSource.PlayOneShot(clip);
    }

    private void PlayLoop(AudioSource source, AudioClip clip, AudioMixerGroup mixerGroup)
    {
        if (source == null)
        {
            return;
        }

        if (clip == null)
        {
            source.Stop();
            source.clip = null;
            return;
        }

        source.outputAudioMixerGroup = mixerGroup;

        if (source.clip == clip && source.isPlaying)
        {
            return;
        }

        source.Stop();
        source.clip = clip;
        source.Play();
    }

    private void StopStoppedLoopAudio()
    {
        if (stoppedLoopAudioSource == null)
        {
            return;
        }

        stoppedLoopAudioSource.Stop();
        stoppedLoopAudioSource.clip = null;
    }

    private class SlotRuntimeState
    {
        public AreaActionDefinition action;
        public TouchArea? area;
        public int frameIndex;
        public bool isActive;
        public bool isAutoRunning;
        public bool isForcedAuto;
        public Coroutine autoCoroutine;
    }

    private void UpdateAutoToggleIndicator()
    {
        if (autoToggleIndicator == null)
        {
            return;
        }

        if (autoToggleOn && autoOnSprite != null)
        {
            autoToggleIndicator.sprite = autoOnSprite;
            return;
        }

        if (!autoToggleOn && autoOffSprite != null)
        {
            autoToggleIndicator.sprite = autoOffSprite;
        }
    }

    private AreaActionDefinition FindAreaAction(TouchArea area, AreaInputTrigger trigger)
    {
        if (areaActions == null)
        {
            return null;
        }

        ActionKey actionKey = BuildActionKey(area, trigger);
        for (int i = 0; i < areaActions.Count; i++)
        {
            AreaActionDefinition action = areaActions[i];
            if (action != null && action.actionKey == actionKey)
            {
                return action;
            }
        }

        return null;
    }

    private ActionKey BuildActionKey(TouchArea area, AreaInputTrigger trigger)
    {
        TouchArea normalizedArea = NormalizeArea(area);
        switch (normalizedArea)
        {
            case TouchArea.Face:
                return trigger == AreaInputTrigger.Right
                    ? ActionKey.FaceRight
                    : (trigger == AreaInputTrigger.Long ? ActionKey.FaceLong : ActionKey.FaceLeft);
            case TouchArea.LeftBreast:
                return trigger == AreaInputTrigger.Right
                    ? ActionKey.LeftBreastRight
                    : (trigger == AreaInputTrigger.Long ? ActionKey.LeftBreastLong : ActionKey.LeftBreastLeft);
            case TouchArea.RightBreast:
                return trigger == AreaInputTrigger.Right
                    ? ActionKey.RightBreastRight
                    : (trigger == AreaInputTrigger.Long ? ActionKey.RightBreastLong : ActionKey.RightBreastLeft);
            default:
                return trigger == AreaInputTrigger.Right
                    ? ActionKey.MataRight
                    : (trigger == AreaInputTrigger.Long ? ActionKey.MataLong : ActionKey.MataLeft);
        }
    }

    private TouchArea NormalizeArea(TouchArea area)
    {
        return area == TouchArea.Crotch ? TouchArea.Mata : area;
    }
}

public enum TouchArea
{
    Face,
    RightBreast,
    LeftBreast,
    Mata,
    // Legacy alias kept for compatibility with existing inspector assignments.
    Crotch = Mata
}

public enum AreaInputTrigger
{
    Left,
    Right,
    Long
}

public enum ActionKey
{
    FaceLeft,
    FaceRight,
    FaceLong,
    LeftBreastLeft,
    LeftBreastRight,
    LeftBreastLong,
    RightBreastLeft,
    RightBreastRight,
    RightBreastLong,
    MataLeft,
    MataRight,
    MataLong
}

public enum TopOutfit
{
    Sweater,
    Bra,
    Topless
}

public enum BottomOutfit
{
    Skirt,
    Panties,
    Nude
}

public enum AreaPlayMode
{
    ClickOnly,
    AutoOnly,
    Both
}

public enum MenSlot
{
    Mouth,
    RightHand,
    LeftHand,
    Crotch
}

[Serializable]
public class PoseSet
{
    public Sprite[] bodyFrames;
    public Sprite[] manMouthFrames;
    public Sprite[] manRightHandFrames;
    public Sprite[] manLeftHandFrames;
    public Sprite[] manCrotchFrames;
    // Legacy single-slot fallback.
    public Sprite[] manFrames;
    public Sprite[] subFrames;
    public Sprite faceLevel1;
    public Sprite faceLevel2;
    public Sprite faceLevel3;
}

[Serializable]
public class PoseKeyEntry
{
    public TouchArea area;
    public TopOutfit topOutfit;
    public BottomOutfit bottomOutfit;
    public PoseSet poseSet;
}

[Serializable]
public class AreaPlayModeEntry
{
    public TouchArea area;
    public AreaPlayMode playMode = AreaPlayMode.Both;
}

[Serializable]
public class RandomSpriteChannel
{
    public bool filterByArea;
    public TouchArea area;
    public Image targetImage;
    public Sprite[] sprites;
}

[Serializable]
public class StoppedVisualSet
{
    public Sprite bodySprite;
    public Sprite faceSprite;
    public Sprite manMouthSprite;
    public Sprite manRightHandSprite;
    public Sprite manLeftHandSprite;
    public Sprite manCrotchSprite;
    // Legacy single-slot fallback.
    public Sprite manSprite;
    public Sprite subSprite;

    public AudioClip oneShotClip;
    public AudioClip loopClip;
    public AudioMixerGroup audioMixerGroup;
}

[Serializable]
public class AreaActionDefinition
{
    public ActionKey actionKey;
    public string actionName;
    public MenSlot targetMenSlot = MenSlot.Mouth;
    public bool onlyExclusive;

    [Header("Play Modes")]
    public AreaPlayMode defaultPlayMode = AreaPlayMode.Both;
    public List<AreaPlayModeEntry> areaModes = new List<AreaPlayModeEntry>();

    [Header("Pose Data by Area + Outfit")]
    public List<PoseKeyEntry> poseEntries = new List<PoseKeyEntry>();
    public PoseSet fallbackPose;

    [Header("Frame/Auto")]
    public float autoBaseInterval = 0.5f;

    [Header("Status Delta (per tick)")]
    public float valueTickSeconds = 1f;
    public float kandoDeltaPerTick;
    public float excitementDeltaPerTick;

    [Header("Action Audio")]
    public AudioClip startClip;
    public AudioClip loopClip;
    public AudioMixerGroup audioMixerGroup;

    [Header("Optional Random Onomatopoeia")]
    public float randomSpriteInterval = 0.6f;
    public List<RandomSpriteChannel> randomChannels = new List<RandomSpriteChannel>();
}

public struct PoseKey : IEquatable<PoseKey>
{
    public readonly TouchArea area;
    public readonly TopOutfit top;
    public readonly BottomOutfit bottom;

    public PoseKey(TouchArea area, TopOutfit top, BottomOutfit bottom)
    {
        this.area = area;
        this.top = top;
        this.bottom = bottom;
    }

    public bool Equals(PoseKey other)
    {
        return area == other.area && top == other.top && bottom == other.bottom;
    }

    public override bool Equals(object obj)
    {
        return obj is PoseKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (int)area;
            hash = (hash * 31) + (int)top;
            hash = (hash * 31) + (int)bottom;
            return hash;
        }
    }
}
