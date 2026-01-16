# 外部UIデベロッパー向け開発ガイドライン

## 概要

本ドキュメントは、PierCameraプロジェクトのUI開発を担当する外部エンジニア向けのガイドラインです。

---

## 作業内容

### 目的

ARカメラアプリ「PierCamera」のメインUI（撮影画面）をUIToolkitで刷新する。

### 対象画面

**ARCamera_origin** - メイン撮影画面

現在の機能：
- 写真撮影（タップ）/ 動画撮影（長押し、最大5秒）
- アバター管理（VRM/FBXモデルのスロット管理）
- ライティング調整（AR光推定またはマニュアル）
- シャドウ調整（有効/無効、強度、ソフトネス）
- 表情/ポーズ切り替え
- アスペクト比変更（Full / 16:9 / 3:2 / 1:1）

### 既存UIの仕様書

詳細な仕様は以下のドキュメントを参照してください：

| ドキュメント | 内容 |
|-------------|------|
| `aiCam/Docs/UI/ARCamera_UI_Specification.md` | UI仕様書（全体構造、各コンポーネント詳細） |
| `aiCam/Docs/UI/ARCamera_UI_Layout.md` | レイアウト詳細 |

### UIコンポーネント一覧

| コンポーネント | 説明 |
|---------------|------|
| captureButton | 撮影ボタン（120x120px、円形プログレス付き） |
| topPanel | 上部機能ボタン（Light, Shadow, Expression, Pose, Plane） |
| sidePanel | 左サイドバー（設定, アスペクト比, フラッシュ, バグレポート） |
| AvaterSlot | アバター選択パネル（水平スクロール、動的スロット追加） |
| lightingPanelOverlay | ライティング設定パネル |
| shadowPanelOverlay | シャドウ設定パネル |
| alertBar | 警告/エラー通知バー |
| iconPreviewPanel | 撮影後プレビュー確認パネル |

### 作業範囲

**開発対象（UITK_Pier内）：**
1. UXML/USS ファイルの作成・編集
2. UIコントローラースクリプト（C#）の作成
3. アイコン画像の配置

**対象外（既存コードとの結合）：**
- 既存のCameraCaptureController.csとの統合
- AR機能との接続
- アバターローダーとの連携

### 成果物

1. `Assets/UITK_Pier/UI/ARCameraScreen/` 内のUXML/USSファイル
2. `Assets/UITK_Pier/UI/Scripts/` 内のコントローラースクリプト
3. `Assets/UITK_Pier/UI/Icons/` 内のアイコン画像

### 受け入れ基準

1. 既存UIの機能を全て網羅していること
2. UIToolkitのベストプラクティスに従っていること
3. レスポンシブ対応（セーフエリア考慮）
4. `Assets/UITK_Pier/` 以外のファイルを変更していないこと
5. **既存の要素IDを完全に維持していること**（下記参照）

### 重要: 要素ID互換性要件

**移管時にUXML/USS/PanelSettingsのファイル差し替えのみで完結させるため、既存の要素IDを必ず維持してください。**

コントローラー（CameraCaptureController.cs）は以下の要素IDでUIを操作します。
これらのIDが存在しない場合、アプリケーションは正常に動作しません。

#### 必須要素ID一覧

**撮影ボタン関連:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `captureButton` | VisualElement | 撮影ボタン（タップ/長押し検出） |
| `innerCircle` | VisualElement | 内側円（録画時に色変更） |
| `progressRing` | VisualElement | プログレスリング親要素 |
| `progressArc` | VisualElement | 録画進捗の円弧 |
| `flashOverlay` | VisualElement | 撮影フラッシュ演出 |

**ギャラリー/プレビュー:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `galleryThumbnail` | VisualElement | 最後の撮影サムネイル |
| `viewerOverlay` | VisualElement | 全画面プレビューオーバーレイ |
| `viewerImage` | Image | プレビュー画像 |
| `iconPreviewPanel` | VisualElement | アイコン確認パネル |
| `iconPreviewImage` | VisualElement | アイコンプレビュー画像 |
| `iconPreviewRetake` | Button | 「撮り直す」ボタン |
| `iconPreviewConfirm` | Button | 「確定」ボタン |

**アラート:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `alertBar` | VisualElement | 警告/エラーバー |
| `alertMessage` | Label | アラートメッセージ |
| `alertClose` | Button | アラート閉じるボタン |

**上部パネル:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `topPanel` | VisualElement | 上部パネルコンテナ |
| `topButton1` | Button | ライティング |
| `topButton2` | Button | シャドウ |
| `topButton3` | Button | 表情 |
| `topButton4` | Button | ポーズ |
| `topButton5` | Button | 平面表示ON/OFF |

**サイドパネル:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `sidePanel` | VisualElement | サイドパネルコンテナ |
| `sideButton1` | Button | 設定 |
| `sideButton2` | Button | アスペクト比 |
| `sideButton3` | Button | フラッシュ |
| `sideButtonBugReport` | Button | バグレポート |

**アバタースロット:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `bottomPanel` | VisualElement | 下部パネルコンテナ |
| `bottomScrollView` | ScrollView | スロットスクロールビュー |
| `bottomButtonContainer` | VisualElement | スロットボタンコンテナ |
| `bottomButtonAdd` | Button | スロット追加（+）ボタン |

**アスペクト比マスク:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `topMask` | VisualElement | 上マスク |
| `bottomMask` | VisualElement | 下マスク |
| `leftMask` | VisualElement | 左マスク |
| `rightMask` | VisualElement | 右マスク |

