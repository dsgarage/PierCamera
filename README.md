# aiCam

AR Foundation を用いたモバイルAR撮影アプリケーション。環境深度による現実的なオクルージョン表示と、アバター表情のリアルタイム切り替えを実現し、高品質なAR体験を提供します。

## 主な機能

- **AR オクルージョン管理**: AROcclusionManager による環境深度・人物セグメンテーションの動的制御
- **表情システム**: Animator レイヤーを使用した表情のクロスフェード切り替え
- **AR 撮影機能**: 写真・動画撮影とフォトライブラリへの保存
- **アバター配置**: 平面検出によるAR空間へのアバター配置
- **iOS ビルド自動化**: Info.plist への権限記述とフレームワークの自動追加

## 対応環境

| 項目 | バージョン/要件 |
|------|--------------|
| Unity | 6000.1.11f1 |
| AR Foundation | 6.2.0 |
| ARCore XR Plugin | 6.1.1 |
| ARKit XR Plugin | 6.1.1 |
| Universal RP | 17.1.0 |
| プラットフォーム | iOS 12.0+, Android 7.0+ (API Level 24+) |

## セットアップ

### 1. Unity プロジェクトの準備

1. Unity Hub で Unity 6000.1.11f1 をインストール
2. プロジェクトを開く
3. `File > Build Settings` で iOS または Android を選択
4. `Player Settings` で以下を確認：
   - **iOS**: Minimum iOS Version を 12.0 以上に設定
   - **Android**: Minimum API Level を 24 以上に設定

### 2. シーンの配置

1. `Assets/Scenes/MainScene` を開く
2. Hierarchy で以下の構成を確認：
   - AR Session Origin (AR Camera を含む)
   - AR Session
   - Avatar Prefab (表情システム付き)
   - UI Canvas (表情切り替えボタン)

### 3. 権限設定

#### iOS
ビルド時に以下の権限が自動的に Info.plist に追加されます：
- カメラ使用権限 (NSCameraUsageDescription)
- フォトライブラリ追加権限 (NSPhotoLibraryAddUsageDescription)
- マイク使用権限 (NSMicrophoneUsageDescription)

※ 英語・日本語のローカライズも自動生成されます

#### Android
`Assets/Plugins/Android/AndroidManifest.xml` に以下を追加（未設定の場合）：
```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
```

## 基本的な使い方

### アプリの起動
1. ビルドしたアプリをデバイスで起動
2. カメラ権限を許可
3. 平面を検出するまでデバイスを動かす

### 表情の切り替え
- 画面下部の表情ボタンをタップして切り替え
- または、スクリプトから `FaceController.SetFace(string faceName)` を呼び出し

### オクルージョンの ON/OFF
スクリプトから制御：
```csharp
// オクルージョンを有効化
OcclusionToggle.EnableDepth();

// オクルージョンを無効化
OcclusionToggle.DisableDepth();
```

### 写真撮影
画面上の撮影ボタンをタップするか、スクリプトから：
```csharp
ARPhotoController.TakePhoto();
```

## iOS ビルド手順

### 1. ビルド設定
1. `File > Build Settings` を開く
2. iOS を選択し、`Switch Platform`
3. シーンが追加されていることを確認

### 2. ビルド実行
1. `Build` または `Build And Run` をクリック
2. 出力先フォルダを選択

### 3. 自動後処理
ビルド後、以下が自動的に実行されます：

#### Info.plist への追加
- カメラ・フォトライブラリ・マイクの使用説明文
- 英語/日本語ローカライズファイル (en.lproj, ja.lproj)

#### フレームワークの追加
- Photos.framework (フォトライブラリ保存用)
- AVFoundation.framework (動画録画用)

### 4. Xcode での最終確認
1. 生成された `.xcodeproj` を Xcode で開く
2. Signing & Capabilities でチーム設定
3. デバイスを接続して実行

## 既知の制約・注意点

### パフォーマンス
- オクルージョン有効時は GPU 負荷が増加します
- 低スペックデバイスでは `EnvironmentDepthMode.Fastest` の使用を推奨

### iOS 固有の制約
- iOS 14+ で LiDAR センサー搭載機種は高精度な深度情報を取得可能
- それ以外のデバイスでは推定深度による動作となります

### Android 固有の制約
- ARCore 対応デバイスが必要です
- 一部デバイスでは深度センサーが利用できない場合があります

### アニメーション
- 表情アニメーションクリップ名は Animator ステート名と一致させる必要があります
- フェードアウト中の表情切り替えは前の表情がキャンセルされます

## ライセンス

本プロジェクトのコードは MIT ライセンスで提供されます。

### サードパーティアセット

本プロジェクトは以下のアセットを使用しています：
- **lilToon**: シェーダーシステム (MIT License)
- **Unity-chan Model**: © Unity Technologies Japan/UCL
- その他フォント・テクスチャアセット（各アセットのライセンスを参照）

## トラブルシューティング

### ビルドエラーが発生する場合
1. Unity バージョンが 6000.1.11f1 であることを確認
2. Package Manager で AR Foundation 6.2.0 がインストールされていることを確認
3. `Edit > Project Settings > XR Plug-in Management` で ARCore/ARKit が有効になっていることを確認

### アプリ起動時にカメラが表示されない
1. デバイスの設定でカメラ権限が許可されていることを確認
2. AR Session と AR Session Origin が Scene に配置されていることを確認

### オクルージョンが機能しない
1. デバイスが深度センサーまたは深度推定に対応していることを確認
2. `OcclusionToggle` コンポーネントが AR Camera に追加されていることを確認