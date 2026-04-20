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
    public Image manImage;
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

    [Header("Stopped Visual Sets")]
    public StoppedVisualSet stoppedVisual;
    public StoppedVisualSet stoppedAfterVisual;

    [Header("Optional Auto Toggle Indicator")]
    public Image autoToggleIndicator;
    public Sprite autoOffSprite;
    public Sprite autoOnSprite;

    private ConstantButtonData currentAction;
    private TouchArea? currentArea;
    private int currentFrameIndex;
    private bool autoToggleOn;
    private bool isAutoRunning;
    private bool isForcedAuto;
    private bool isStopped;
    private int backgroundIndex;

    private readonly Dictionary<PoseKey, PoseSet> poseLookup = new Dictionary<PoseKey, PoseSet>();
    private readonly Dictionary<TouchArea, AreaPlayMode> areaModeLookup = new Dictionary<TouchArea, AreaPlayMode>();

    private Coroutine autoCoroutine;
    private Coroutine valueCoroutine;
    private Coroutine stopTransitionCoroutine;
    private Coroutine randomOnomatopoeiaCoroutine;

    private AudioSource oneShotAudioSource;
    private AudioSource actionLoopAudioSource;
    private AudioSource stoppedLoopAudioSource;

    private void Awake()
    {
        oneShotAudioSource = gameObject.AddComponent<AudioSource>();

        actionLoopAudioSource = gameObject.AddComponent<AudioSource>();
        actionLoopAudioSource.playOnAwake = false;
        actionLoopAudioSource.loop = true;

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
    }

    // Inspector helper: assign this from each touch-area button if you prefer explicit event wiring.
    public void HandleAreaClick(TouchArea area)
    {
        if (currentAction == null)
        {
            return;
        }

        bool isAreaChanged = !currentArea.HasValue || currentArea.Value != area;
        AreaPlayMode mode = GetAreaPlayMode(area);

        if (isAutoRunning && isAreaChanged)
        {
            StopAuto(false);
        }

        if (isStopped)
        {
            ExitStoppedState();
        }

        if (isAreaChanged)
        {
            currentArea = area;
            currentFrameIndex = 0;
            ApplyPose();
            StartValueTickerIfNeeded();
            StartRandomOnomatopoeiaIfNeeded();

            if (mode == AreaPlayMode.AutoOnly)
            {
                StartAuto(true);
            }
            else if (mode == AreaPlayMode.Both && autoToggleOn)
            {
                StartAuto(false);
            }

            return;
        }

        if (mode == AreaPlayMode.AutoOnly)
        {
            if (!isAutoRunning)
            {
                StartAuto(true);
            }

            return;
        }

        if (mode == AreaPlayMode.Both && isAutoRunning)
        {
            // Same action + same area while Both auto is running: auto -> click transition.
            autoToggleOn = false;
            UpdateAutoToggleIndicator();
            StopAuto(false);
            return;
        }

        AdvanceFrameAndApply();
    }

    public void HandleConstantActionClick(ConstantButtonData action)
    {
        if (action == null)
        {
            return;
        }

        bool actionChanged = currentAction != action;
        currentAction = action;
        currentFrameIndex = 0;
        isStopped = false;

        BuildActionLookup(currentAction);

        StopStoppedTransition();
        StopAuto(false);
        StopActionLoopAudio();
        StopStoppedLoopAudio();

        PlayOneShot(action.startClip, action.audioMixerGroup);

        if (currentArea.HasValue)
        {
            ApplyPose();

            AreaPlayMode mode = GetAreaPlayMode(currentArea.Value);
            if (mode == AreaPlayMode.AutoOnly)
            {
                StartAuto(true);
            }
            else if (mode == AreaPlayMode.Both && autoToggleOn)
            {
                StartAuto(false);
            }
        }

        if (actionChanged)
        {
            StartValueTickerIfNeeded();
            StartRandomOnomatopoeiaIfNeeded();
        }
    }

    public void HandleAutoToggleButton()
    {
        autoToggleOn = !autoToggleOn;
        UpdateAutoToggleIndicator();

        if (!autoToggleOn && isAutoRunning && !isForcedAuto)
        {
            StopAuto(false);
        }
    }

    public void HandleStopActionButton()
    {
        EnterStoppedState();
    }

    public void HandleBackgroundChangeButton()
    {
        if (backgrounds == null || backgrounds.Count == 0)
        {
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

    private void EnterStoppedState()
    {
        if (currentAction == null)
        {
            return;
        }

        isStopped = true;
        StopAuto(false);
        StopValueTicker();
        StopRandomOnomatopoeia();
        StopActionLoopAudio();

        ApplyStoppedVisual(stoppedVisual);

        if (stopTransitionCoroutine != null)
        {
            StopCoroutine(stopTransitionCoroutine);
        }

        stopTransitionCoroutine = StartCoroutine(StoppedAfterTransitionCoroutine());
    }

    private void ExitStoppedState()
    {
        isStopped = false;
        StopStoppedTransition();
        StopStoppedLoopAudio();

        ApplyPose();
        StartValueTickerIfNeeded();
        StartRandomOnomatopoeiaIfNeeded();
    }

    private IEnumerator StoppedAfterTransitionCoroutine()
    {
        yield return new WaitForSeconds(stoppedImageDuration);

        if (!isStopped)
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
        ApplyFrameToImage(manImage, visual.manSprite);
        ApplyFrameToImage(subImage, visual.subSprite);

        PlayOneShot(visual.oneShotClip, visual.audioMixerGroup);
        PlayLoop(stoppedLoopAudioSource, visual.loopClip, visual.audioMixerGroup);
    }

    private void StartAuto(bool forced)
    {
        if (!currentArea.HasValue)
        {
            return;
        }

        StopAuto(false);

        isAutoRunning = true;
        isForcedAuto = forced;

        if (currentAction != null)
        {
            PlayLoop(actionLoopAudioSource, currentAction.loopClip, currentAction.audioMixerGroup);
        }

        autoCoroutine = StartCoroutine(AutoFrameCoroutine());
    }

    private void StopAuto(bool clearForced)
    {
        if (autoCoroutine != null)
        {
            StopCoroutine(autoCoroutine);
            autoCoroutine = null;
        }

        isAutoRunning = false;

        if (clearForced)
        {
            isForcedAuto = false;
        }

        StopActionLoopAudio();
    }

    private IEnumerator AutoFrameCoroutine()
    {
        while (isAutoRunning)
        {
            yield return new WaitForSeconds(GetAutoInterval());
            AdvanceFrameAndApply();
        }
    }

    private float GetAutoInterval()
    {
        float baseInterval = fallbackAutoBaseInterval;
        if (currentAction != null && currentAction.autoBaseInterval > 0f)
        {
            baseInterval = currentAction.autoBaseInterval;
        }

        float speedMultiplier = 1f;
        if (speedSlider != null)
        {
            speedMultiplier = Mathf.Max(0.01f, speedSlider.value);
        }

        return baseInterval / speedMultiplier;
    }

    private void AdvanceFrameAndApply()
    {
        if (currentAction == null || !currentArea.HasValue)
        {
            return;
        }

        PoseSet pose = FindPose(currentArea.Value);
        int frameCount = Mathf.Max(
            SafeLength(pose?.manFrames),
            SafeLength(pose?.bodyFrames),
            SafeLength(pose?.subFrames),
            1
        );

        currentFrameIndex = (currentFrameIndex + 1) % frameCount;
        ApplyPose();
    }

    private void ApplyPose()
    {
        if (currentAction == null || !currentArea.HasValue || isStopped)
        {
            return;
        }

        PoseSet pose = FindPose(currentArea.Value);
        if (pose == null)
        {
            return;
        }

        ApplyFrameToImage(bodyImage, SelectFrame(pose.bodyFrames, currentFrameIndex));
        ApplyFrameToImage(manImage, SelectFrame(pose.manFrames, currentFrameIndex));
        ApplyFrameToImage(subImage, SelectFrame(pose.subFrames, currentFrameIndex));
        ApplyFrameToImage(faceImage, SelectFaceByKando(pose));
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
            return pose.faceLevel3 != null ? pose.faceLevel3 : pose.faceLevel2;
        }

        if (faceLevel == 2)
        {
            return pose.faceLevel2 != null ? pose.faceLevel2 : pose.faceLevel1;
        }

        return pose.faceLevel1;
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
        while (currentAction != null && !isStopped)
        {
            foreach (var channel in currentAction.randomChannels)
            {
                if (channel == null || channel.targetImage == null || channel.sprites == null || channel.sprites.Length == 0)
                {
                    continue;
                }

                if (channel.filterByArea && (!currentArea.HasValue || channel.area != currentArea.Value))
                {
                    channel.targetImage.gameObject.SetActive(false);
                    continue;
                }

                channel.targetImage.gameObject.SetActive(true);
                channel.targetImage.sprite = channel.sprites[UnityEngine.Random.Range(0, channel.sprites.Length)];
            }

            yield return new WaitForSeconds(currentAction.randomSpriteInterval);
        }
    }

    private void BuildActionLookup(ConstantButtonData action)
    {
        poseLookup.Clear();
        areaModeLookup.Clear();

        if (action == null)
        {
            return;
        }

        if (action.poseEntries != null)
        {
            foreach (var entry in action.poseEntries)
            {
                if (entry?.poseSet == null)
                {
                    continue;
                }

                var key = new PoseKey(entry.area, entry.topOutfit, entry.bottomOutfit);
                poseLookup[key] = entry.poseSet;
            }
        }

        if (action.areaModes != null)
        {
            foreach (var modeEntry in action.areaModes)
            {
                if (modeEntry == null)
                {
                    continue;
                }

                areaModeLookup[modeEntry.area] = modeEntry.playMode;
            }
        }
    }

    private PoseSet FindPose(TouchArea area)
    {
        var key = new PoseKey(area, currentTopOutfit, currentBottomOutfit);
        if (poseLookup.TryGetValue(key, out var pose))
        {
            return pose;
        }

        return currentAction != null ? currentAction.fallbackPose : null;
    }

    private AreaPlayMode GetAreaPlayMode(TouchArea area)
    {
        if (areaModeLookup.TryGetValue(area, out var mode))
        {
            return mode;
        }

        return currentAction != null ? currentAction.defaultPlayMode : AreaPlayMode.Both;
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

    private void PlayOneShot(AudioClip clip, AudioMixerGroup mixerGroup)
    {
        if (clip == null)
        {
            return;
        }

        oneShotAudioSource.outputAudioMixerGroup = mixerGroup;
        oneShotAudioSource.PlayOneShot(clip);
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
    public Sprite manSprite;
    public Sprite subSprite;

    public AudioClip oneShotClip;
    public AudioClip loopClip;
    public AudioMixerGroup audioMixerGroup;
}

[Serializable]
public class ConstantButtonData
{
    public string actionName;
    public Button button;

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
