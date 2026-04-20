using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
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
    public Button nextSceneButton;
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

    [Header("Conversation UI")]
    public Image conversationMaleImage;
    public Image conversationFemaleImage;

    [Header("Conversation Sequences")]
    public ConversationSequence openingConversation;
    public List<SingleUseButtonData> singleUseButtons = new List<SingleUseButtonData>();
    public List<AreaConversationData> areaConversations = new List<AreaConversationData>();
    public NextSceneData nextSceneData;

    private ConstantButtonData currentAction;
    private TouchArea? currentArea;
    private int currentFrameIndex;
    private bool autoToggleOn;
    private bool isAutoRunning;
    private bool isForcedAuto;
    private bool isStopped;
    private bool isGameplayInputLocked;
    private bool isNextSceneTransitioning;
    private int backgroundIndex;

    private readonly Dictionary<PoseKey, PoseSet> poseLookup = new Dictionary<PoseKey, PoseSet>();
    private readonly Dictionary<TouchArea, AreaPlayMode> areaModeLookup = new Dictionary<TouchArea, AreaPlayMode>();

    private Coroutine autoCoroutine;
    private Coroutine valueCoroutine;
    private Coroutine stopTransitionCoroutine;
    private Coroutine randomOnomatopoeiaCoroutine;
    private Coroutine activeConversationCoroutine;
    private Coroutine openingConversationCoroutine;
    private Coroutine nextSceneConversationCoroutine;
    private int stopTransitionToken;
    private int conversationToken;

    private AudioSource sfxAudioSource;
    private AudioSource actionLoopAudioSource;
    private AudioSource stoppedLoopAudioSource;
    private readonly HashSet<SingleUseButtonData> usedSingleUseButtons = new HashSet<SingleUseButtonData>();
    private readonly HashSet<string> areaConversationFlags = new HashSet<string>();

    private void Awake()
    {
        sfxAudioSource = gameObject.AddComponent<AudioSource>();

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

        if (nextSceneButton != null)
        {
            nextSceneButton.onClick.AddListener(HandleNextSceneButton);
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

        foreach (var singleUse in singleUseButtons)
        {
            if (singleUse?.button == null)
            {
                continue;
            }

            SingleUseButtonData capturedData = singleUse;
            capturedData.button.onClick.AddListener(() => HandleSingleUseButton(capturedData));
        }

        UpdateAutoToggleIndicator();
        ApplyBackground();
        HideConversationImages();
    }

    private void Start()
    {
        TryStartOpeningConversation();
    }

    // Inspector helper: assign this from each touch-area button if you prefer explicit event wiring.
    public void HandleAreaClick(TouchArea area)
    {
        if (isGameplayInputLocked)
        {
            return;
        }

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

            TryPlayAreaConversation(currentAction, area);
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
            // Transition from auto to click mode when user clicks the same area in Both play mode.
            autoToggleOn = false;
            UpdateAutoToggleIndicator();
            StopAuto(false);
            StartValueTickerIfNeeded();
            StartRandomOnomatopoeiaIfNeeded();
            return;
        }

        AdvanceFrameAndApply();
        TryPlayAreaConversation(currentAction, area);
    }

    public void HandleConstantActionClick(ConstantButtonData action)
    {
        if (isGameplayInputLocked)
        {
            return;
        }

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
        if (isGameplayInputLocked)
        {
            return;
        }

        autoToggleOn = !autoToggleOn;
        UpdateAutoToggleIndicator();

        if (!autoToggleOn && isAutoRunning && !isForcedAuto)
        {
            StopAuto(false);
        }
    }

    public void HandleStopActionButton()
    {
        CancelAllConversationFlowsForStop();
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

    public void HandleNextSceneButton()
    {
        if (isNextSceneTransitioning)
        {
            return;
        }

        if (nextSceneConversationCoroutine != null)
        {
            StopCoroutine(nextSceneConversationCoroutine);
            nextSceneConversationCoroutine = null;
        }

        nextSceneConversationCoroutine = StartCoroutine(HandleNextSceneConversationCoroutine());
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

    private void TryStartOpeningConversation()
    {
        if (openingConversation == null || openingConversation.turns == null || openingConversation.turns.Count == 0)
        {
            return;
        }

        SetGameplayInputLock(true);

        if (openingConversationCoroutine != null)
        {
            StopCoroutine(openingConversationCoroutine);
        }

        openingConversationCoroutine = StartCoroutine(PlaySequenceAndUnlockCoroutine(openingConversation));
    }

    private IEnumerator PlaySequenceAndUnlockCoroutine(ConversationSequence sequence)
    {
        yield return PlayConversationBlocking(sequence);

        openingConversationCoroutine = null;
        if (!isNextSceneTransitioning)
        {
            SetGameplayInputLock(false);
        }
    }

    private IEnumerator HandleNextSceneConversationCoroutine()
    {
        isNextSceneTransitioning = true;
        SetGameplayInputLock(true);

        if (nextSceneData?.conversation != null)
        {
            yield return PlayConversationBlocking(nextSceneData.conversation);
            // Stop button cancels transition by setting isNextSceneTransitioning = false.
            if (!isNextSceneTransitioning)
            {
                yield break;
            }
        }

        nextSceneConversationCoroutine = null;

        if (nextSceneData == null || string.IsNullOrEmpty(nextSceneData.sceneName))
        {
            Debug.LogWarning("Next scene conversation finished, but no next scene name is configured.");
            isNextSceneTransitioning = false;
            SetGameplayInputLock(false);
            yield break;
        }

        SceneManager.LoadScene(nextSceneData.sceneName);
    }

    private void HandleSingleUseButton(SingleUseButtonData data)
    {
        if (data == null || data.button == null || usedSingleUseButtons.Contains(data))
        {
            return;
        }

        usedSingleUseButtons.Add(data);

        if (data.hideAfterUse)
        {
            data.button.gameObject.SetActive(false);
        }
        else
        {
            data.button.interactable = false;
        }

        if (data.conversation != null)
        {
            PlayConversation(data.conversation);
        }
    }

    private void TryPlayAreaConversation(ConstantButtonData action, TouchArea area)
    {
        if (action == null || areaConversations == null || areaConversations.Count == 0)
        {
            return;
        }

        string actionKey = GetActionConversationKey(action);
        string flagKey = $"{actionKey}:{area}";
        if (areaConversationFlags.Contains(flagKey))
        {
            return;
        }

        for (int i = 0; i < areaConversations.Count; i++)
        {
            AreaConversationData candidate = areaConversations[i];
            if (candidate == null || candidate.area != area || candidate.conversation == null)
            {
                continue;
            }

            if (!string.Equals(candidate.actionName, actionKey, StringComparison.Ordinal))
            {
                continue;
            }

            areaConversationFlags.Add(flagKey);
            PlayConversation(candidate.conversation);
            break;
        }
    }

    private string GetActionConversationKey(ConstantButtonData action)
    {
        if (!string.IsNullOrEmpty(action?.actionName))
        {
            return action.actionName;
        }

        if (action?.button != null && !string.IsNullOrEmpty(action.button.name))
        {
            return action.button.name;
        }

        return string.Empty;
    }

    private void PlayConversation(ConversationSequence sequence)
    {
        BeginConversation(sequence);
    }

    private IEnumerator PlayConversationBlocking(ConversationSequence sequence)
    {
        Coroutine startedConversation = BeginConversation(sequence);
        if (startedConversation != null)
        {
            yield return startedConversation;
        }
    }

    private Coroutine BeginConversation(ConversationSequence sequence)
    {
        StopActiveConversation();
        conversationToken++;
        int token = conversationToken;

        activeConversationCoroutine = StartCoroutine(PlayConversationSequenceCoroutine(sequence, token));
        return activeConversationCoroutine;
    }

    private IEnumerator PlayConversationSequenceCoroutine(ConversationSequence sequence, int token)
    {
        HideConversationImages();

        if (sequence == null || sequence.turns == null)
        {
            CompleteConversationIfCurrent(token);
            yield break;
        }

        for (int i = 0; i < sequence.turns.Count; i++)
        {
            if (token != conversationToken)
            {
                CompleteConversationIfCurrent(token);
                yield break;
            }

            ConversationTurn turn = sequence.turns[i];
            if (turn == null)
            {
                continue;
            }

            GetSpeakerImages(turn.speaker, out Image activeImage, out Image inactiveImage);

            if (inactiveImage != null)
            {
                inactiveImage.gameObject.SetActive(false);
            }

            if (activeImage == null)
            {
                continue;
            }

            CanvasGroup canvasGroup = EnsureCanvasGroup(activeImage);
            activeImage.sprite = turn.sprite;
            activeImage.gameObject.SetActive(true);
            canvasGroup.alpha = 0f;

            yield return FadeConversation(canvasGroup, 0f, 1f, turn.fadeInDuration, token);
            if (token != conversationToken)
            {
                CompleteConversationIfCurrent(token);
                yield break;
            }

            if (turn.audioClip != null)
            {
                PlayOneShot(turn.audioClip, turn.audioMixerGroup);
            }

            if (turn.displayDuration > 0f)
            {
                yield return new WaitForSeconds(turn.displayDuration);
            }

            if (token != conversationToken)
            {
                CompleteConversationIfCurrent(token);
                yield break;
            }

            yield return FadeConversation(canvasGroup, 1f, 0f, turn.fadeOutDuration, token);
            if (token != conversationToken)
            {
                CompleteConversationIfCurrent(token);
                yield break;
            }

            activeImage.gameObject.SetActive(false);
        }

        if (token == conversationToken)
        {
            HideConversationImages();
            CompleteConversationIfCurrent(token);
        }
    }

    private IEnumerator FadeConversation(CanvasGroup canvasGroup, float from, float to, float duration, int token)
    {
        if (canvasGroup == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        canvasGroup.alpha = from;

        while (elapsed < duration)
        {
            if (token != conversationToken)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        canvasGroup.alpha = to;
    }

    private CanvasGroup EnsureCanvasGroup(Image image)
    {
        if (image == null)
        {
            return null;
        }

        CanvasGroup canvasGroup = image.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = image.gameObject.AddComponent<CanvasGroup>();
        }

        return canvasGroup;
    }

    private void StopActiveConversation()
    {
        if (activeConversationCoroutine != null)
        {
            StopCoroutine(activeConversationCoroutine);
            activeConversationCoroutine = null;
        }

        HideConversationImages();
    }

    private void CompleteConversationIfCurrent(int token)
    {
        if (token == conversationToken)
        {
            activeConversationCoroutine = null;
        }
    }

    private void GetSpeakerImages(ConversationSpeaker speaker, out Image activeImage, out Image inactiveImage)
    {
        if (speaker == ConversationSpeaker.Male)
        {
            activeImage = conversationMaleImage;
            inactiveImage = conversationFemaleImage;
            return;
        }

        activeImage = conversationFemaleImage;
        inactiveImage = conversationMaleImage;
    }

    private void CancelAllConversationFlowsForStop()
    {
        if (openingConversationCoroutine != null)
        {
            StopCoroutine(openingConversationCoroutine);
            openingConversationCoroutine = null;
        }

        if (nextSceneConversationCoroutine != null)
        {
            StopCoroutine(nextSceneConversationCoroutine);
            nextSceneConversationCoroutine = null;
        }

        isNextSceneTransitioning = false;
        SetGameplayInputLock(false);
        conversationToken++;
        StopActiveConversation();
    }

    private void HideConversationImages()
    {
        if (conversationMaleImage != null)
        {
            conversationMaleImage.gameObject.SetActive(false);
        }

        if (conversationFemaleImage != null)
        {
            conversationFemaleImage.gameObject.SetActive(false);
        }
    }

    private void SetGameplayInputLock(bool locked)
    {
        isGameplayInputLocked = locked;
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

            if (!isAutoRunning || isStopped || currentAction == null || !currentArea.HasValue)
            {
                continue;
            }

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
            ConstantButtonData activeAction = currentAction;
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

[Serializable]
public class SingleUseButtonData
{
    public Button button;
    public ConversationSequence conversation;
    public bool hideAfterUse = true;
}

[Serializable]
public class AreaConversationData
{
    public string actionName;
    public TouchArea area;
    public ConversationSequence conversation;
}

[Serializable]
public class NextSceneData
{
    public string sceneName;
    public ConversationSequence conversation;
}

[Serializable]
public class ConversationSequence
{
    public List<ConversationTurn> turns = new List<ConversationTurn>();
}

[Serializable]
public class ConversationTurn
{
    public ConversationSpeaker speaker;
    public Sprite sprite;
    public AudioClip audioClip;
    public AudioMixerGroup audioMixerGroup;
    public float displayDuration = 1f;
    public float fadeInDuration = 0.15f;
    public float fadeOutDuration = 0.15f;
}

public enum ConversationSpeaker
{
    Male,
    Female
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
