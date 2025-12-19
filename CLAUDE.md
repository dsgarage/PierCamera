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
