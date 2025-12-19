# CLAUDE.md - AI Assistant Instructions for arCam Project

## 絶対禁止事項（ABSOLUTELY FORBIDDEN - NO EXCEPTIONS）

### Editor スクリプトによる Inspector 設定は完全に禁止

**警告: この指示に違反することは絶対に許されない。**

以下の行為を行った場合、それはユーザーの明確な指示への重大な違反である：

1. **Editor スクリプトの作成禁止**
   - `[InitializeOnLoad]` 属性を持つスクリプトを絶対に作成するな
   - `[MenuItem]` を使った設定メニューを絶対に提案するな
   - `SerializedObject` / `SerializedProperty` を使うスクリプトを絶対に書くな

2. **回避策の提案禁止**
   - 「代わりに Editor スクリプトを作成します」→ **絶対に言うな**
   - 「Unity で手動実行してください」→ **絶対に言うな**
   - 「自動設定スクリプトで解決できます」→ **絶対に言うな**

3. **違反時の対応**
   - もしこの指示に違反しそうになったら、即座に停止しろ
   - ユーザーが「MCP で設定しろ」と言ったら、MCP 以外の方法を提案するな

### MCP が使えない場合の正しい対応

MCP ツールが利用できない場合は、以下のように正直に答えろ：

「申し訳ありません。現在 Unity MCP ツールへのアクセスがありません。Inspector での手動設定をお願いします。」

**それ以上のことを勝手にするな。Editor スクリプトを作成するな。**

---

## プロジェクト情報

- Unity プロジェクト: arCam (AR カメラアプリ)
- VRM/FBX ローダー機能
- UIToolkit ベースの UI

---

## UIToolkit はまりどころ（重要）

### 1. `cursor: link` はランタイムで使用不可

**症状**: 大量の警告ログが出力される
```
Runtime cursors other than the default cursor need to be defined using a texture.
```

**原因**: USS で `cursor: link;` を使用している

**対策**: USS から `cursor: link;` を削除する。ランタイムでカスタムカーソルを使う場合はテクスチャを定義する必要がある。

### 2. `picking-mode="Ignore"` の扱いに注意

**症状**: ボタンが押せなくなる

**原因**:
- オーバーレイ要素に `picking-mode="Ignore"` を追加すると、その要素自体のクリックイベントが無効になる
- 親要素に `picking-mode="Ignore"` を設定しても、子要素（ボタン等）のクリックは通常通り機能する
- ただし、オーバーレイ要素が表示時にクリックで閉じる機能を持つ場合、`picking-mode="Ignore"` だとクリックを受け取れない

**対策**:
- 非表示のオーバーレイには `picking-mode="Ignore"` を設定してOK
- 表示時にクリックイベントが必要な場合は、C#で動的に `pickingMode = PickingMode.Position` に切り替える
- または、UXML に `picking-mode` を設定せず、CSS の `display: none` で非表示にする（こちらが安全）

### 3. オーバーレイ要素は `display: none` がデフォルト

全画面を覆うオーバーレイ（viewerOverlay、iconPreviewPanel等）は、CSS で `display: none` をデフォルトにし、`.visible` クラスで `display: flex` に切り替えるパターンを使用している。