**設定パネル:**
| 要素ID | 型 | 用途 |
|--------|-----|------|
| `settingsPanelBackdrop` | VisualElement | 設定パネル背景 |
| `lightingPanelOverlay` | VisualElement | ライティングパネル |
| `lightingPanelClose` | Button | ライティングパネル閉じる |
| `shadowPanelOverlay` | VisualElement | シャドウパネル |
| `shadowPanelClose` | Button | シャドウパネル閉じる |

---

## 1. プロジェクト構成

### 1.1 作業ディレクトリ

**重要: 全てのUI作業は以下のディレクトリ内で行ってください。**

```
Assets/UITK_Pier/
├── UI/
│   ├── ARCameraScreen/     # メインUI (UXML/USS)
│   ├── Icons/              # アイコン画像
│   ├── Scripts/            # UIコントローラー
│   └── Styles/             # 共通スタイル (tokens.uss)
└── UIToolkit/
    ├── PanelSettings.asset
    └── UnityThemes/
```

### 1.2 既存UIとの関係

| ディレクトリ | 用途 | 編集可否 |
|-------------|------|----------|
| `Assets/UITK_Pier/` | 新UI開発用 | ✅ 編集可 |
| `Assets/UI/CameraCapture/` | 既存UI | ❌ 編集禁止 |
| `Assets/UI Toolkit/` | 既存設定 | ❌ 編集禁止 |
| `Assets/Scripts/` | 既存スクリプト | ❌ 編集禁止 |

---

## 2. 環境セットアップ

### 2.1 既知の問題: SSHパッケージエラー

プロジェクトを開くと、以下のパッケージでエラーが発生する場合があります：

```
- com.dsgarage.unisil
- jp.dsgarage.cc2unimcp
```

**これらのエラーは無視して構いません。** UI開発には影響しません。

### 2.2 必要な環境

- Unity 6000.2.x (Unity 6)
- UIToolkit (標準搭載)

---

## 3. 開発ルール

### 3.1 絶対禁止事項

1. **`Assets/UITK_Pier/` 以外のファイルを変更しない**
2. **既存のUnityThemesを変更しない** (`Assets/UI Toolkit/UnityThemes/`)
3. **URP設定を変更しない** (`Assets/URP/Settings/`)
4. **シーンファイルを変更しない** (`Assets/Scenes/`)

### 3.2 推奨事項

1. 独自のPanelSettingsを使用する (`Assets/UITK_Pier/UIToolkit/PanelSettings.asset`)
2. 独自のUnityThemesを使用する (`Assets/UITK_Pier/UIToolkit/UnityThemes/`)
3. スタイル変数は `tokens.uss` で一元管理する
4. アイコンは `Assets/UITK_Pier/UI/Icons/` に配置する

---

## 4. UIファイル構成

### 4.1 UXML/USS ファイル

```
Assets/UITK_Pier/UI/ARCameraScreen/
├── CaptureControls.uxml    # メインUI構造
├── CaptureControls.uss     # メインUIスタイル
├── CaptureGuide.uxml       # ガイドUI構造
└── CaptureGuide.uss        # ガイドUIスタイル
```

### 4.2 スタイル変数 (tokens.uss)

```css
:root {
    --primary-color: #007AFF;
    --background-color: rgba(0, 0, 0, 0.5);
    /* ... */
}
```

### 4.3 コントローラー

```
Assets/UITK_Pier/UI/Scripts/
├── CaptureControlsController.cs    # メインUI制御
└── CaptureGuideController.cs       # ガイドUI制御
```

---

## 5. Gitワークフロー

### 5.1 ブランチ

```
feature/ui-overhaul-documentation  ← このブランチで作業
```

### 5.2 コミットルール

- `Assets/UITK_Pier/` 内のファイルのみをコミット
- 他のファイルが変更されていないことを確認してからコミット

```bash
# コミット前の確認
git status

# UITK_Pier以外の変更がないことを確認
git diff --name-only | grep -v "UITK_Pier"
```

### 5.3 Pull Request

1. 変更は `Assets/UITK_Pier/` 内のみであることを確認
2. PRタイトルに `[UI]` プレフィックスを付ける
3. 変更内容のスクリーンショットを添付

---

## 6. 動作確認

### 6.1 テスト用シーン

UI動作確認用のシーンを作成する場合は、以下の命名規則に従ってください：

```
Assets/UITK_Pier/Scenes/UITK_Pier_Test.unity
```

### 6.2 ビルド確認

変更後、以下を確認してください：

1. コンソールにエラーがないこと (SSH関連エラーは除く)
2. Play モードでUIが正しく表示されること

---

## 7. トラブルシューティング

### 7.1 SSHパッケージエラー

**症状:** `com.dsgarage.unisil` や `jp.dsgarage.cc2unimcp` の解決エラー

**対処:** 無視して作業を続行。UI開発には影響なし。

### 7.2 UIが表示されない

**確認事項:**
1. PanelSettingsがUIDocumentにアタッチされているか
2. Source Assetに正しいUXMLが設定されているか

### 7.3 スタイルが適用されない

**確認事項:**
1. UXMLにUSSがリンクされているか
2. セレクタ名が正しいか

---

## 8. 連絡先

質問や問題がある場合は、GitHub Issue #448 にコメントしてください。

- Issue: https://github.com/dsgarage/PierCamera/issues/448

---

## 変更履歴

| 日付 | バージョン | 変更内容 |
|------|-----------|----------|
| 2026-01-16 | 1.0 | 初版作成 |
| 2026-01-16 | 1.1 | 作業内容セクション追加 |
| 2026-01-16 | 1.2 | 要素ID互換性要件追加 |
