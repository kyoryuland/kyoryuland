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
    // Wire these to the dedicated men slot layer images.
    public Image manMouthImage;
    public Image manRightHandImage;
    public Image manLeftHandImage;
    public Image manCrotchImage;
    public Image subImage;

    [Header("Touch Area Buttons")]
    public Button faceAreaButton;
    public Button rightBreastAreaButton;
    public Button leftBreastAreaButton;
    public Button crotchAreaButton;

    [Header("Action Buttons")]
    public List<ConstantButtonData> constantButtons = new List<ConstantButtonData>();

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
    public float excitementValue;
    public float minValue = 0f;
    public float maxValue = 100f;

    [Header("Timing")]
    public float fallbackAutoBaseInterval = 0.5f;
    public float stoppedImageDuration = 1.5f;

    [Header("Stopped Face Fallbacks")]
    public FaceLevelSet fallbackStopFaceSet;
    public FaceLevelSet fallbackAfterStopFaceSet;

    [Header("Optional Auto Toggle Indicator")]
    public Image autoToggleIndicator;
    public Sprite autoOffSprite;
    public Sprite autoOnSprite;

    private readonly Dictionary<MenSlot, SlotRuntimeState> slotStates = new Dictionary<MenSlot, SlotRuntimeState>();
    private readonly Dictionary<MenSlot, Image> slotImages = new Dictionary<MenSlot, Image>();
    private readonly Dictionary<TouchArea, ActionAreaDefinition> areaDefinitionLookup = new Dictionary<TouchArea, ActionAreaDefinition>();

    private ConstantButtonData currentAction;
    private ConstantButtonData lastActionForStopFace;
    private TouchArea? lastVisualArea;
    private bool autoToggleOn;
    private bool isStopped;
    private int backgroundIndex;

    private Coroutine valueCoroutine;
    private Coroutine stopTransitionCoroutine;
    private Coroutine randomOnomatopoeiaCoroutine;
    private int stopTransitionToken;

    private AudioSource sfxAudioSource;
    private AudioSource actionLoopAudioSource;
    private AudioSource stoppedLoopAudioSource;

    private void Awake()
    {
        sfxAudioSource = gameObject.AddComponent<AudioSource>();

        actionLoopAudioSource = gameObject.AddComponent<AudioSource>();
        actionLoopAudioSource.playOnAwake = false;
        actionLoopAudioSource.loop = true;

        stoppedLoopAudioSource = gameObject.AddComponent<AudioSource>();
        stoppedLoopAudioSource.playOnAwake = false;
        stoppedLoopAudioSource.loop = true;

        slotImages[MenSlot.Mouth] = manMouthImage;
        slotImages[MenSlot.RightHand] = manRightHandImage;
        slotImages[MenSlot.LeftHand] = manLeftHandImage;
        slotImages[MenSlot.Crotch] = manCrotchImage;

        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            slotStates[slot] = new SlotRuntimeState();
        }

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

        if (faceAreaButton != null)
        {
            faceAreaButton.onClick.AddListener(() => HandleAreaClick(TouchArea.Face));
        }

        if (rightBreastAreaButton != null)
        {
            rightBreastAreaButton.onClick.AddListener(() => HandleAreaClick(TouchArea.RightBreast));
        }

        if (leftBreastAreaButton != null)
        {
            leftBreastAreaButton.onClick.AddListener(() => HandleAreaClick(TouchArea.LeftBreast));
        }

        if (crotchAreaButton != null)
        {
            crotchAreaButton.onClick.AddListener(() => HandleAreaClick(TouchArea.Crotch));
        }

        foreach (var action in constantButtons)
        {
            if (action?.button == null)
            {
                continue;
            }

            ConstantButtonData capturedAction = action;
            capturedAction.button.onClick.AddListener(() => HandleConstantActionClick(capturedAction));
        }

        UpdateAutoToggleIndicator();
        ApplyBackground();
        HideAllManSlotImages();
    }

    public void HandleAreaClick(TouchArea area)
    {
        if (currentAction == null)
        {
            return;
        }

        ActionAreaDefinition definition = FindAreaDefinition(area);
        if (definition == null)
        {
            return;
        }

        MenSlot targetSlot = definition.usedSlot;
        AreaPlayMode mode = ResolvePlayMode(definition);
        SlotRuntimeState state = slotStates[targetSlot];

        bool isSameAreaOnSameSlot = state.isActive && state.area == area;

        if (isSameAreaOnSameSlot)
        {
            if (mode == AreaPlayMode.AutoOnly)
            {
                if (!state.isAutoRunning)
                {
                    StartSlotAuto(targetSlot, true);
                }

                return;
            }

            if (mode == AreaPlayMode.Both && state.isAutoRunning)
            {
                autoToggleOn = false;
                UpdateAutoToggleIndicator();
                StopSlotAuto(targetSlot, false);
                EnsureRuntimeCoroutines();
                return;
            }

            AdvanceSlotFrame(targetSlot);
            lastVisualArea = area;
            ApplyPose();
            EnsureRuntimeCoroutines();
            return;
        }

        if (definition.exclusiveOnly)
        {
            StopAllSlotActions();
        }
        else
        {
            StopSlotAction(targetSlot);
        }

        SlotRuntimeState newState = slotStates[targetSlot];
        newState.isActive = true;
        newState.area = area;
        newState.mode = mode;
        newState.frameIndex = 0;
        newState.pose = FindPose(definition);
        slotStates[targetSlot] = newState;

        lastVisualArea = area;
        ApplyPose();

        if (mode == AreaPlayMode.AutoOnly)
        {
            StartSlotAuto(targetSlot, true);
        }
        else if (mode == AreaPlayMode.Both && autoToggleOn)
        {
            StartSlotAuto(targetSlot, false);
        }

        EnsureRuntimeCoroutines();
    }

    public void HandleConstantActionClick(ConstantButtonData action)
    {
        if (action == null)
        {
            return;
        }

        currentAction = action;
        isStopped = false;

        BuildActionLookup(currentAction);

        StopStoppedTransition();
        StopStoppedLoopAudio();
        StopAllSlotActions();

        PlayOneShot(action.startClip, action.audioMixerGroup);

        EnsureRuntimeCoroutines();
    }

    public void HandleAutoToggleButton()
    {
        autoToggleOn = !autoToggleOn;
        UpdateAutoToggleIndicator();

        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            SlotRuntimeState state = slotStates[slot];
            if (!state.isActive)
            {
                continue;
            }

            if (state.mode != AreaPlayMode.Both)
            {
                continue;
            }

            if (autoToggleOn)
            {
                if (!state.isAutoRunning)
                {
                    StartSlotAuto(slot, false);
                }
            }
            else if (state.isAutoRunning && !state.isForcedAuto)
            {
                StopSlotAuto(slot, false);
            }
        }

        EnsureRuntimeCoroutines();
    }

    public void HandleStopActionButton()
    {
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

    public void SetBackground(Sprite sprite)
    {
        if (backgroundImage == null)
        {
            return;
        }

        backgroundImage.sprite = sprite;
    }

    private void EnterStoppedState()
    {
        if (currentAction == null)
        {
            return;
        }

        lastActionForStopFace = currentAction;

        isStopped = true;
        StopAllSlotActions();
        HideAllManSlotImages();
        StopValueTicker();
        StopRandomOnomatopoeia();
        StopActionLoopAudio();

        ApplyStoppedFace(lastActionForStopFace?.stopFaceSet, fallbackStopFaceSet);

        currentAction = null;
        lastVisualArea = null;
        areaDefinitionLookup.Clear();

        StopStoppedTransition();
        stopTransitionToken++;
        int token = stopTransitionToken;
        stopTransitionCoroutine = StartCoroutine(StoppedAfterTransitionCoroutine(token));
    }

    private IEnumerator StoppedAfterTransitionCoroutine(int token)
    {
        yield return new WaitForSeconds(stoppedImageDuration);

        if (!isStopped || token != stopTransitionToken)
        {
            yield break;
        }

        ApplyStoppedFace(lastActionForStopFace?.afterStopFaceSet, fallbackAfterStopFaceSet);
    }

    private void ApplyStoppedFace(FaceLevelSet primary, FaceLevelSet fallback)
    {
        FaceLevelSet set = primary ?? fallback;
        if (set == null)
        {
            return;
        }

        ApplyFrameToImage(faceImage, SelectFaceByKando(set.level1, set.level2, set.level3));
        PlayOneShot(set.oneShotClip, set.audioMixerGroup);
        PlayLoop(stoppedLoopAudioSource, set.loopClip, set.audioMixerGroup);
    }

    private void StartSlotAuto(MenSlot slot, bool forced)
    {
        SlotRuntimeState state = slotStates[slot];
        if (!state.isActive)
        {
            return;
        }

        StopSlotAuto(slot, false);

        state.isAutoRunning = true;
        state.isForcedAuto = forced;
        state.autoCoroutine = StartCoroutine(AutoFrameCoroutine(slot));
        slotStates[slot] = state;

        UpdateActionLoopAudio();
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
        UpdateActionLoopAudio();
    }

    private IEnumerator AutoFrameCoroutine(MenSlot slot)
    {
        while (slotStates[slot].isAutoRunning)
        {
            SlotRuntimeState state = slotStates[slot];
            yield return new WaitForSeconds(GetAutoInterval(state.area));

            state = slotStates[slot];
            if (!state.isAutoRunning || isStopped || currentAction == null)
            {
                continue;
            }

            AdvanceSlotFrame(slot);
            lastVisualArea = state.area;
            ApplyPose();
        }
    }

    private float GetAutoInterval(TouchArea area)
    {
        float baseInterval = fallbackAutoBaseInterval;

        if (currentAction != null && currentAction.autoBaseInterval > 0f)
        {
            baseInterval = currentAction.autoBaseInterval;
        }

        ActionAreaDefinition definition = FindAreaDefinition(area);
        if (definition != null && definition.useAutoBaseIntervalOverride && definition.autoBaseIntervalOverride > 0f)
        {
            baseInterval = definition.autoBaseIntervalOverride;
        }

        float speedMultiplier = 1f;
        if (speedSlider != null)
        {
            speedMultiplier = Mathf.Max(0.01f, speedSlider.value);
        }

        return baseInterval / speedMultiplier;
    }

    private void AdvanceSlotFrame(MenSlot slot)
    {
        SlotRuntimeState state = slotStates[slot];
        if (!state.isActive)
        {
            return;
        }

        PoseSet pose = state.pose ?? FindPose(state.area);
        if (pose == null)
        {
            return;
        }

        int frameCount = Mathf.Max(
            SafeLength(GetSlotFrames(pose, slot)),
            SafeLength(pose.bodyFrames),
            SafeLength(pose.subFrames),
            1);

        state.frameIndex = (state.frameIndex + 1) % frameCount;
        state.pose = pose;
        slotStates[slot] = state;
    }

    private void ApplyPose()
    {
        if (isStopped)
        {
            return;
        }

        if (!lastVisualArea.HasValue)
        {
            RefreshManSlotVisuals();
            return;
        }

        PoseSet visualPose = FindPose(lastVisualArea.Value);
        if (visualPose == null)
        {
            RefreshManSlotVisuals();
            return;
        }

        int visualFrame = ResolveVisualFrameIndex(lastVisualArea.Value);
        ApplyFrameToImage(bodyImage, SelectFrame(visualPose.bodyFrames, visualFrame));
        ApplyFrameToImage(subImage, SelectFrame(visualPose.subFrames, visualFrame));
        ApplyFrameToImage(faceImage, SelectFaceByKando(visualPose.faceLevel1, visualPose.faceLevel2, visualPose.faceLevel3));

        RefreshManSlotVisuals();
    }

    private int ResolveVisualFrameIndex(TouchArea area)
    {
        ActionAreaDefinition definition = FindAreaDefinition(area);
        if (definition == null)
        {
            return 0;
        }

        SlotRuntimeState state = slotStates[definition.usedSlot];
        return state.isActive && state.area == area ? Mathf.Max(0, state.frameIndex) : 0;
    }

    private void RefreshManSlotVisuals()
    {
        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            SlotRuntimeState state = slotStates[slot];
            Image slotImage = GetSlotImage(slot);
            if (slotImage == null)
            {
                continue;
            }

            if (!state.isActive || isStopped || currentAction == null)
            {
                slotImage.gameObject.SetActive(false);
                continue;
            }

            PoseSet pose = state.pose ?? FindPose(state.area);
            Sprite sprite = SelectFrame(GetSlotFrames(pose, slot), state.frameIndex);
            if (sprite == null)
            {
                slotImage.gameObject.SetActive(false);
                continue;
            }

            slotImage.gameObject.SetActive(true);
            slotImage.sprite = sprite;
        }
    }

    private void HideAllManSlotImages()
    {
        foreach (var entry in slotImages)
        {
            if (entry.Value != null)
            {
                entry.Value.gameObject.SetActive(false);
            }
        }
    }

    private void StopAllSlotActions()
    {
        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            StopSlotAction(slot);
        }
    }

    private void StopSlotAction(MenSlot slot)
    {
        StopSlotAuto(slot, true);

        SlotRuntimeState state = slotStates[slot];
        state.isActive = false;
        state.pose = null;
        state.frameIndex = 0;
        slotStates[slot] = state;

        Image image = GetSlotImage(slot);
        if (image != null)
        {
            image.gameObject.SetActive(false);
        }

        EnsureRuntimeCoroutines();
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

    private Sprite SelectFaceByKando(Sprite faceLevel1, Sprite faceLevel2, Sprite faceLevel3)
    {
        int faceLevel = ResolveFaceLevel();
        if (faceLevel == 3)
        {
            return FirstAvailableByPriority(faceLevel3, faceLevel2, faceLevel1);
        }

        if (faceLevel == 2)
        {
            return FirstAvailableByPriority(faceLevel2, faceLevel3, faceLevel1);
        }

        return FirstAvailableByPriority(faceLevel1, faceLevel2, faceLevel3);
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
        if (!CanRunActionSideEffects())
        {
            StopValueTicker();
            return;
        }

        if (valueCoroutine != null)
        {
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

            if (!CanRunActionSideEffects())
            {
                continue;
            }

            kandoValue = Mathf.Clamp(kandoValue + currentAction.kandoDeltaPerTick, minValue, maxValue);
            excitementValue = Mathf.Clamp(excitementValue + currentAction.excitementDeltaPerTick, minValue, maxValue);

            ApplyPose();
        }

        valueCoroutine = null;
    }

    private void StartRandomOnomatopoeiaIfNeeded()
    {
        if (!CanRunActionSideEffects() || currentAction?.randomChannels == null || currentAction.randomChannels.Count == 0)
        {
            StopRandomOnomatopoeia();
            return;
        }

        if (randomOnomatopoeiaCoroutine != null)
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
            if (channel?.targetImage != null)
            {
                channel.targetImage.gameObject.SetActive(false);
            }
        }
    }

    private IEnumerator RandomOnomatopoeiaCoroutine()
    {
        while (CanRunActionSideEffects())
        {
            ConstantButtonData activeAction = currentAction;
            foreach (var channel in activeAction.randomChannels)
            {
                if (channel == null || channel.targetImage == null || channel.sprites == null || channel.sprites.Length == 0)
                {
                    continue;
                }

                if (channel.filterByArea && !IsAreaActive(channel.area))
                {
                    channel.targetImage.gameObject.SetActive(false);
                    continue;
                }

                channel.targetImage.gameObject.SetActive(true);
                channel.targetImage.sprite = channel.sprites[UnityEngine.Random.Range(0, channel.sprites.Length)];
            }

            yield return new WaitForSeconds(activeAction.randomSpriteInterval);
        }

        randomOnomatopoeiaCoroutine = null;
    }

    private void BuildActionLookup(ConstantButtonData action)
    {
        areaDefinitionLookup.Clear();

        if (action?.areaDefinitions == null)
        {
            return;
        }

        foreach (var definition in action.areaDefinitions)
        {
            if (definition == null)
            {
                continue;
            }

            areaDefinitionLookup[definition.area] = definition;
        }
    }

    private ActionAreaDefinition FindAreaDefinition(TouchArea area)
    {
        if (areaDefinitionLookup.TryGetValue(area, out var definition))
        {
            return definition;
        }

        if (currentAction?.fallbackActionArea != null)
        {
            return currentAction.fallbackActionArea;
        }

        return null;
    }

    private PoseSet FindPose(TouchArea area)
    {
        return FindPose(FindAreaDefinition(area));
    }

    private PoseSet FindPose(ActionAreaDefinition definition)
    {
        if (definition == null)
        {
            return currentAction != null ? currentAction.fallbackPose : null;
        }

        if (definition.poseEntries != null)
        {
            foreach (var entry in definition.poseEntries)
            {
                if (entry == null || entry.poseSet == null)
                {
                    continue;
                }

                if (entry.topOutfit == currentTopOutfit && entry.bottomOutfit == currentBottomOutfit)
                {
                    return entry.poseSet;
                }
            }
        }

        return definition.fallbackPose ?? currentAction?.fallbackPose;
    }

    private AreaPlayMode ResolvePlayMode(ActionAreaDefinition definition)
    {
        if (definition != null && definition.usePlayModeOverride)
        {
            return definition.playModeOverride;
        }

        return currentAction != null ? currentAction.defaultPlayMode : AreaPlayMode.Both;
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
                return pose.manMouthFrames;
            case MenSlot.RightHand:
                return pose.manRightHandFrames;
            case MenSlot.LeftHand:
                return pose.manLeftHandFrames;
            case MenSlot.Crotch:
                return pose.manCrotchFrames;
            default:
                return null;
        }
    }

    private bool CanRunActionSideEffects()
    {
        return currentAction != null && !isStopped && HasAnyActiveSlot();
    }

    private bool HasAnyActiveSlot()
    {
        foreach (var state in slotStates.Values)
        {
            if (state.isActive)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyAutoRunningSlot()
    {
        foreach (var state in slotStates.Values)
        {
            if (state.isAutoRunning)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAreaActive(TouchArea area)
    {
        foreach (var state in slotStates.Values)
        {
            if (state.isActive && state.area == area)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureRuntimeCoroutines()
    {
        StartValueTickerIfNeeded();
        StartRandomOnomatopoeiaIfNeeded();
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

    private Image GetSlotImage(MenSlot slot)
    {
        slotImages.TryGetValue(slot, out var image);
        return image;
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

    private void UpdateActionLoopAudio()
    {
        if (currentAction == null)
        {
            StopActionLoopAudio();
            return;
        }

        if (HasAnyAutoRunningSlot())
        {
            PlayLoop(actionLoopAudioSource, currentAction.loopClip, currentAction.audioMixerGroup);
        }
        else
        {
            StopActionLoopAudio();
        }
    }

    private void StopActionLoopAudio()
    {
        if (actionLoopAudioSource == null)
        {
            return;
        }

        actionLoopAudioSource.Stop();
        actionLoopAudioSource.clip = null;
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
}

public enum TouchArea
{
    Face,
    RightBreast,
    LeftBreast,
    Crotch
}

public enum MenSlot
{
    Mouth,
    RightHand,
    LeftHand,
    Crotch
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

[Serializable]
public class PoseSet
{
    public Sprite[] bodyFrames;
    public Sprite[] manMouthFrames;
    public Sprite[] manRightHandFrames;
    public Sprite[] manLeftHandFrames;
    public Sprite[] manCrotchFrames;
    public Sprite[] subFrames;
    public Sprite faceLevel1;
    public Sprite faceLevel2;
    public Sprite faceLevel3;
}

[Serializable]
public class PoseKeyEntry
{
    public TopOutfit topOutfit;
    public BottomOutfit bottomOutfit;
    public PoseSet poseSet;
}

[Serializable]
public class ActionAreaDefinition
{
    public TouchArea area;
    public MenSlot usedSlot;
    public bool exclusiveOnly;

    [Header("Optional Play Mode Override")]
    public bool usePlayModeOverride;
    public AreaPlayMode playModeOverride = AreaPlayMode.Both;

    [Header("Optional Auto Interval Override")]
    public bool useAutoBaseIntervalOverride;
    public float autoBaseIntervalOverride = 0.5f;

    [Header("Pose Data by Outfit")]
    public List<PoseKeyEntry> poseEntries = new List<PoseKeyEntry>();
    public PoseSet fallbackPose;
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
public class FaceLevelSet
{
    public Sprite level1;
    public Sprite level2;
    public Sprite level3;

    public AudioClip oneShotClip;
    public AudioClip loopClip;
    public AudioMixerGroup audioMixerGroup;
}

[Serializable]
public class ConstantButtonData
{
    public string actionName;
    public Button button;

    [Header("Default Play Mode")]
    public AreaPlayMode defaultPlayMode = AreaPlayMode.Both;

    [Header("Action Area Definitions")]
    public List<ActionAreaDefinition> areaDefinitions = new List<ActionAreaDefinition>();
    public ActionAreaDefinition fallbackActionArea;

    [Header("Fallback Pose")]
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

    [Header("Stopped Face by Kando")]
    public FaceLevelSet stopFaceSet;
    public FaceLevelSet afterStopFaceSet;

    [Header("Optional Random Onomatopoeia")]
    public float randomSpriteInterval = 0.6f;
    public List<RandomSpriteChannel> randomChannels = new List<RandomSpriteChannel>();
}

public class SlotRuntimeState
{
    public bool isActive;
    public TouchArea area;
    public AreaPlayMode mode;
    public int frameIndex;
    public bool isAutoRunning;
    public bool isForcedAuto;
    public Coroutine autoCoroutine;
    public PoseSet pose;
}
