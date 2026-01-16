using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

// Modeの型を定義→「選ぶのを文字列ではなく３つの型に限定」
public enum PlacementMode { GroundFix, ScreenFix, Follow }

// クラスの宣言(MonoBehavior: Unityの部品) → MonoBehaviorを継承すると、Awake()、Start()、Update()などの"イベント関数が呼ばれるようになる"
public class CaptureControlsController : MonoBehaviour
{
    // Inspectorで差し込みたい参照(privateだけど、Inspectorに露出させる)
    [SerializeField] private UIDocument doc;
    [SerializeField] private CaptureGuideController captureGuide; // Layer3参照（Inspectorで入れる）

    // state(このクラスが持つ現在の状態：UIは状態から動作をコードで作っていく)
    [SerializeField] private PlacementMode placementMode = PlacementMode.GroundFix;
    [SerializeField] private CaptureAspect aspect = CaptureAspect.FourThree;
    [SerializeField] private bool flashOn = false;
    [SerializeField] private bool flatVisible = true;

    // UIの要素(VisualElement)を"掴む"ための変数 uxmlのnameと対応する
    VisualElement root;
    VisualElement flashBtn;
    VisualElement flatBtn;
    VisualElement aspectBtn;
    VisualElement captureModeBtn;
    VisualElement captureModeIcon;
    // Label内のTextを変更するために label.text(Labelが持っているtextを使う)を利用できるので、Labelとして型を定義
    private Label captureModeLabel;
    VisualElement avatarSlotBar;
    VisualElement avatarSlotRow;
    const int MaxSlots = 7;
    // 最初のSlot と 2つ目以降のSlot
    VisualElement initialAvatarSlotBtn;
    VisualElement slotAddBtn;
    // Slot Delete Overlay
    VisualElement slotDeleteOverlay;
    VisualElement slotDeleteBtn;
    VisualElement pendingDeleteSlot;
    Coroutine longPressRoutine;

    // ------- Lighitng PanelのUI要素 -------
    VisualElement lightingBtn;
    VisualElement lightingPanel;
    VisualElement lightingCloseBtn;
    VisualElement tabMood;
    VisualElement tabDirection;
    VisualElement lightingPanelMood;
    VisualElement lightingPanelDirection;

    // Auto Sync
    VisualElement autoSyncBtn;
    VisualElement autoSyncToggle;
    Label autoSyncStatusLabel;
    bool autoSyncOn = true;
    
    // Preset selections
    ScrollView presetSelections;
    VisualElement presetBtnAuto, presetBtnSunny, presetBtnCloudy, presetBtnIndoor, presetBtnWarm, presetBtnSunset;
    
    // Sliders
    VisualElement colorTemperatureSlider;
    VisualElement colorTemperatureThumbBtn;
    Label colorTemperatureValueLabel;
    VisualElement brightnessSlider;
    VisualElement brightnessThumbBtn;
    Label brightnessValueLabel;

    // Slider values
    float colorTempK = 5500f;   // 2000-10000
    float brightness = 1.0f;    // 0.1-2.0
    bool draggingTemp = false;
    bool draggingBright = false;
    bool slidersInitialized = false;

    // ---- Direction tab ----
    VisualElement directionPad;
    VisualElement dirKnob;

    VisualElement elevSlider;
    VisualElement elevTrack;
    VisualElement elevKnob;
    Label elevationValueLabel;

    bool draggingDir = false;
    bool draggingElev = false;
    bool directionInitialized = false;

    // values
    float dirX01 = 0.5f;     // 0..1 (中心=0.5)
    float dirY01 = 0.5f;     // 0..1
    float elevation01 = 0.5f; // 0..1

    // ------- Shadow PanelのUI要素 -------
    VisualElement shadowBtn;
    VisualElement shadowPanel;
    VisualElement shadowCloseBtn;

    // Enable Shadow (UI only)
    VisualElement enableShadowBtn;
    VisualElement enableShadowToggle;
    Label enableShadowStatusLabel;
    bool shadowEnabled = true;

    // Shadow intensity slider
    VisualElement shadowIntensitySlider;
    VisualElement shadowIntensityThumbBtn;
    Label shadowIntensityValueLabel;
    float shadowIntensity = 0.5f;
    bool draggingShadowIntensity = false;
    bool shadowSliderInitialized = false;

    // Softness selections (UI only)
    VisualElement shadowSoftnessSelections;
    VisualElement softnessSoft;
    VisualElement softnessMedium;
    VisualElement softnessHard;

