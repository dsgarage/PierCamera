---
description: "Unity プロジェクトのビルドとスクリプトコンパイル確認"
---

# Unity ビルド

## スクリプトコンパイル確認（コマンドライン）
```bash
# Unity をバッチモードで起動してコンパイルエラーを検出
/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath /Users/daisuketsukada/Documents/dsgarageUnity/arCam \
  -logFile - 2>&1 | tail -50
```

## 注意事項
- ビルド前に .meta ファイルの整合性を確認
- 新規スクリプト追加時は対応する .meta ファイルが自動生成されるまで Unity を開く必要がある
- Editor フォルダ配下のスクリプトは作成禁止（プロジェクトルール参照）
