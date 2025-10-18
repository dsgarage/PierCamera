# AROcclusionManager セットアップガイド

## 問題の概要

UnityEditor環境でARFoundationのAROcclusionManagerを有効にすると、以下のエラーが発生します:

```
NullReferenceException: Object reference not set to an instance of an object
UnityEngine.XR.ARFoundation.AROcclusionManager.DestroyTextures ()
UnityEngine.XR.ARFoundation.AROcclusionManager.OnDisable ()
```

これはEditor環境ではARサブシステムが存在しないため、内部のテクスチャ管理でNullReferenceが発生するためです。

## 解決方法

### 1. AROcclusionManagerをInspectorで無効化

**ARCamera.unity シーン内:**
1. Hierarchy で `XR Origin` (または `AR Session Origin`) を選択
2. Inspector で `AR Occlusion Manager` コンポーネントを探す
3. コンポーネントの左上のチェックボックスを**オフ**にして無効化
4. シーンを保存

### 2. AROcclusionSafeEnablerスクリプトの使用

`AROcclusionSafeEnabler.cs` が自動的に以下を行います:

- **UnityEditor環境**: AROcclusionManagerを無効化してエラーを防ぐ
- **実機環境**: 安全にAROcclusionManagerを初期化・有効化

### 3. セットアップ手順

1. XR OriginオブジェクトにAROcclusionManagerコンポーネントがアタッチされていることを確認
2. 同じオブジェクトに`AROcclusionSafeEnabler`コンポーネントを追加
3. InspectorでAROcclusionManagerの参照を設定
4. 必要に応じてオクルージョン設定を調整:
   - **Environment Depth**: 環境のオクルージョン（推奨）
   - **Human Segmentation**: 人物のオクルージョン（必要な場合のみ）
   - **Warmup Frames**: 初期化待機フレーム数（デフォルト: 5）

## 動作の仕組み

### Editor環境
- `AROcclusionSafeEnabler`がAROcclusionManagerを無効化
- エラーなくEditor上で動作確認が可能

### 実機環境
1. アプリ起動時、AROcclusionManagerは無効状態
2. ARSessionとサブシステムの初期化を待機（0.5秒 + warmupFrames）
3. サブシステムが準備完了したことを確認
4. AROcclusionManagerを有効化
5. オクルージョン設定を適用

## トラブルシューティング

### エラーが消えない場合

1. **AROcclusionManagerがInspectorで無効化されているか確認**
2. **DefaultExecutionOrderの確認**: AROcclusionSafeEnablerが-32000に設定されていることを確認
3. **シーンを再保存**: 変更を保存してUnityを再起動

### 実機でオクルージョンが動作しない場合

1. **デバイスがDepthをサポートしているか確認**: LiDARセンサー搭載デバイスが必要
2. **ログを確認**: `[AROcclusionSafeEnabler]`のログメッセージを確認
3. **Warmup Framesを増やす**: 初期化に時間がかかる場合、5→10に増やしてみる

## 推奨設定

```
Environment Depth: Medium または Fastest
Human Segmentation: 無効（必要な場合のみ有効化）
Warmup Frames: 5
Occlusion Preference: PreferEnvironmentOcclusion
```

## 注意事項

- UnityEditor上ではオクルージョン効果は確認できません
- 実機ビルドでテストしてください
- iOS: iPhone 12 Pro以降、iPad Pro (2020以降) がLiDARをサポート
- Android: ARCore Depth API対応デバイスが必要