    // UXLMから要素を検索して、変数に入れる（１回だけ）※名前一致がとても大事
    void Awake()
    {
        if (!doc) doc = GetComponent<UIDocument>();
        root = doc.rootVisualElement;

        flashBtn = root.Q<VisualElement>("flashBtn");
        flatBtn = root.Q<VisualElement>("flatVisualBtn");
        aspectBtn = root.Q<VisualElement>("aspectBtn");

        captureModeBtn = root.Q<VisualElement>("captureModeBtn");
        captureModeIcon = root.Q<VisualElement>("captureModeIcon");
        captureModeLabel = root.Q<Label>("captureModeLabel");

        avatarSlotBar = root.Q<VisualElement>("avatarSlotBar");
        avatarSlotRow = root.Q<VisualElement>("avatarSlotRow");
        initialAvatarSlotBtn = root.Q<VisualElement>("avatarSlotBtn");
        slotAddBtn = root.Q<VisualElement>("slotAddBtn");

        slotDeleteOverlay = root.Q<VisualElement>("slotDeleteOverlay");
        slotDeleteBtn = root.Q<VisualElement>("slotDeleteBtn");
        // overlayは入力を取る必要がある
        slotDeleteOverlay.pickingMode = PickingMode.Position;
        slotDeleteBtn.pickingMode = PickingMode.Position;

        // ---- Lighting Panel ----
        lightingBtn = root.Q<VisualElement>("lightingBtn");
        lightingPanel = root.Q<VisualElement>("lightingPanel");
        lightingCloseBtn = root.Q<VisualElement>("lightingCloseBtn");

        tabMood = root.Q<VisualElement>("tabMood");
        tabDirection = root.Q<VisualElement>("tabDirection");

        lightingPanelMood = root.Q<VisualElement>("lightingPanelMood");
        lightingPanelDirection = root.Q<VisualElement>("lightingPanelDirection");

        // auto sync
        autoSyncBtn = root.Q<VisualElement>("autoSyncBtn");
        autoSyncToggle = root.Q<VisualElement>("autoSyncToggle");
        autoSyncStatusLabel = root.Q<Label>("autoSyncStatus");

        //preset
        presetSelections = root.Q<ScrollView>("presetSelections");

        presetBtnAuto = root.Q<VisualElement>("presetBtnAuto");
        presetBtnSunny = root.Q<VisualElement>("presetBtnSunny");
        presetBtnCloudy = root.Q<VisualElement>("presetBtnCloudy");
        presetBtnIndoor = root.Q<VisualElement>("presetBtnIndoor");
        presetBtnWarm = root.Q<VisualElement>("presetBtnWarm");
        presetBtnSunset = root.Q<VisualElement>("presetBtnSunset");

        // Lighting sliders
        colorTemperatureSlider = root.Q<VisualElement>("colorTemperatureSlider");
        colorTemperatureThumbBtn = root.Q<VisualElement>("colorTemperatureThumbBtn");
        colorTemperatureValueLabel = root.Q<Label>("colorTemperatureValue");

        brightnessSlider = root.Q<VisualElement>("brightnessSlider");
        brightnessThumbBtn = root.Q<VisualElement>("brightnessThumbBtn");
        brightnessValueLabel = root.Q<Label>("brightnessValue");

        // Direction tab
        directionPad = root.Q<VisualElement>("directionPad");
        dirKnob = root.Q<VisualElement>("dirKnob");

        elevSlider = root.Q<VisualElement>("elevSlider");
        elevTrack = root.Q<VisualElement>("elevTrack");
        elevKnob = root.Q<VisualElement>("elevKnob");
        elevationValueLabel = root.Q<Label>("elevationValue");

        // ---- Shadow Panel ----
        shadowBtn = root.Q<VisualElement>("shadowBtn");
        shadowPanel = root.Q<VisualElement>("shadowPanel");
        shadowCloseBtn = root.Q<VisualElement>("shadowCloseBtn");

        // Enable Shadow
        enableShadowBtn = root.Q<VisualElement>("enableShadowBtn");
        enableShadowToggle = root.Q<VisualElement>("enableShadowToggle");
        enableShadowStatusLabel = root.Q<Label>("enableShadowStatus");

        // Intensity
        shadowIntensitySlider = root.Q<VisualElement>("shadowIntensitySlider");
        shadowIntensityThumbBtn = root.Q<VisualElement>("shadowIntensityThumbBtn");
        shadowIntensityValueLabel = root.Q<Label>("shadowIntensityValue");

        // Softness
        shadowSoftnessSelections = root.Q<VisualElement>("shadowSoftnessSelections");
        softnessSoft = root.Q<VisualElement>("softnessSoft");
        softnessMedium = root.Q<VisualElement>("softnessMedium");
        softnessHard = root.Q<VisualElement>("softnessHard");
    }

