# オクルージョンが効かない場合のトラブルシューティング

## 確認すべきログ

Xcodeコンソールで以下のログを確認してください:

### 正常な場合
```
[AROcclusionSafeEnabler] Awake called on device
[AROcclusionSafeEnabler] Starting EnableWhenReady coroutine...
[AROcclusionSafeEnabler] Warmup complete. Checking subsystem...
[AROcclusionSafeEnabler] Retry 0/20: subsystem=true, running=true
[AROcclusionSafeEnabler] Subsystem is ready! Enabling occlusion...
[AROcclusionSafeEnabler] AROcclusionManager enabled
[AROcclusionSafeEnabler] Occlusion modes applied - EnvDepth: Medium, Preference: PreferEnvironmentOcclusion
```

### タイムアウトの場合
```
[AROcclusionSafeEnabler] Retry 0/20: subsystem=false, running=false
[AROcclusionSafeEnabler] Retry 1/20: subsystem=false, running=false
...
[AROcclusionSafeEnabler] Occlusion subsystem did not become ready in time. Occlusion will remain disabled.
```

## 対処法

### 1. デバイスがDepthをサポートしているか確認

オクルージョンにはLiDARセンサーが必要です:

**サポートデバイス:**
- iPhone 12 Pro / Pro Max
- iPhone 13 Pro / Pro Max
- iPhone 14 Pro / Pro Max
- iPhone 15 Pro / Pro Max
- iPad Pro (2020以降、11インチ/12.9インチ)

### 2. AROcclusionManagerの設定を確認

Inspectorで以下を確認:
- `Environment Depth` が有効
- `Environment Depth Mode` が `Medium` または `Fastest`に設定
- デバイスが対応していない場合は自動的に `Disabled` になります

### 3. ARSessionの設定を確認

`AR Session` コンポーネントで:
- `Attempt Update` が有効
- iOS 14.0以降が必要

### 4. タイムアウトしている場合

`AROcclusionSafeEnabler` のInspectorで:
- `Warmup Frames` を 5 → 10 に増やす
- それでも解決しない場合は、以下の手動設定を試す

## 手動設定（最終手段）

AROcclusionSafeEnablerを無効化して、Inspector上で直接設定:

1. XR Origin の `AROcclusionSafeEnabler` を無効化（チェックを外す）
2. `AR Occlusion Manager` を有効化
3. Inspector で以下を設定:
   - `Environment Depth Mode`: Medium
   - `Occlusion Preference Mode`: PreferEnvironmentOcclusion

**注意:** この方法ではUnityEditor上でエラーが出ますが、実機では動作します

## よくある問題

### オクルージョンが部分的にしか効かない

- `Environment Depth Mode` を `Fastest` → `Medium` または `Best` に変更
- より精度が上がりますが、パフォーマンスが低下します

### オブジェクトが完全に消える

- オクルージョンが強すぎる可能性
- `Occlusion Preference Mode` を `PreferEnvironmentOcclusion` → `NoOcclusion` に一時的に変更して確認

### 実機でのみ動作しない

1. ビルド設定を確認:
   - `Player Settings > Other Settings > Camera Usage Description` が設定されているか
   - `Target minimum iOS Version` が 14.0以降か

2. デバイスの権限:
   - カメラへのアクセスが許可されているか

## デバッグ用の簡易テスト

オクルージョンが正常に動作しているか確認するには:

1. 実機で平面を検知
2. アバターを配置
3. デバイスを動かして、物理的な壁や物体の後ろにアバターを移動
4. アバターが壁に隠れれば成功

## ログから問題を特定

実機で取得したログを確認してください。以下の情報を含めると問題解決しやすくなります:

- `subsystem=` の値
- `running=` の値
- `EnvDepth:` の値
- `Preference:` の値
- デバイス名とiOSバージョン
