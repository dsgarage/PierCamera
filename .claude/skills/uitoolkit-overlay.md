---
description: "UIToolkit オーバーレイの表示/非表示パターン実装"
---

# UIToolkit オーバーレイ実装パターン

## USS（スタイル）
```css
.overlay {
    display: none;
    position: absolute;
    width: 100%;
    height: 100%;
}
.overlay.visible {
    display: flex;
}
```

## C#（切替ロジック）
```csharp
// 表示
overlay.AddToClassList("visible");
overlay.pickingMode = PickingMode.Position;

// 非表示
overlay.RemoveFromClassList("visible");
overlay.pickingMode = PickingMode.Ignore;
```

## 注意事項
- `cursor: link;` は USS で使用しない（ランタイム警告の原因）
- 非表示時は `picking-mode="Ignore"` で背面要素へのクリックを通す
- 表示時にクリックで閉じる機能が必要な場合は `PickingMode.Position` に切替