    // 最初に画面が呼ばれる時の実行内容
    void Start()
    {
        // クリックイベントを繋ぐ（VisualElementでもClickEvent取れる）
        flashBtn?.RegisterCallback<ClickEvent>(_ => ToggleFlash());
        flatBtn?.RegisterCallback<ClickEvent>(_ => ToggleFlat());
        aspectBtn?.RegisterCallback<ClickEvent>(_ => CycleAspect());
        captureModeBtn?.RegisterCallback<ClickEvent>(_ => CyclePlacementMode());
        slotAddBtn?.RegisterCallback<ClickEvent>(_ => AddAvatarSlot());

        // slotAddBtnが右端にある前提、「何個のslotが既にあるか」をざっくり推定して、番号被りを防ぐ
        slotCount = Mathf.Max(1, avatarSlotRow.IndexOf(slotAddBtn)); // ざっくり既存数から
        // slotAddBtnがSlot数がMaxになったら消えるように
        UpdateSlotAddButtonVisibility();

        // Slot Delete押下
        slotDeleteBtn.RegisterCallback<ClickEvent>(_ => ConfirmDeleteSlot());
        // overlay外タップで閉じる
        slotDeleteOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            // Deleteボタン自体は除外（押下できるように）
            if (evt.target == slotDeleteBtn || slotDeleteBtn.Contains((VisualElement)evt.target)) return;
            HideSlotDeleteOverlay();
        });
        // １つ目のSlotBtnにも付与
        AttachLongPressToSlot(initialAvatarSlotBtn);

        // 起動直後に「状態→UIの見た目への反映」を行う
        ApplyAllToUI();
        // レイヤー4(このレイヤー)のAspectをレイヤー3のスクリプトへ伝える
        ApplyAspectToGuide();

        // ---- Lighting Panelの制御 ----
        // Lighting Panel の open/close
        lightingBtn?.RegisterCallback<ClickEvent>(_ => ShowLightingPanel());
        lightingCloseBtn?.RegisterCallback<ClickEvent>(_ => HideLightingPanel());

        // Auto sync
        autoSyncBtn?.RegisterCallback<ClickEvent>(_ => ToggleAutoSyncUI());
        ApplyAutoSyncUI(); // 初期表示を状態に合わせる

        // Lighting panel内のTabsの切り替え
        tabMood?.RegisterCallback<ClickEvent>(_ => ShowLightingMood());
        tabDirection?.RegisterCallback<ClickEvent>(_ => ShowLightingDirection());

        // Presetをクリックしたものに切り替え
        presetBtnAuto?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnAuto));
        presetBtnSunny?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnSunny));
        presetBtnCloudy?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnCloudy));
        presetBtnIndoor?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnIndoor));
        presetBtnWarm?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnWarm));
        presetBtnSunset?.RegisterCallback<ClickEvent>(_ => SelectPreset(presetBtnSunset));

        // ---- Shado Panelの制御 ----
        // Shadow Panel open/close 
        shadowBtn?.RegisterCallback<ClickEvent>(_ => ShowShadowPanel());
        shadowCloseBtn?.RegisterCallback<ClickEvent>(_ => HideShadowPanel());

        // Shadow enable toggle (UI only)
        enableShadowBtn?.RegisterCallback<ClickEvent>(_ => ToggleShadowUI());
        ApplyShadowToggleUI(); // 初期表示

        // Shadow softness (UI only)
        softnessSoft?.RegisterCallback<ClickEvent>(_ => SelectShadowSoftness(softnessSoft));
        softnessMedium?.RegisterCallback<ClickEvent>(_ => SelectShadowSoftness(softnessMedium));
        softnessHard?.RegisterCallback<ClickEvent>(_ => SelectShadowSoftness(softnessHard));

    }

    // ---------- 1) Flash ----------
    public void ToggleFlash()
    {
        // flashOnを反転(ONならOFFに、OFFならONに)
        flashOn = !flashOn;
        // flashBtnが存在する場合、flashONがtrueなら"is-on"クラスをつける、falseなら外す
        flashBtn?.EnableInClassList("is-on", flashOn);

        // TODO: AR Foundation torch切替に接続する必要あり
        // Debug.Log($"Flash: {(flashOn ? "ON" : "OFF")}");
    }

    // ---------- 2) Flat ----------
    public void ToggleFlat()
    {
        // bool(flatVisible)を反転(ONならOFFに、OFFならONに)
        flatVisible = !flatVisible;
        // flatBtnが存在する場合、flashONがtrueなら"is-on"クラスをつける、falseなら外す
        flatBtn?.EnableInClassList("is-on", flatVisible);

        // TODO: Layer2レンダリング（Flat面）切替に接続する必要あり
        //Debug.Log($"Flat: {(flatVisible ? "Visible" : "Hidden")}");
    }

    // ---------- 3) Aspect (4:3 -> 1:1 -> 16:9) ----------
    // 状態を計算 → クラス付け替え → Layer3に通知
    public void CycleAspect()
    {
        aspect = NextAspect(aspect);
        ApplyAspectToUI();
        ApplyAspectToGuide();
    }

    // 4:3 -> 1:1 -> 16:9 -> 4:3(回帰)の順に回す
    CaptureAspect NextAspect(CaptureAspect a)
    {
        return a switch
        {
            CaptureAspect.FourThree => CaptureAspect.OneOne,
            CaptureAspect.OneOne => CaptureAspect.SixteenNine,
            _ => CaptureAspect.FourThree
        };
    }

    //aspectBtnから古いclassを外す→に"aspect-4x3"などのclassを付け替える
    void ApplyAspectToUI()
    {
        if (aspectBtn == null) return;

        aspectBtn.RemoveFromClassList("aspect-4x3");
        aspectBtn.RemoveFromClassList("aspect-1x1");
        aspectBtn.RemoveFromClassList("aspect-16x9");

        switch (aspect)
        {
            case CaptureAspect.FourThree: aspectBtn.AddToClassList("aspect-4x3"); break;
            case CaptureAspect.OneOne: aspectBtn.AddToClassList("aspect-1x1"); break;
            case CaptureAspect.SixteenNine: aspectBtn.AddToClassList("aspect-16x9"); break;
        }
    }

    // Layer3に captureGuide.SetAspect(aspect)で伝達
    void ApplyAspectToGuide()
    {
        if (captureGuide != null)
            captureGuide.SetAspect(aspect);
    }

    // ---------- 4) Placement Mode (GroundFix -> ScreenFix -> Follow) ----------
    // enumをいったん int にして、+1して、3で割った余り（%3）にすると 0→1→2→0 と回る。
    public void CyclePlacementMode()
    {
        placementMode = (PlacementMode)(((int)placementMode + 1) % 3);
        ApplyModeToUI();

        // TODO: Layer2の設置モード切替に接続する必要あり
        // Debug.Log($"Mode: {placementMode}");
    }

    // captureModeIconの古いclassを外す→"mode-ground"などのclassを付け替える→captureModeLabelのtextを変更する
    void ApplyModeToUI()
    {
        if (captureModeIcon == null) return;

        captureModeIcon.RemoveFromClassList("mode-ground");
        captureModeIcon.RemoveFromClassList("mode-screen");
        captureModeIcon.RemoveFromClassList("mode-follow");

        switch (placementMode)
        {
            case PlacementMode.GroundFix: captureModeIcon.AddToClassList("mode-ground"); break;
            case PlacementMode.ScreenFix: captureModeIcon.AddToClassList("mode-screen"); break;
            case PlacementMode.Follow: captureModeIcon.AddToClassList("mode-follow"); break;
        }

        if (captureModeLabel != null)
        {
            captureModeLabel.text = placementMode switch
            {
                PlacementMode.GroundFix => "Ground Fix",
                PlacementMode.ScreenFix => "Screen Fix",
                PlacementMode.Follow    => "Follow",
                _ => "Ground Fix"
            };
        }
        
    }

    // ---------- 5) AvatarSlot ----------
    int slotCount = 1; // 新規スロットの連番用（好きに）

    void AddAvatarSlot()
    {
        if (avatarSlotRow == null || slotAddBtn == null) return;

        // 既存スロット数（Addボタンを除く）
        int currentSlots = avatarSlotRow.Query<VisualElement>(className: "avatar-slot-container").ToList().Count - 1;
        if (currentSlots >= MaxSlots) return;

        // 新しいスロット要素を作る
        var newSlot = new VisualElement();
        newSlot.name = $"avatarSlotBtn_{slotCount++}";
        newSlot.AddToClassList("avatar-slot-container");

        var icon = new VisualElement();
        icon.name = $"{newSlot.name}_icon";
        icon.AddToClassList("slot-icon");

        newSlot.Add(icon);

        // slotAddBtn の直前に挿入
        int insertIndex = avatarSlotRow.IndexOf(slotAddBtn);
        if (insertIndex < 0) insertIndex = avatarSlotRow.childCount;
        avatarSlotRow.Insert(insertIndex, newSlot);

        // 任意：クリック挙動をつける
        newSlot.RegisterCallback<ClickEvent>(_ => Debug.Log($"Clicked {newSlot.name}"));

        // slotAddBtnの削除/再出現
        UpdateSlotAddButtonVisibility();

        // 長押しのイベント
        AttachLongPressToSlot(newSlot);
    }
    // Avatar SlotがMaxになったらAddボタンが消えるように
    void UpdateSlotAddButtonVisibility()
    {
        if (avatarSlotRow == null || slotAddBtn == null) return;

        // スロット数（Addボタン除外）
        int currentSlots =
            avatarSlotRow.Query<VisualElement>(className: "avatar-slot-container").ToList().Count - 1;

        bool shouldHide = currentSlots >= MaxSlots;
        slotAddBtn.EnableInClassList("is-hidden", shouldHide);
    }
    // 長押しでDelete Btn出す
    void AttachLongPressToSlot(VisualElement slot)
    {
        if (slot == null) return;
        slot.pickingMode = PickingMode.Position;

        Vector2 downPos = Vector2.zero;
        bool canceled = false;

        slot.RegisterCallback<PointerDownEvent>(evt =>
        {
            // debug
            Debug.Log("Slot PointerDown");
            
            // すでに出てたら閉じる
            HideSlotDeleteOverlay();

            canceled = false;
            downPos = evt.position;
            // Debug
            Debug.Log("Start long-press coroutine");
            // 0.5秒後に出す
            if (longPressRoutine != null) StopCoroutine(longPressRoutine);
            longPressRoutine = StartCoroutine(LongPressTimer(slot));

            evt.StopPropagation();
        });

        slot.RegisterCallback<PointerMoveEvent>(evt =>
        {
            // 指が動いたらキャンセル（スクロールと干渉しない）：2Dでの操作
            Vector2 p = new Vector2(evt.position.x, evt.position.y);
            if ((p - downPos).sqrMagnitude > 16f * 16f) // 16px以上動いたら(使ってみて変更可)
            {
                canceled = true;
                if (longPressRoutine != null) StopCoroutine(longPressRoutine);
            }
        });

        slot.RegisterCallback<PointerUpEvent>(evt =>
        {
            canceled = true;
            if (longPressRoutine != null) StopCoroutine(longPressRoutine);
        });

        IEnumerator LongPressTimer(VisualElement pressedSlot)
        {
            yield return new WaitForSeconds(0.5f);
            if (canceled) yield break;
            // Debug
            Debug.Log("Long press triggered!");
            ShowSlotDeleteOverlay(pressedSlot);
        }
    }
    void ShowSlotDeleteOverlay(VisualElement slot)
    {
        if (slotDeleteOverlay == null || slotDeleteBtn == null || slot == null) return;

        pendingDeleteSlot = slot;

        // 表示
        slotDeleteOverlay.RemoveFromClassList("is-hidden");
        slotDeleteOverlay.BringToFront();

        // ボタンサイズ（まだ0ならフォールバック）
        float btnW = slotDeleteBtn.resolvedStyle.width;
        float btnH = slotDeleteBtn.resolvedStyle.height;
        if (btnW <= 1f) btnW = 64f;
        if (btnH <= 1f) btnH = 33f;

        const float gap = 0f;

        // slotの上端中央（world座標）
        Vector2 slotTopCenterWorld = new Vector2(slot.worldBound.center.x, slot.worldBound.yMin);

        // overlayのローカル座標に変換
        Vector2 slotTopCenterLocal = slotDeleteOverlay.WorldToLocal(slotTopCenterWorld);

        // 「8px上」に配置（ボタン左上座標を計算）
        float x = slotTopCenterLocal.x - btnW * 0.5f;
        float y = slotTopCenterLocal.y - gap - btnH;

        // debug
        Debug.Log($"btnW={btnW} btnH={btnH} slotCenterX(local)={slotTopCenterLocal.x}");

        slotDeleteBtn.style.left = x;
        slotDeleteBtn.style.top  = y;
    }
    void ConfirmDeleteSlot()
    {
        if (pendingDeleteSlot == null) return;

        // Addボタンは削除対象外にしたいからガード
        if (pendingDeleteSlot == slotAddBtn) return;

        pendingDeleteSlot.RemoveFromHierarchy();
        pendingDeleteSlot = null;

        HideSlotDeleteOverlay();

        // ★Slot数が Max 未満なら slotAddBtn が復活する
        UpdateSlotAddButtonVisibility();
    }

    void HideSlotDeleteOverlay()
    {
        slotDeleteOverlay.AddToClassList("is-hidden");
        pendingDeleteSlot = null;
    }

    // ---------- 6) Lighting Panel ----------
    void ShowLightingPanel()
    {
        if (lightingPanel == null) return;
        lightingPanel.RemoveFromClassList("is-hidden");
        ShowLightingMood(); // デフォルトはMood

        if (!slidersInitialized)
            StartCoroutine(InitLightingSlidersWhenReady());
    }
    // パネルのクローズ
    void HideLightingPanel()
    {
        lightingPanel?.AddToClassList("is-hidden");
    }
    // Auto sync
    void ToggleAutoSyncUI()
    {
        autoSyncOn = !autoSyncOn;
        ApplyAutoSyncUI();
    }
    void ApplyAutoSyncUI()
    {
        autoSyncToggle?.EnableInClassList("is-active", autoSyncOn);

        if (autoSyncStatusLabel != null)
            autoSyncStatusLabel.text = autoSyncOn ? "ON" : "OFF";
    }
    // Mood/DirectionタブのActive切り替え
    void ShowLightingMood()
    {
        tabMood?.AddToClassList("is-selected");
        tabDirection?.RemoveFromClassList("is-selected");

        lightingPanelMood?.AddToClassList("is-active");
        lightingPanelDirection?.RemoveFromClassList("is-active");
    }
    void ShowLightingDirection()
    {
        tabDirection?.AddToClassList("is-selected");
        tabMood?.RemoveFromClassList("is-selected");

        lightingPanelDirection?.AddToClassList("is-active");
        lightingPanelMood?.RemoveFromClassList("is-active");

        if (!directionInitialized)
        StartCoroutine(InitDirectionControlsWhenReady());
    }
    // Presetの切り替え
    void SelectPreset(VisualElement selected)
    {
        if (presetSelections == null || selected == null) return;

        // ScrollView化して階層が変わっても「presetSelections」の「preset-btn」を全部探してOFFにする
        presetSelections.Query<VisualElement>(className: "preset-btn").ForEach(e =>
        e.RemoveFromClassList("is-selected")
        );
        // 選んだやつだけON
        selected.AddToClassList("is-selected");
        // Debug
        Debug.Log($"Preset selected: {selected.name}");

        // TODO: 温度/明るさをプリセット値に更新する繋ぎ込みが必要
    }
    // Mood Slider操作
    bool SlidersReady()
    {
        // 参照が取れているか
        if (colorTemperatureSlider == null || brightnessSlider == null) return false;
        if (colorTemperatureThumbBtn == null || brightnessThumbBtn == null) return false;

        // レイアウト確定しているか（widthが0だとノブ位置計算ができない）
        if (colorTemperatureSlider.resolvedStyle.width <= 1f) return false;
        if (brightnessSlider.resolvedStyle.width <= 1f) return false;

        // thumbも確定しているとより安全（USSの幅が反映されているか）
        if (colorTemperatureThumbBtn.resolvedStyle.width <= 1f) return false;
        if (brightnessThumbBtn.resolvedStyle.width <= 1f) return false;

        return true;
    }
    IEnumerator InitLightingSlidersWhenReady()
    {
        // 最大30フレーム待つ
        for (int i = 0; i < 30; i++)
        {
            if (SlidersReady()) break;
            yield return null;
        }

        if (!SlidersReady())
        {
            Debug.LogWarning("Sliders not ready (width=0). Init skipped.");
            yield break;
        }

        HookHorizontalSlider(
            slider: colorTemperatureSlider,
            thumb: colorTemperatureThumbBtn,
            onValue01Changed: t =>
            {
                colorTempK = Mathf.Lerp(2000f, 10000f, t);
                if (colorTemperatureValueLabel != null)
                    colorTemperatureValueLabel.text = $"{Mathf.RoundToInt(colorTempK)}K";
            },
            initial01: Mathf.InverseLerp(2000f, 10000f, colorTempK),
            setDragging: v => draggingTemp = v,
            isDragging: () => draggingTemp
        );

        HookHorizontalSlider(
            slider: brightnessSlider,
            thumb: brightnessThumbBtn,
            onValue01Changed: t =>
            {
                brightness = Mathf.Lerp(0.1f, 2.0f, t);
                if (brightnessValueLabel != null)
                    brightnessValueLabel.text = $"{brightness:0.0}";
            },
            initial01: Mathf.InverseLerp(0.1f, 2.0f, brightness),
            setDragging: v => draggingBright = v,
            isDragging: () => draggingBright
        );

        slidersInitialized = true;
    }
    void HookHorizontalSlider(
        VisualElement slider,
        VisualElement thumb,
        System.Action<float> onValue01Changed,
        float initial01,
        System.Action<bool> setDragging,
        System.Func<bool> isDragging
    )
    {
        if (slider == null || thumb == null) return;

        // 初期位置を反映
        SetThumbBy01(slider, thumb, initial01);
        onValue01Changed?.Invoke(initial01);

        slider.RegisterCallback<PointerDownEvent>(evt =>
        {
            // debug用
            Debug.Log($"PointerDown on {slider.name}");
            
            setDragging(true);
            slider.CapturePointer(evt.pointerId);
            UpdateFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        });

        slider.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!isDragging()) return;
            UpdateFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        });

        slider.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!isDragging()) return;
            setDragging(false);
            slider.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });

        void UpdateFromPointer(float localX)
        {
            float w = slider.resolvedStyle.width;
            if (w <= 1f) return;

            float thumbW = thumb.resolvedStyle.width;
            if (thumbW <= 1f) thumbW = 24f; // 保険（USSの値）
            float r = thumbW * 0.5f;

            // thumb中心の可動域
            float minX = r;
            float maxX = w - r;

            float clampedX = Mathf.Clamp(localX, minX, maxX);

            // 0..1 へ正規化（中心可動域基準）
            float t = (clampedX - minX) / (maxX - minX);

            SetThumbBy01(slider, thumb, t);
            onValue01Changed?.Invoke(t);
        }
    }

    void SetThumbBy01(VisualElement slider, VisualElement thumb, float t01)
    {
        float w = slider.resolvedStyle.width;
        if (w <= 1f) return;

        float thumbW = thumb.resolvedStyle.width;
        if (thumbW <= 1f) thumbW = 24f;
        float r = thumbW * 0.5f;

        float minX = r;
        float maxX = w - r;

        float x = Mathf.Lerp(minX, maxX, Mathf.Clamp01(t01));
        thumb.style.left = x; // 中心位置

    }

    // Direction Tab
    IEnumerator InitDirectionControlsWhenReady()
    {
        // 最大30フレーム待つ（タブ表示後にレイアウト確定を待つ）
        for (int i = 0; i < 30; i++)
        {
            if (DirectionReady()) break;
            yield return null;
        }

        if (!DirectionReady())
        {
            Debug.LogWarning("Direction controls not ready (width/height=0). Init skipped.");
            yield break;
        }

        // 初期位置を反映
        SetDirKnobBy01(dirX01, dirY01);
        SetElevationBy01(elevation01, updateLabel: true);

        // 入力を繋ぐ（Pad）
        HookDirectionPad();

        // 入力を繋ぐ（Elevation）
        HookElevationSlider();

        directionInitialized = true;
    }

    bool DirectionReady()
    {
        if (directionPad == null || dirKnob == null) return false;
        if (elevSlider == null || elevTrack == null || elevKnob == null) return false;

        if (directionPad.resolvedStyle.width <= 1f || directionPad.resolvedStyle.height <= 1f) return false;
        if (dirKnob.resolvedStyle.width <= 1f || dirKnob.resolvedStyle.height <= 1f) return false;

        if (elevSlider.resolvedStyle.height <= 1f) return false;
        if (elevTrack.resolvedStyle.height <= 1f) return false;
        if (elevKnob.resolvedStyle.height <= 1f) return false;

        return true;
    }
    // Direction Pad
    void HookDirectionPad()
    {
        directionPad.RegisterCallback<PointerDownEvent>(evt =>
        {
            draggingDir = true;
            directionPad.CapturePointer(evt.pointerId);
            UpdateDirFromLocal(evt.localPosition);
            evt.StopPropagation();
        });

        directionPad.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!draggingDir) return;
            UpdateDirFromLocal(evt.localPosition);
            evt.StopPropagation();
        });

        directionPad.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!draggingDir) return;
            draggingDir = false;
            directionPad.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
    }

    void UpdateDirFromLocal(Vector2 local)
    {
        float w = directionPad.resolvedStyle.width;
        float h = directionPad.resolvedStyle.height;

        // padは円想定。中心
        Vector2 center = new Vector2(w * 0.5f, h * 0.5f);

        // ノブ半径分、動ける半径を内側に取る
        float knobR = (float)dirKnob.resolvedStyle.width * 0.5f;
        float padR = Mathf.Min(w, h) * 0.5f - knobR;

        Vector2 v = local - center;

        // 円の外をドラッグしたら円周にクランプ
        if (v.magnitude > padR)
            v = v.normalized * padR;

        // ノブ中心座標（pad内）
        Vector2 knobCenter = center + v;

        // 0..1に正規化（中心=0.5）
        dirX01 = (knobCenter.x - knobR) / (w - knobR * 2f);
        dirY01 = (knobCenter.y - knobR) / (h - knobR * 2f);

        // 見た目更新
        SetDirKnobCenter(knobCenter);

        // TODO: 後で Layer2 に通知する（方位角など）
    }

    void SetDirKnobBy01(float x01, float y01)
    {
        float w = directionPad.resolvedStyle.width;
        float h = directionPad.resolvedStyle.height;
        float knobR = (float)dirKnob.resolvedStyle.width * 0.5f;

        float cx = Mathf.Lerp(knobR, w - knobR, Mathf.Clamp01(x01));
        float cy = Mathf.Lerp(knobR, h - knobR, Mathf.Clamp01(y01));

        SetDirKnobCenter(new Vector2(cx, cy));
    }

    void SetDirKnobCenter(Vector2 centerPx)
    {
        // dir-knob は position:absolute + translate(-50% -50%) 前提
        dirKnob.style.left = centerPx.x;
        dirKnob.style.top = centerPx.y;
    }
    // Elevation Slider
    void HookElevationSlider()
    {
        elevSlider.RegisterCallback<PointerDownEvent>(evt =>
        {
            draggingElev = true;
            elevSlider.CapturePointer(evt.pointerId);
            UpdateElevFromLocalY(evt.localPosition.y);
            evt.StopPropagation();
        });

        elevSlider.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!draggingElev) return;
            UpdateElevFromLocalY(evt.localPosition.y);
            evt.StopPropagation();
        });

        elevSlider.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!draggingElev) return;
            draggingElev = false;
            elevSlider.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
    }

    void UpdateElevFromLocalY(float localY)
    {
        // elevSliderの中で、trackの高さを使って正規化する
        float sliderH = elevSlider.resolvedStyle.height;
        float trackH = elevTrack.resolvedStyle.height;

        // Trackが中央に配置されている前提で、trackの開始Yを計算
        float trackTop = (sliderH - trackH) * 0.5f;
        float trackBottom = trackTop + trackH;

        float knobH = elevKnob.resolvedStyle.height;
        float knobR = knobH * 0.5f;

        // knob中心が動ける範囲（track内側）
        float minY = trackTop + knobR;
        float maxY = trackBottom - knobR;

        float clampedY = Mathf.Clamp(localY, minY, maxY);

        // 0..1（上=1、下=0 にしたいなら反転）
        float t = 1f - (clampedY - minY) / (maxY - minY);
        SetElevationBy01(t, updateLabel: true);

        // TODO: 後で Layer2 に通知する（仰角）
    }

    void SetElevationBy01(float t01, bool updateLabel)
    {
        elevation01 = Mathf.Clamp01(t01);

        float sliderH = elevSlider.resolvedStyle.height;
        float trackH = elevTrack.resolvedStyle.height;
        float trackTop = (sliderH - trackH) * 0.5f;
        float trackBottom = trackTop + trackH;

        float knobH = elevKnob.resolvedStyle.height;
        float knobR = knobH * 0.5f;

        float minY = trackTop + knobR;
        float maxY = trackBottom - knobR;

        // 上が1、下が0なので反転してYに戻す
        float y = Mathf.Lerp(maxY, minY, elevation01);

        elevKnob.style.top = y;

        if (updateLabel && elevationValueLabel != null)
        {
            // 例：0..90°に変換（好きな範囲でOK）
            float deg = Mathf.Lerp(0f, 90f, elevation01);
            elevationValueLabel.text = $"{Mathf.RoundToInt(deg)}°";
        }
    }

    // ---------- 7) Shadow Panel ----------
    // Panelの開閉
    void ShowShadowPanel()
    {
        if (shadowPanel == null) return;

        shadowPanel.RemoveFromClassList("is-hidden");

        // 初回だけスライダー初期化（display:none中はwidth=0だから）
        if (!shadowSliderInitialized)
            StartCoroutine(InitShadowSliderWhenReady());
    }

    void HideShadowPanel()
    {
        shadowPanel?.AddToClassList("is-hidden");
    }

    // Enable Shadowトグル　*TODO:後で、実際のシャドウのON/OFFコントロールに繋ぎ込み必要
    void ToggleShadowUI()
    {
        shadowEnabled = !shadowEnabled;
        ApplyShadowToggleUI();
    }

    void ApplyShadowToggleUI()
    {
        enableShadowToggle?.EnableInClassList("is-active", shadowEnabled);

        if (enableShadowStatusLabel != null)
            enableShadowStatusLabel.text = shadowEnabled ? "ON" : "OFF";
    }

    // Intensity スライダー　*TODO: 後でIntensityのコントロールに繋ぎ込み必要
    bool ShadowSliderReady()
    {
        return shadowIntensitySlider != null &&
            shadowIntensityThumbBtn != null &&
            shadowIntensitySlider.resolvedStyle.width > 1f &&
            shadowIntensityThumbBtn.resolvedStyle.width > 1f;
    }

    IEnumerator InitShadowSliderWhenReady()
    {
        for (int i = 0; i < 30; i++)
        {
            if (ShadowSliderReady()) break;
            yield return null;
        }

        if (!ShadowSliderReady())
        {
            Debug.LogWarning("Shadow intensity slider not ready (width=0). Init skipped.");
            yield break;
        }

        HookHorizontalSlider(
            slider: shadowIntensitySlider,
            thumb: shadowIntensityThumbBtn,
            onValue01Changed: t =>
            {
                shadowIntensity = Mathf.Lerp(0f, 1f, t);

                if (shadowIntensityValueLabel != null)
                    shadowIntensityValueLabel.text = $"{shadowIntensity:0.0}";
            },
            initial01: Mathf.InverseLerp(0f, 1f, shadowIntensity),
            setDragging: v => draggingShadowIntensity = v,
            isDragging: () => draggingShadowIntensity
        );

        shadowSliderInitialized = true;
    }

    // Softness Btn  *TODO:後で、実際のシャドウのコントロールに繋ぎ込み必要
    void SelectShadowSoftness(VisualElement selected)
    {
        if (shadowSoftnessSelections == null || selected == null) return;

        foreach (var child in shadowSoftnessSelections.Children())
            child.RemoveFromClassList("is-selected");

        selected.AddToClassList("is-selected");

        Debug.Log($"Shadow softness selected: {selected.name}");
    }


    // ---------- Init ----------
    void ApplyAllToUI()
    {
        flashBtn?.EnableInClassList("is-on", flashOn);
        flatBtn?.EnableInClassList("is-on", flatVisible);
        ApplyAspectToUI();
        ApplyModeToUI();
    }

    // ---------- External trigger ----------
    // ダブルタップ側（Layer2のGestureController）からこれを呼べばOK
    public void OnDoubleTap()
    {
        CyclePlacementMode();
    }
}