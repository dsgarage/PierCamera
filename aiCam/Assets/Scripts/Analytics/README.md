# Firebase Analytics 導入ガイド

## 概要
デバイス情報（機種、LiDAR有無、カテゴリ）をFirebase Analyticsで収集します。

## 導入手順

### 1. Firebase Console設定
1. [Firebase Console](https://console.firebase.google.com) にアクセス
2. 「プロジェクトを追加」または既存プロジェクトを選択
3. 「アプリを追加」→「iOS」を選択
4. Bundle ID: `is.pier.beta`
5. `GoogleService-Info.plist` をダウンロード

### 2. GoogleService-Info.plist配置
ダウンロードした `GoogleService-Info.plist` を以下に配置：
```
Assets/GoogleService-Info.plist
```

### 3. Firebase Unity SDK導入
1. [Firebase Unity SDK](https://firebase.google.com/download/unity) をダウンロード
2. `FirebaseAnalytics.unitypackage` をインポート

### 4. Scripting Define Symbols追加
Project Settings → Player → Other Settings → Scripting Define Symbols に追加：
```
FIREBASE_ANALYTICS
```

### 5. シーン設定
1. 空のGameObjectを作成（名前: `Analytics`）
2. `DeviceAnalytics` コンポーネントをアタッチ

## 使用方法

### 自動収集
`DeviceAnalytics` は起動時に以下を自動送信：
- device_model: "iPhone14,2"
- device_name: "iPhone 13 Pro"
- os_version: "iOS 17.0"
- has_lidar: "yes" / "no"
- device_category: "HighEnd" / "MidRange" / "Standard" / "LowEnd"
- memory_mb: 6144
- graphics_memory_mb: 1536

### カスタムイベント
```csharp
// 写真撮影
DeviceAnalytics.Instance.LogPhotoCapture("ar_mode", withAvatar: true);

// アバターロード
DeviceAnalytics.Instance.LogAvatarLoad("vrm", success: true, loadTimeSeconds: 2.5f);
```

### LiDAR判定
```csharp
if (DeviceAnalytics.HasLiDAR())
{
    // LiDAR搭載機種の処理
}

var category = DeviceAnalytics.GetDeviceCategory();
switch (category)
{
    case DeviceAnalytics.DeviceCategory.HighEnd:
        // 最高品質設定
        break;
    case DeviceAnalytics.DeviceCategory.LowEnd:
        // 軽量設定
        break;
}
```

## デバッグモード
Firebase SDKがない状態でも `debugMode = true` でログ出力されます。

## Firebase Console確認
イベントは Firebase Console → Analytics → Events で確認できます。
（データ反映に最大24時間かかる場合があります）
