# 実装済み要件と技術仕様

aiCam プロジェクトにおける主要な実装要件と各コンポーネントの技術仕様を説明します。

## Occlusion 管理（AROcclusionManager）

### 目的
AR空間における現実的な前後関係の表現を実現するため、環境深度情報と人物セグメンテーションを活用したオクルージョン処理を提供します。

### 公開API
- `EnableDepth()`: オクルージョンを有効化（設定品質: Medium）
- `DisableDepth()`: オクルージョンを段階的に無効化
- `DisableDepthNow()`: オクルージョンを即座に無効化
- `CurrentDepthMode`: 現在の環境深度モード取得

### 例外対策
```csharp
// アプリ終了時のレース条件を回避
private void OnDisable()
{
    if (!_preDisabled)
    {
        TrySetAll(
            env: EnvironmentDepthMode.Disabled,
            humanStencil: HumanSegmentationStencilMode.Disabled,
            humanDepth: HumanSegmentationDepthMode.Disabled,
            pref: OcclusionPreferenceMode.NoOcclusion);
    }
}
```

### ライフサイクル
1. **Awake**: AROcclusionManager の参照取得と初期状態キャッシュ
2. **OnEnable**: 前回の要求値へ復帰
3. **OnDisable**: 安全な無効化処理（二重停止の防止）
4. **OnDestroy**: リソースのクリーンアップ

### 使用例
```csharp
// オクルージョンの動的切り替え
public void ToggleOcclusion()
{
    var toggle = GetComponent<OcclusionToggle>();
    if (toggle.CurrentDepthMode == EnvironmentDepthMode.Disabled)
    {
        toggle.EnableDepth();
    }
    else
    {
        toggle.DisableDepth();
    }
}
```

## iOS ビルド後処理

### 目的
iOS アプリケーションに必要な権限記述とフレームワークを自動的に設定し、手動設定のミスを防ぎます。

### Info.plist 自動編集
- **NSCameraUsageDescription**: AR カメラ使用の説明
- **NSPhotoLibraryAddUsageDescription**: 写真保存の説明
- **NSMicrophoneUsageDescription**: 動画録画時のマイク使用説明

### ローカライズ対応
英語・日本語の InfoPlist.strings を自動生成：
```
// en.lproj/InfoPlist.strings
"NSCameraUsageDescription" = "This app uses the camera for AR.";

// ja.lproj/InfoPlist.strings
"NSCameraUsageDescription" = "このアプリはAR表示のためにカメラを使用します";
```

### 必要Framework
- **Photos.framework**: 写真・動画のフォトライブラリ保存
- **AVFoundation.framework**: 動画録画とオーディオ処理

### 実装の特徴
- 既存の UsageDescription 値は上書きしない（ブランド固有の文言を保護）
- CFBundleLocalizations への言語追加
- Xcode プロジェクトへの lproj フォルダ自動追加

## 表情システム

### 目的
アバターの表情をリアルタイムで切り替え、自然なトランジションを提供します。

### Animator レイヤー構成
- **Base Layer**: 基本アニメーション（歩行、待機など）
- **Face Layer**: 表情アニメーション専用（重み制御によるブレンド）

### FaceController API
```csharp
public void SetFace(string faceName)
{
    if (stateHashByName.TryGetValue(faceName, out int hash))
    {
        // クロスフェードで自然な切り替え
        animator.CrossFade(hash, 0.2f, faceLayerIndex);
        layerWeight = 1f;
    }
}
```

### 連携UI
FaceUIManager による自動生成：
1. FaceController から表情名リストを取得
2. 各表情に対応するボタンを動的生成
3. ButtonFaceAction を通じた onClick イベント設定

### フェードアウト挙動
```csharp
// keepFace = false の場合、時間経過で表情が元に戻る
if (!keepFace && layerWeight > 0f)
{
    layerWeight -= fadeOutSpeed * Time.deltaTime;
    animator.SetLayerWeight(faceLayerIndex, layerWeight);
}
```

## 実装ファイル一覧

### Core Scripts
- [`OcclusionToggle.cs`](../aiCam/Assets/Scripts/OcclusionToggle.cs) - オクルージョン管理
- [`FaceController.cs`](../aiCam/Assets/Scripts/FaceController.cs) - 表情制御
- [`FaceUIManager.cs`](../aiCam/Assets/Scripts/FaceUIManager.cs) - 表情UI管理
- [`ARPhotoController.cs`](../aiCam/Assets/Scripts/ARPhotoController.cs) - AR撮影機能
- [`PlaceAvatarOnPlaneOnly.cs`](../aiCam/Assets/Scripts/PlaceAvatarOnPlaneOnly.cs) - アバター配置

### Editor Scripts
- [`IOSPlistPostProcess.cs`](../aiCam/Assets/Editor/IOSPlistPostProcess.cs) - iOS権限設定
- [`AddIOSFrameworks.cs`](../aiCam/Assets/Editor/AddIOSFrameworks.cs) - フレームワーク追加
- [`FaceUIManagerEditor.cs`](../aiCam/Assets/Editor/FaceUIManagerEditor.cs) - カスタムエディタ

### 依存パッケージ
- AR Foundation 6.2.0
- ARCore XR Plugin 6.1.1
- ARKit XR Plugin 6.1.1
- Universal RP 17.1.0

## 設計方針

1. **防御的プログラミング**: 例外処理とnullチェックの徹底
2. **自動化優先**: ビルド設定の自動化による人的ミスの削減
3. **拡張性**: インターフェースとコンポーネント設計による機能追加の容易性
4. **パフォーマンス**: オクルージョン品質の段階的制御による負荷調整

## 関連ページ

- [Occlusion Management](./Occlusion-Management.md) - オクルージョン詳細仕様
- [iOS Build Postprocess](./iOS-Build-Postprocess.md) - iOSビルド後処理詳細
- [Face System](./Face-System.md) - 表情システム詳細仕様