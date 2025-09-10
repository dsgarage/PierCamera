
これらの .puml ファイルは PlantUML でレンダリングできます。

1) App_UI_Flow_Activity.puml : アプリ全体のフロー（アクティビティ図）
2) App_UI_PhotoScreen_Salt.puml : ARフォト画面のUIパーツ図（Salt）
3) App_UI_Sequence_Capture.puml : 撮影〜編集〜投稿のシークエンス図

ローカルでのレンダリング例（Java版 PlantUML を導入済みの場合）:
  plantuml App_UI_Flow_Activity.puml
  plantuml App_UI_PhotoScreen_Salt.puml
  plantuml App_UI_Sequence_Capture.puml

VS Code の PlantUML 拡張でもプレビュー可能です。
