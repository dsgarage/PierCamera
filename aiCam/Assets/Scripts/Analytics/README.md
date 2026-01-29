# Analytics ブリッジ

## 概要
デバイス情報（機種、LiDAR有無、カテゴリ）およびアバター関連情報を収集し、親アプリ（iOS）のFirebase SDKに送信します。

## アーキテクチャ

```
┌─────────────────┐    event_name|json ┌─────────────────┐
│     Unity       │ ─────────────────> │   親アプリ(iOS) │
│ AnalyticsBridge │  sendMessageTo     │ NativeCallProxy │
│                 │  MobileApp()       │                 │
└─────────────────┘                    └─────────────────┘
                                              │
                                              ▼
                                       ┌─────────────────┐
                                       │  Firebase SDK   │
                                       │ (Analytics/     │
                                       │  Crashlytics)   │
                                       └─────────────────┘
```

### 設計方針
- **参照関係の遵守**: 親アプリ → PierCamera（Unity）の参照方向を維持
- **Firebase SDK非依存**: Unity側にFirebase SDKを入れない
- **既存インフラの活用**: `NativeCallProxy.sendMessageToMobileApp()` を使用

## メッセージ形式

```
event_name|payloadJson
```

### プレフィックス
| プレフィックス | 用途 |
|--------------|------|
| `Analytics_` | Firebase Analytics |
| `Crashlytics_` | Firebase Crashlytics |

## ファイル構成

| ファイル | 説明 |
|---------|------|
| `AnalyticsBridge.cs` | 親アプリへのメッセージ送信（静的クラス） |
| `CrashlyticsHelper.cs` | Crashlytics用ヘルパー（アバター情報設定） |
| `DeviceAnalytics.cs` | デバイス情報収集・送信（MonoBehaviour） |

## 使用方法

### デバイス情報の自動送信
`DeviceAnalytics` コンポーネントをシーンに配置すると、起動時に以下が送信されます：
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

### Crashlytics情報設定
```csharp
// アバター情報をクラッシュレポートに付加
CrashlyticsHelper.SetAvatarInfo("/path/to/avatar.vrm");

// スロット情報
CrashlyticsHelper.SetSlotInfo(slotIndex);

// 非致命的エラーの記録
CrashlyticsHelper.LogAvatarLoadError("/path/to/avatar.vrm", "Texture load failed");
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

## 親アプリ側の実装

親アプリは `NativeCallsProtocol` で受信したメッセージを解析し、Firebase SDKを呼び出します：

```swift
func handleUnityMessage(_ message: String) {
    // メッセージを分割: "event_name|payloadJson"
    let parts = message.split(separator: "|", maxSplits: 1)
    guard parts.count == 2 else { return }

    let eventName = String(parts[0])
    let payloadString = String(parts[1])

    guard let data = payloadString.data(using: .utf8),
          let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }

    // Analytics系
    if eventName.hasPrefix("Analytics_") {
        switch eventName {
        case "Analytics_init":
            // 初期化完了
            break
        case "Analytics_logEvent":
            let name = json["eventName"] as? String ?? ""
            let params = json["parameters"] as? [String: Any] ?? [:]
            Analytics.logEvent(name, parameters: params)
        case "Analytics_setUserProperty":
            let name = json["name"] as? String ?? ""
            let value = json["value"] as? String
            Analytics.setUserProperty(value, forName: name)
        default:
            break
        }
    }
    // Crashlytics系
    else if eventName.hasPrefix("Crashlytics_") {
        switch eventName {
        case "Crashlytics_setCustomKey":
            let key = json["key"] as? String ?? ""
            let value = json["value"] as? String ?? ""
            Crashlytics.crashlytics().setCustomValue(value, forKey: key)
        case "Crashlytics_log":
            let msg = json["message"] as? String ?? ""
            Crashlytics.crashlytics().log(msg)
        case "Crashlytics_logError":
            let domain = json["domain"] as? String ?? "Unity"
            let msg = json["message"] as? String ?? ""
            let error = NSError(domain: domain, code: 0, userInfo: [NSLocalizedDescriptionKey: msg])
            Crashlytics.crashlytics().record(error: error)
        default:
            break
        }
    }
}
```

## メッセージ一覧

### Analytics

| イベント名 | ペイロード例 |
|-----------|-------------|
| `Analytics_init` | `{"version":"1.0"}` |
| `Analytics_logEvent` | `{"eventName":"app_launch","parameters":{"device_category":"HighEnd"}}` |
| `Analytics_setUserProperty` | `{"name":"device_category","value":"HighEnd"}` |

### Crashlytics

| イベント名 | ペイロード例 |
|-----------|-------------|
| `Crashlytics_setCustomKey` | `{"key":"avatar_filename","value":"model.vrm"}` |
| `Crashlytics_log` | `{"message":"Avatar loaded: model.vrm (1234567 bytes)"}` |
| `Crashlytics_logError` | `{"domain":"AvatarLoad","message":"model.vrm: Texture load failed"}` |

## デバッグモード
Unity Editor または非iOSプラットフォームでは、メッセージが `Debug.Log` に出力されます。
