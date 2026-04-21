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
    public Image manMouthImage;
    public Image manRightHandImage;
    public Image manLeftHandImage;
    public Image manCrotchImage;
    public Image subImage;


    [Header("Touch Area Input Handlers")]
    public TouchAreaInputHandler faceAreaInputHandler;
    public TouchAreaInputHandler rightBreastAreaInputHandler;
    public TouchAreaInputHandler leftBreastAreaInputHandler;
    public TouchAreaInputHandler crotchAreaInputHandler;

    [Header("Area Input Actions (4 Areas x Left/Right/Long)")]
    public List<AreaInputActionData> areaInputActions = new List<AreaInputActionData>();

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

    [Header("Conversation UI")]
    public Image conversationMaleImage;
    public Image conversationFemaleImage;

    [Header("Opening Conversation")]
    public ConversationSequence openingConversation;

    [Header("Area Conversations (one-time by action+area)")]
    public List<AreaConversationData> areaConversations = new List<AreaConversationData>();

    [Header("Single-use Conversations")]
    public List<SingleUseButtonData> singleUseButtons = new List<SingleUseButtonData>();

    [Header("Next Scene")]
    public Button nextSceneButton;
    public NextSceneData nextSceneData;

    [Header("Outfit Drag Unlocks")]
    public float undressDragThresholdPixels = 30f;
    public float outfitChangeDurationSeconds = 1f;
    public AudioClip outfitChangeClip;
    public AudioMixerGroup outfitChangeMixerGroup;

    [Header("Area Input Unlocks (by Area x Trigger)")]
    public List<AreaInputKey> initialUnlockedInputs = new List<AreaInputKey>();

    [Header("Realtime Speed Debug")]
    public float debugSpeedSliderValue;
    public float debugSpeedMultiplier = 1f;
    public float debugEffectiveAutoInterval;

    [Header("UI Roots (hidden during Opening/Next event conversations)")]
    public GameObject gameplayUiRoot;   // 行動ボタン、停止、背景切替、スライダー等をまとめた親
    public GameObject menuImagesRoot;   // メニュー画像群をまとめた親（最初は見せない）

    [Header("Event Conversation Advance (center click to advance)")]
    public Button eventConversationAdvanceButton; // 画面中央に置く透明ボタン推奨

    [Header("Fade")]
    public Image fadeOverlay;
    public float fadeOutSeconds = 0.5f;
    public float fadeInSeconds = 0.5f;

    private ConstantButtonData currentAction;
    private TouchArea? currentArea;
    private bool autoToggleOn;
    private bool isStopped;
    private int backgroundIndex;
    private Coroutine valueCoroutine;
    private Coroutine stopTransitionCoroutine;
    private Coroutine randomOnomatopoeiaCoroutine;
    private int stopTransitionToken;
    private readonly Dictionary<MenSlot, SlotRuntimeState> slotStates = new Dictionary<MenSlot, SlotRuntimeState>();

    private AudioSource sfxAudioSource;
    private AudioSource stoppedLoopAudioSource;

    private bool isGameplayInputLocked;
    private bool isGameplayConversationActive;
    private bool isOutfitChangePauseActive;

    private bool topDragUnlocked;
    private bool bottomDragUnlocked;

    private Coroutine gameplayConversationCoroutine;
    private int gameplayConversationToken;

    private Coroutine lockedConversationCoroutine;

    private Coroutine outfitChangePauseCoroutine;
    private int outfitChangePauseToken;
    private readonly Dictionary<MenSlot, AutoResumeState> outfitPauseResumeStates = new Dictionary<MenSlot, AutoResumeState>();

    private TouchArea? dragStartArea;
    private Vector2 dragStartPosition;
    private bool isDragTracking;

    private readonly HashSet<AreaConversationKey> playedAreaConversations = new HashSet<AreaConversationKey>();
    private readonly HashSet<SingleUseButtonData> usedSingleUseButtons = new HashSet<SingleUseButtonData>();

    private readonly HashSet<AreaInputKey> unlockedInputs = new HashSet<AreaInputKey>();

    private bool isEventConversationActive;
    private int eventAdvanceToken;



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

        if (speedSlider != null)
        {
            speedSlider.onValueChanged.AddListener(HandleSpeedSliderValueChanged);
        }

        if (nextSceneButton != null)
        {
            nextSceneButton.onClick.AddListener(HandleNextSceneButton);
        }

        if (eventConversationAdvanceButton != null)
        {
            eventConversationAdvanceButton.onClick.AddListener(RequestEventConversationAdvance);
            eventConversationAdvanceButton.gameObject.SetActive(false); // 最初は隠す
        }

        if (fadeOverlay != null)
        {
            // シーン開始は黒から始める（Startより前＝最初のフレームから黒）
            fadeOverlay.gameObject.SetActive(true);

            Color c = fadeOverlay.color;
            c.a = 1f;
            fadeOverlay.color = c;

            // クリックをブロックしたいならONのままでOK（FadeIn終了後にOFFにする）
            fadeOverlay.raycastTarget = true;
        }

        foreach (var buttonData in singleUseButtons)
        {
            if (buttonData?.button == null)
            {
                continue;
            }

            SingleUseButtonData capturedData = buttonData;
            capturedData.button.onClick.AddListener(() => HandleSingleUseButton(capturedData));
        }

        InitializeSlotStates();
        HideAllMenSlotImages();
        HideConversationImages();
        UpdateAutoToggleIndicator();
        ApplyBackground();
        // 起動直後はUIを隠しておく（Openingが無い場合は後で戻す）
        SetEventUiVisible(false);
        RefreshSpeedDebugInfo();


        unlockedInputs.Clear();
        if (initialUnlockedInputs != null)
        {
            foreach (var key in initialUnlockedInputs)
            {
                unlockedInputs.Add(key);
            }
        }

        UpdateInputInteractableState();
    }

    private void Start()
    {
        // 最初はUI非表示
        SetEventUiVisible(false);

        StartCoroutine(SceneStartRoutine());
    }

    private IEnumerator SceneStartRoutine()
    {
        if (fadeOverlay != null)
        {
            yield return FadeIn(fadeInSeconds);
        }

        // Openingがあるなら、Opening側でBeginEventConversation→EndEventConversationが走る
        TryStartOpeningConversation();

        // Openingが「無い」場合は、ここでUIを戻す
        if (openingConversation == null || !openingConversation.HasTurns)
        {
            SetEventUiVisible(true);
        }
    }

    private void RequestEventConversationAdvance()
    {
        if (!isEventConversationActive)
        {
            return;
        }

        eventAdvanceToken++;
    }

    private void BeginEventConversation()
    {
        isEventConversationActive = true;
        eventAdvanceToken = 0;

        SetEventUiVisible(false); // ボタン類・最初のメニューも全部隠す

        if (eventConversationAdvanceButton != null)
        {
            eventConversationAdvanceButton.gameObject.SetActive(true); // センタークリックだけON
        }
    }

    private void EndEventConversation()
    {
        isEventConversationActive = false;

        if (eventConversationAdvanceButton != null)
        {
            eventConversationAdvanceButton.gameObject.SetActive(false);
        }

        SetEventUiVisible(true); // イベント終了後にUIを戻す
    }

    private IEnumerator WaitTurnDurationOrEventAdvance(float seconds)
    {
        float endTime = Time.unscaledTime + Mathf.Max(0.01f, seconds);
        int startToken = eventAdvanceToken;

        while (Time.unscaledTime < endTime)
        {
            // イベント会話中だけ、クリックで待ちをスキップ
            if (isEventConversationActive && eventAdvanceToken != startToken)
            {
                yield break;
            }

            yield return null;
        }
    }



    private IEnumerator FadeOut(float seconds)
    {
        if (fadeOverlay == null)
        {
            yield break;
        }

        fadeOverlay.gameObject.SetActive(true);

        Color c = fadeOverlay.color;
        c.a = 0f;
        fadeOverlay.color = c;

        float t = 0f;
        seconds = Mathf.Max(0.01f, seconds);

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Clamp01(t / seconds);
            fadeOverlay.color = c;
            yield return null;
        }

        c.a = 1f;
        fadeOverlay.color = c;
    }

    private IEnumerator FadeIn(float seconds)
    {
        if (fadeOverlay == null)
        {
            yield break;
        }

        fadeOverlay.gameObject.SetActive(true);

        Color c = fadeOverlay.color;
        c.a = 1f;                // 最初は真っ黒（不透明）
        fadeOverlay.color = c;

        float t = 0f;
        seconds = Mathf.Max(0.01f, seconds);

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            c.a = 1f - Mathf.Clamp01(t / seconds);   // 1 -> 0
            fadeOverlay.color = c;
            yield return null;
        }

        c.a = 0f;
        fadeOverlay.color = c;
        fadeOverlay.raycastTarget = false;   // クリックブロック解除
        // 透明になったらオブジェクトごと無効化（Raycastも止めたい場合）
        fadeOverlay.gameObject.SetActive(false);
    }

    private IEnumerator FadeInSceneVisuals(float seconds)
    {
        seconds = Mathf.Max(0.01f, seconds);

        Image[] targets =
        {
        backgroundImage,
        bodyImage,
        faceImage,
        subImage,
        manMouthImage,
        manRightHandImage,
        manLeftHandImage,
        manCrotchImage
    };

        // まずα=0
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Color c = targets[i].color;
            c.a = 0f;
            targets[i].color = c;
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / seconds);

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                Color c = targets[i].color;
                c.a = a;
                targets[i].color = c;
            }

            yield return null;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Color c = targets[i].color;
            c.a = 1f;
            targets[i].color = c;
        }
    }
    private void SetEventUiVisible(bool visible)
    {
        if (gameplayUiRoot != null)
        {
            gameplayUiRoot.SetActive(visible);
        }

        if (menuImagesRoot != null)
        {
            menuImagesRoot.SetActive(visible);
        }
    }

    // Legacy helper keeps existing references working; left click behavior is used.
    public void HandleAreaClick(TouchArea area)
    {
        HandleAreaInput(area, AreaInputTrigger.LeftClick);
    }

    public void HandleAreaInput(TouchArea area, AreaInputTrigger trigger)
    {
        if (IsGameplayInputBlocked())
        {
            return;
        }

        ConstantButtonData action = ResolveAreaInputAction(area, trigger);
        if (action == null)
        {
            return;
        }

        if (isStopped)
        {
            ExitStoppedState();
        }

        currentAction = action;
        currentArea = area;

        AreaPlayMode? forcedMode = trigger == AreaInputTrigger.LongPress ? AreaPlayMode.AutoOnly : (AreaPlayMode?)null;
        StartOrUpdateSlot(action, area, forcedMode);
        ApplyPose();
        StartValueTickerIfNeeded();
        StartRandomOnomatopoeiaIfNeeded();
        TryPlayAreaConversation(action, area);
        RefreshSpeedDebugInfo();
    }

    public void HandleConstantActionClick(ConstantButtonData action)
    {
        if (IsGameplayInputBlocked())
        {
            return;
        }

        if (action == null)
        {
            return;
        }

        bool actionChanged = currentAction != action;
        currentAction = action;
        isStopped = false;

        StopStoppedTransition();
        StopStoppedLoopAudio();

        PlayOneShot(action.startClip, action.audioMixerGroup);

        if (currentArea.HasValue)
        {
            StartOrUpdateSlot(action, currentArea.Value);
            ApplyPose();
        }

        if (actionChanged)
        {
            StartValueTickerIfNeeded();
            StartRandomOnomatopoeiaIfNeeded();
            RefreshSpeedDebugInfo();
        }
    }

    public void HandleAutoToggleButton()
    {
        if (IsGameplayInputBlocked())
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
        if (isGameplayInputLocked)
        {
            return;
        }

        StopGameplayConversation();
        CancelOutfitChangePause(true);
        EnterStoppedState();
    }

    public void HandleBackgroundChangeButton()
    {
        if (IsGameplayInputBlocked())
        {
            return;
        }

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
        if (IsGameplayInputBlocked())
        {
            return;
        }

        if (lockedConversationCoroutine != null)
        {
            return;
        }

        lockedConversationCoroutine = StartCoroutine(NextSceneFlowCoroutine());
    }

    public void HandleSingleUseButton(SingleUseButtonData buttonData)
    {
        if (IsGameplayInputBlocked())
        {
            return;
        }

        if (buttonData == null || buttonData.button == null || usedSingleUseButtons.Contains(buttonData))
        {
            return;
        }

        if (isStopped)
        {
            ExitStoppedState();
        }

        StartGameplayConversation(buttonData.conversation, () =>
        {
            usedSingleUseButtons.Add(buttonData);

            if (buttonData.disableAfterUse && buttonData.button != null)
            {
                buttonData.button.interactable = false;
                if (buttonData.hideAfterUse)
                {
                    buttonData.button.gameObject.SetActive(false);
                }
            }

            if (buttonData.unlockOnComplete)
            {
                ApplyUnlock(buttonData.unlockType);
            }

            // === ここから追記：行動解禁 ===
            if (buttonData.unlockInputsOnComplete != null)
            {
                foreach (var key in buttonData.unlockInputsOnComplete)
                {
                    unlockedInputs.Add(key);
                }
            }

            // === ここから追記：メニュー画像表示（出しっぱなし）===
            if (buttonData.menuImagesToShowOnComplete != null)
            {
                foreach (var img in buttonData.menuImagesToShowOnComplete)
                {
                    if (img != null)
                    {
                        img.gameObject.SetActive(true);
                    }
                }
            }
        });
    }

    public void NotifyPointerDown(TouchArea area, Vector2 screenPosition)
    {
        if (IsGameplayInputBlocked())
        {
            return;
        }

        dragStartArea = area;
        dragStartPosition = screenPosition;
        isDragTracking = true;
    }

    public void NotifyPointerDrag(Vector2 screenPosition)
    {
        if (!isDragTracking || !dragStartArea.HasValue || IsGameplayInputBlocked())
        {
            return;
        }

        TouchArea area = dragStartArea.Value;
        if (!CanUndressFromArea(area))
        {
            return;
        }

        float dragDistance = Vector2.Distance(dragStartPosition, screenPosition);
        if (dragDistance < undressDragThresholdPixels)
        {
            return;
        }

        TryUndress(area);
        dragStartArea = null;
        isDragTracking = false;
    }

    public void NotifyPointerUp()
    {
        dragStartArea = null;
        isDragTracking = false;
    }

    // Optional hook for dialogue/event systems.
    public void SetBackground(Sprite sprite)
    {
        if (backgroundImage == null || sprite == null)
        {
            return;
        }

        backgroundImage.sprite = sprite;
    }

    private void ApplyTurnSceneOverrides(ConversationTurn turn)
    {
        if (turn == null)
        {
            return;
        }

        // 背景
        if (turn.backgroundOverride != null && backgroundImage != null)
        {
            backgroundImage.sprite = turn.backgroundOverride;
        }

        // 女側（Body/Face/Sub）
        if (turn.bodyOverride != null && bodyImage != null)
        {
            bodyImage.sprite = turn.bodyOverride;
            bodyImage.gameObject.SetActive(true);
        }

        if (turn.faceOverride != null && faceImage != null)
        {
            faceImage.sprite = turn.faceOverride;
            faceImage.gameObject.SetActive(true);
        }

        if (turn.subOverride != null && subImage != null)
        {
            subImage.sprite = turn.subOverride;
            subImage.gameObject.SetActive(true);
        }

        // 男側（4スロット）
        if (turn.manMouthOverride != null)
        {
            SetSlotImageSprite(MenSlot.Mouth, turn.manMouthOverride);
        }

        if (turn.manRightHandOverride != null)
        {
            SetSlotImageSprite(MenSlot.RightHand, turn.manRightHandOverride);
        }

        if (turn.manLeftHandOverride != null)
        {
            SetSlotImageSprite(MenSlot.LeftHand, turn.manLeftHandOverride);
        }

        if (turn.manCrotchOverride != null)
        {
            SetSlotImageSprite(MenSlot.Crotch, turn.manCrotchOverride);
        }
    }

    public bool TryUndressFromDrag(TouchArea area, Vector2 pressStartPosition, Vector2 currentPosition)
    {
        if (IsGameplayInputBlocked())
        {
            return false;
        }

        if (!isDragTracking || !dragStartArea.HasValue)
        {
            return false;
        }

        if (!CanUndressFromArea(area))
        {
            return false;
        }

        float dragDistance = Vector2.Distance(pressStartPosition, currentPosition);
        if (dragDistance < undressDragThresholdPixels)
        {
            return false;
        }

        bool changed = false;

        // 上：胸エリア
        if (topDragUnlocked && (area == TouchArea.LeftBreast || area == TouchArea.RightBreast))
        {
            changed = TryAdvanceTopOutfit();
        }
        // 下：股エリア
        else if (bottomDragUnlocked && area == TouchArea.Crotch)
        {
            changed = TryAdvanceBottomOutfit();
        }

        if (!changed)
        {
            return false;
        }

        // 脱衣成功時の既存演出
        ApplyPose();
        PlayOneShot(outfitChangeClip, outfitChangeMixerGroup);
        BeginOutfitChangePause();

        // ドラッグ状態も終了扱いにしておく（連続発火防止）
        dragStartArea = null;
        isDragTracking = false;

        return true;
    }

    private bool IsGameplayInputBlocked()
    {
        return isGameplayInputLocked || isGameplayConversationActive || isOutfitChangePauseActive;
    }

    private void TryStartOpeningConversation()
    {
        if (openingConversation == null || !openingConversation.HasTurns)
        {
            return;
        }

        if (lockedConversationCoroutine != null)
        {
            StopCoroutine(lockedConversationCoroutine);
        }

        lockedConversationCoroutine = StartCoroutine(OpeningConversationCoroutine());
    }

    private IEnumerator OpeningConversationCoroutine()
    {
        BeginEventConversation();
        SetGameplayInputLock(true);

        yield return PlayConversationSequence(openingConversation);

        SetGameplayInputLock(false);
        EndEventConversation();

        lockedConversationCoroutine = null;
    }

    private IEnumerator NextSceneFlowCoroutine()
    {
        // ここに来た時点で「次へ」が押されているので、まずUIと操作を止める
        BeginEventConversation();
        SetGameplayInputLock(true);

        // Nextイベント会話（あれば再生 / センタークリックで早送り可）
        if (nextSceneData != null && nextSceneData.conversation != null && nextSceneData.conversation.HasTurns)
        {
            yield return PlayConversationSequence(nextSceneData.conversation);
        }

        // 会話が終わったらUIは戻さず、そのまま暗転して遷移
        string sceneName = nextSceneData != null ? nextSceneData.sceneName : null;
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            yield return FadeOut(fadeOutSeconds);
            SceneManager.LoadScene(sceneName);
            yield break;
        }

        // sceneName が未設定なら「遷移できない」ので、仕方なくUIを戻す
        SetGameplayInputLock(false);
        EndEventConversation();
        lockedConversationCoroutine = null;
    }



    private void TryPlayAreaConversation(ConstantButtonData action, TouchArea area)
    {
        if (action == null || areaConversations == null || areaConversations.Count == 0)
        {
            return;
        }

        AreaConversationData data = null;
        for (int i = 0; i < areaConversations.Count; i++)
        {
            AreaConversationData candidate = areaConversations[i];
            if (candidate == null)
            {
                continue;
            }

            if (candidate.action == action && candidate.area == area)
            {
                data = candidate;
                break;
            }
        }

        if (data == null || data.conversation == null || !data.conversation.HasTurns)
        {
            return;
        }

        AreaConversationKey key = new AreaConversationKey(action, area);
        if (!playedAreaConversations.Add(key))
        {
            return;
        }

        StartGameplayConversation(data.conversation, null);
    }

    private void StartGameplayConversation(ConversationSequence conversation, Action onComplete)
    {
        StopGameplayConversation();

        if (conversation == null || !conversation.HasTurns)
        {
            onComplete?.Invoke();
            return;
        }

        isGameplayConversationActive = true;
        UpdateInputInteractableState();

        gameplayConversationToken++;
        int token = gameplayConversationToken;
        gameplayConversationCoroutine = StartCoroutine(GameplayConversationCoroutine(conversation, onComplete, token));
    }

    private IEnumerator GameplayConversationCoroutine(ConversationSequence conversation, Action onComplete, int token)
    {
        yield return PlayConversationSequence(conversation, () => token != gameplayConversationToken);

        if (token != gameplayConversationToken)
        {
            yield break;
        }

        gameplayConversationCoroutine = null;
        isGameplayConversationActive = false;
        UpdateInputInteractableState();
        onComplete?.Invoke();
    }

    private void StopGameplayConversation()
    {
        if (!isGameplayConversationActive && gameplayConversationCoroutine == null)
        {
            HideConversationImages();
            return;
        }

        gameplayConversationToken++;

        if (gameplayConversationCoroutine != null)
        {
            StopCoroutine(gameplayConversationCoroutine);
            gameplayConversationCoroutine = null;
        }

        isGameplayConversationActive = false;
        HideConversationImages();
        UpdateInputInteractableState();
    }

    private IEnumerator PlayConversationSequence(ConversationSequence sequence, Func<bool> shouldCancel = null)
    {
        HideConversationImages();

        if (sequence == null || sequence.turns == null)
        {
            yield break;
        }

        for (int i = 0; i < sequence.turns.Count; i++)
        {
            if (shouldCancel != null && shouldCancel())
            {
                HideConversationImages();
                yield break;
            }

            ConversationTurn turn = sequence.turns[i];
            if (turn == null)
            {
                continue;
            }

            // ★イベント会話の最初だけ：下地→ターン0適用→見た目レイヤーだけフェードイン
            if (isEventConversationActive && i == 0)
            {
                // 1) 下地（任意）
                if (sequence.baseVisualTurn != null)
                {
                    ApplyTurnSceneOverrides(sequence.baseVisualTurn);
                }

                // 2) ターン0の見た目を適用
                ApplyTurnSceneOverrides(turn);

                // 3) 見た目レイヤーだけフェードイン（会話立ち絵はフェードさせない）
                if (sequence.baseToFirstTurnFadeSeconds > 0f)
                {
                    yield return FadeInSceneVisuals(sequence.baseToFirstTurnFadeSeconds);
                }
            }
            else
            {
                // 通常のイベントターン：そのまま上書き
                if (isEventConversationActive)
                {
                    ApplyTurnSceneOverrides(turn);
                }
            }

            ShowConversationTurn(turn);
            PlayOneShot(turn.audioClip, turn.audioMixerGroup);


            float duration = Mathf.Max(0.01f, turn.durationSeconds);

            // ★関数名を「定義してある方」に合わせる
            yield return WaitTurnDurationOrEventAdvance(duration);

        }

        HideConversationImages();
    }

    private void ShowConversationTurn(ConversationTurn turn)
    {
        bool useOverrides = isEventConversationActive &&
                            (turn.maleConversationSprite != null || turn.femaleConversationSprite != null);

        Sprite maleSprite = null;
        Sprite femaleSprite = null;

        if (useOverrides)
        {
            maleSprite = turn.maleConversationSprite;
            femaleSprite = turn.femaleConversationSprite;
        }
        else
        {
            // 従来互換：speaker + sprite でどちらか片側に出す
            bool showMale = turn.speaker == ConversationSpeaker.Male;
            maleSprite = showMale ? turn.sprite : null;
            femaleSprite = showMale ? null : turn.sprite;
        }

        if (conversationMaleImage != null)
        {
            conversationMaleImage.sprite = maleSprite;
            conversationMaleImage.gameObject.SetActive(maleSprite != null);
        }

        if (conversationFemaleImage != null)
        {
            conversationFemaleImage.sprite = femaleSprite;
            conversationFemaleImage.gameObject.SetActive(femaleSprite != null);
        }
    }

    private void HideConversationImages()
    {
        if (conversationMaleImage != null)
        {
            conversationMaleImage.sprite = null;
            conversationMaleImage.gameObject.SetActive(false);
        }

        if (conversationFemaleImage != null)
        {
            conversationFemaleImage.sprite = null;
            conversationFemaleImage.gameObject.SetActive(false);
        }
    }

    private void ApplyUnlock(UnlockType unlockType)
    {
        switch (unlockType)
        {
            case UnlockType.TopDrag:
                topDragUnlocked = true;
                break;
            case UnlockType.BottomDrag:
                bottomDragUnlocked = true;
                break;
            case UnlockType.Both:
                topDragUnlocked = true;
                bottomDragUnlocked = true;
                break;
        }
    }

    private bool CanUndressFromArea(TouchArea area)
    {
        if (topDragUnlocked && (area == TouchArea.LeftBreast || area == TouchArea.RightBreast))
        {
            return true;
        }

        if (bottomDragUnlocked && area == TouchArea.Crotch)
        {
            return true;
        }

        return false;
    }

    private void TryUndress(TouchArea area)
    {
        bool changed = false;

        if (topDragUnlocked && (area == TouchArea.LeftBreast || area == TouchArea.RightBreast))
        {
            changed = TryAdvanceTopOutfit();
        }
        else if (bottomDragUnlocked && area == TouchArea.Crotch)
        {
            changed = TryAdvanceBottomOutfit();
        }

        if (!changed)
        {
            return;
        }

        ApplyPose();
        PlayOneShot(outfitChangeClip, outfitChangeMixerGroup);
        BeginOutfitChangePause();
    }

    private bool TryAdvanceTopOutfit()
    {
        switch (currentTopOutfit)
        {
            case TopOutfit.Sweater:
                currentTopOutfit = TopOutfit.Bra;
                return true;
            case TopOutfit.Bra:
                currentTopOutfit = TopOutfit.Topless;
                return true;
            default:
                return false;
        }
    }

    private bool TryAdvanceBottomOutfit()
    {
        switch (currentBottomOutfit)
        {
            case BottomOutfit.Skirt:
                currentBottomOutfit = BottomOutfit.Panties;
                return true;
            case BottomOutfit.Panties:
                currentBottomOutfit = BottomOutfit.Nude;
                return true;
            default:
                return false;
        }
    }

    private void BeginOutfitChangePause()
    {
        CancelOutfitChangePause(true);

        outfitPauseResumeStates.Clear();
        foreach (var pair in slotStates)
        {
            SlotRuntimeState state = pair.Value;
            outfitPauseResumeStates[pair.Key] = new AutoResumeState
            {
                shouldResume = state.isAutoRunning,
                forced = state.isForcedAuto
            };

            if (state.isAutoRunning)
            {
                StopSlotAuto(pair.Key, false);
            }
        }

        HideAllMenSlotImages();

        isOutfitChangePauseActive = true;
        UpdateInputInteractableState();

        outfitChangePauseToken++;
        int token = outfitChangePauseToken;
        outfitChangePauseCoroutine = StartCoroutine(OutfitChangePauseCoroutine(token));
    }

    private IEnumerator OutfitChangePauseCoroutine(int token)
    {
        float wait = Mathf.Max(0f, outfitChangeDurationSeconds);
        if (wait > 0f)
        {
            yield return new WaitForSeconds(wait);
        }

        if (token != outfitChangePauseToken)
        {
            yield break;
        }

        outfitChangePauseCoroutine = null;
        isOutfitChangePauseActive = false;

        foreach (var pair in outfitPauseResumeStates)
        {
            if (!pair.Value.shouldResume)
            {
                continue;
            }

            MenSlot slot = pair.Key;
            SlotRuntimeState state = slotStates[slot];
            if (!state.isActive || state.action == null || !state.area.HasValue)
            {
                continue;
            }

            StartSlotAuto(slot, state.action, pair.Value.forced);
        }

        outfitPauseResumeStates.Clear();
        ApplyPose();
        UpdateInputInteractableState();
    }

    private void CancelOutfitChangePause(bool preventResume)
    {
        if (preventResume)
        {
            outfitChangePauseToken++;
        }

        if (outfitChangePauseCoroutine != null)
        {
            StopCoroutine(outfitChangePauseCoroutine);
            outfitChangePauseCoroutine = null;
        }

        outfitPauseResumeStates.Clear();
        isOutfitChangePauseActive = false;
        UpdateInputInteractableState();
    }

    private void EnterStoppedState()
    {
        isStopped = true;
        NotifyPointerUp();
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

        if (!isOutfitChangePauseActive)
        {
            ApplyAllSlotMenImages();
        }
    }

    private void ApplyBackground()
    {
        if (backgroundImage == null || backgrounds == null || backgrounds.Count == 0)
        {
            return;
        }

        backgroundIndex = Mathf.Clamp(backgroundIndex, 0, backgrounds.Count - 1);

        Sprite candidate = backgrounds[backgroundIndex];
        if (candidate == null)
        {
            Debug.LogWarning($"Background sprite is null at index {backgroundIndex}. Skipping.");
            return;
        }

        backgroundImage.sprite = candidate;
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

    private void InitializeSlotStates()
    {
        slotStates.Clear();
        foreach (MenSlot slot in Enum.GetValues(typeof(MenSlot)))
        {
            slotStates[slot] = new SlotRuntimeState();
        }
    }

    private ConstantButtonData ResolveAreaInputAction(TouchArea area, AreaInputTrigger trigger)
    {
        if (areaInputActions == null)
        {
            return null;
        }

        for (int i = 0; i < areaInputActions.Count; i++)
        {
            AreaInputActionData candidate = areaInputActions[i];
            if (candidate == null)
            {
                continue;
            }

            if (candidate.area == area && candidate.trigger == trigger)
            {
                if (candidate.requiresUnlock)
                {
                    AreaInputKey key = candidate.requiredUnlock;
                    if (!unlockedInputs.Contains(key))
                    {
                        return null; // 静かに無視
                    }
                }

                return candidate.action;
            }
        }

        return null;
    }

    private void StartOrUpdateSlot(ConstantButtonData action, TouchArea area, AreaPlayMode? forcedMode = null)
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
        AreaPlayMode mode = forcedMode ?? GetAreaPlayMode(action, area);
        bool areaChanged = !state.area.HasValue || state.area.Value != area;
        bool actionChanged = state.action != action;

        if (actionChanged || areaChanged || !state.isActive)
        {
            StartSlot(targetSlot, action, area, mode);
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

    private void StartSlot(MenSlot slot, ConstantButtonData action, TouchArea area, AreaPlayMode mode)
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

        if (mode == AreaPlayMode.AutoOnly)
        {
            StartSlotAuto(slot, action, true);
        }
        else if (mode == AreaPlayMode.Both && autoToggleOn)
        {
            StartSlotAuto(slot, action, false);
        }
    }

    private void StartSlotAuto(MenSlot slot, ConstantButtonData action, bool forced)
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

    private void AdvanceSlotFrame(MenSlot slot, ConstantButtonData action)
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

    private float GetAutoInterval(ConstantButtonData action)
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

    private PoseSet FindPose(ConstantButtonData action, TouchArea area)
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

    private AreaPlayMode GetAreaPlayMode(ConstantButtonData action, TouchArea area)
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

    private void HandleSpeedSliderValueChanged(float _)
    {
        if (isGameplayInputLocked)
        {
            if (speedSlider != null)
            {
                speedSlider.SetValueWithoutNotify(debugSpeedSliderValue);
            }

            return;
        }

        RefreshSpeedDebugInfo();
    }

    private void RefreshSpeedDebugInfo()
    {
        float sliderValue = speedSlider != null ? speedSlider.value : 1f;
        debugSpeedSliderValue = sliderValue;
        debugSpeedMultiplier = Mathf.Max(0.01f, sliderValue);

        float autoBaseInterval = fallbackAutoBaseInterval;
        if (currentAction != null && currentAction.autoBaseInterval > 0f)
        {
            autoBaseInterval = currentAction.autoBaseInterval;
        }

        debugEffectiveAutoInterval = autoBaseInterval / debugSpeedMultiplier;
    }

    private void SetGameplayInputLock(bool isLocked)
    {
        isGameplayInputLocked = isLocked;
        UpdateInputInteractableState();
    }



    private void UpdateInputInteractableState()
    {
        bool gameplayEnabled = !IsGameplayInputBlocked();

        SetAreaInputEnabled(faceAreaInputHandler, gameplayEnabled);
        SetAreaInputEnabled(rightBreastAreaInputHandler, gameplayEnabled);
        SetAreaInputEnabled(leftBreastAreaInputHandler, gameplayEnabled);
        SetAreaInputEnabled(crotchAreaInputHandler, gameplayEnabled);

        if (autoToggleButton != null)
        {
            autoToggleButton.interactable = gameplayEnabled;
        }

        if (backgroundChangeButton != null)
        {
            backgroundChangeButton.interactable = gameplayEnabled;
        }

        if (speedSlider != null)
        {
            speedSlider.interactable = gameplayEnabled;
        }

        if (nextSceneButton != null)
        {
            nextSceneButton.interactable = gameplayEnabled;
        }

        foreach (var singleUse in singleUseButtons)
        {
            if (singleUse?.button == null)
            {
                continue;
            }

            bool isUsed = usedSingleUseButtons.Contains(singleUse);
            singleUse.button.interactable = gameplayEnabled && !isUsed;
        }

        if (stopActionButton != null)
        {
            stopActionButton.interactable = !isGameplayInputLocked;
        }
    }

    private static void SetAreaInputEnabled(TouchAreaInputHandler handler, bool enabled)
    {
        if (handler != null)
        {
            handler.SetInputEnabled(enabled);
        }
    }

    private class SlotRuntimeState
    {
        public ConstantButtonData action;
        public TouchArea? area;
        public int frameIndex;
        public bool isActive;
        public bool isAutoRunning;
        public bool isForcedAuto;
        public Coroutine autoCoroutine;
    }

    private struct AutoResumeState
    {
        public bool shouldResume;
        public bool forced;
    }

    private readonly struct AreaConversationKey : IEquatable<AreaConversationKey>
    {
        public readonly ConstantButtonData action;
        public readonly TouchArea area;

        public AreaConversationKey(ConstantButtonData action, TouchArea area)
        {
            this.action = action;
            this.area = area;
        }

        public bool Equals(AreaConversationKey other)
        {
            return action == other.action && area == other.area;
        }

        public override bool Equals(object obj)
        {
            return obj is AreaConversationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (action != null ? action.GetHashCode() : 0);
                hash = (hash * 31) + (int)area;
                return hash;
            }
        }
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

    private static AreaInputKey ToInputKey(TouchArea area, AreaInputTrigger trigger)
    {
        switch (area)
        {
            case TouchArea.Face:
                return trigger == AreaInputTrigger.RightClick ? AreaInputKey.Face_Right
                     : trigger == AreaInputTrigger.LongPress ? AreaInputKey.Face_Long
                     : AreaInputKey.Face_Left;

            case TouchArea.RightBreast:
                return trigger == AreaInputTrigger.RightClick ? AreaInputKey.RightBreast_Right
                     : trigger == AreaInputTrigger.LongPress ? AreaInputKey.RightBreast_Long
                     : AreaInputKey.RightBreast_Left;

            case TouchArea.LeftBreast:
                return trigger == AreaInputTrigger.RightClick ? AreaInputKey.LeftBreast_Right
                     : trigger == AreaInputTrigger.LongPress ? AreaInputKey.LeftBreast_Long
                     : AreaInputKey.LeftBreast_Left;

            case TouchArea.Crotch:
            default:
                return trigger == AreaInputTrigger.RightClick ? AreaInputKey.Crotch_Right
                     : trigger == AreaInputTrigger.LongPress ? AreaInputKey.Crotch_Long
                     : AreaInputKey.Crotch_Left;
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

public enum MenSlot
{
    Mouth,
    RightHand,
    LeftHand,
    Crotch
}

public enum AreaInputTrigger
{
    LeftClick,
    RightClick,
    LongPress
}

public enum ConversationSpeaker
{
    Male,
    Female
}

public enum UnlockType
{
    None,
    TopDrag,
    BottomDrag,
    Both
}

public enum AreaInputKey
{
    Face_Left,
    Face_Right,
    Face_Long,

    RightBreast_Left,
    RightBreast_Right,
    RightBreast_Long,

    LeftBreast_Left,
    LeftBreast_Right,
    LeftBreast_Long,

    Crotch_Left,
    Crotch_Right,
    Crotch_Long
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
public class ConstantButtonData
{
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

[Serializable]
public class AreaInputActionData
{
    public TouchArea area;
    public AreaInputTrigger trigger = AreaInputTrigger.LeftClick;
    public ConstantButtonData action;

    [Header("Unlock")]
    public bool requiresUnlock;
    public AreaInputKey requiredUnlock;
}

[Serializable]
public class ConversationTurn
{
    public ConversationSpeaker speaker;
    public Sprite sprite;
    public float durationSeconds = 1f;
    public AudioClip audioClip;
    public AudioMixerGroup audioMixerGroup;

    [Header("Conversation UI Overrides (optional)")]
    public Sprite maleConversationSprite;
    public Sprite femaleConversationSprite;

    // 既存
    public Sprite backgroundOverride;

    [Header("Scene Visual Overrides (optional)")]
    public Sprite bodyOverride;
    public Sprite faceOverride;
    public Sprite manMouthOverride;
    public Sprite manRightHandOverride;
    public Sprite manLeftHandOverride;
    public Sprite manCrotchOverride;
    public Sprite subOverride;


}

[Serializable]
public class ConversationSequence
{
    public List<ConversationTurn> turns = new List<ConversationTurn>();

    // ★追加
    public ConversationTurn baseVisualTurn;
    public float baseToFirstTurnFadeSeconds = 0.25f;

    public bool HasTurns
    {
        get { return turns != null && turns.Count > 0; }
    }
}

[Serializable]
public class AreaConversationData
{
    public ConstantButtonData action;
    public TouchArea area;
    public ConversationSequence conversation;
}

[Serializable]
public class SingleUseButtonData
{
    public Button button;
    public ConversationSequence conversation;
    public bool unlockOnComplete;
    public UnlockType unlockType = UnlockType.None;
    public bool disableAfterUse = true;
    public bool hideAfterUse = true;
    public List<AreaInputKey> unlockInputsOnComplete = new List<AreaInputKey>();

    public List<Image> menuImagesToShowOnComplete = new List<Image>();
}

[Serializable]
public class NextSceneData
{
    public string sceneName;
    public ConversationSequence conversation;
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
