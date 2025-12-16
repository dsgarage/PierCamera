using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// Issue #73: UIToolkit用円形プログレスインジケーター
    /// generateVisualContentを使用してカスタムメッシュで円弧を描画
    /// 12時位置から時計回りに進行
    /// </summary>
    public class CircularProgressElement : VisualElement
    {
        // USS class names for styling
        public new class UssClassNames
        {
            public static readonly string ussClassName = "circular-progress";
            public static readonly string activeClassName = "circular-progress--active";
        }

        // Properties
        private float _progress = 0f;
        private float _ringWidth = 3f;
        private float _ringRadius = 0f; // 0 = auto (element size based)
        private Color _progressColor = new Color(0.3f, 0.7f, 1f, 1f); // Light blue
        private Color _backgroundColor = Color.clear; // 背景なし（透明）
        private bool _showBackground = false; // 背景リングを表示するか
        private int _segments = 64; // Number of segments for smooth arc

        /// <summary>
        /// 進捗値 (0.0 〜 1.0)
        /// </summary>
        public float Progress
        {
            get => _progress;
            set
            {
                float newValue = Mathf.Clamp01(value);
                if (!Mathf.Approximately(_progress, newValue))
                {
                    _progress = newValue;
                    MarkDirtyRepaint();

                    // Active class for CSS styling
                    if (_progress > 0f && _progress < 1f)
                    {
                        AddToClassList(UssClassNames.activeClassName);
                    }
                    else
                    {
                        RemoveFromClassList(UssClassNames.activeClassName);
                    }
                }
            }
        }

        /// <summary>
        /// リング幅（ピクセル）
        /// </summary>
        public float RingWidth
        {
            get => _ringWidth;
            set
            {
                if (!Mathf.Approximately(_ringWidth, value))
                {
                    _ringWidth = value;
                    MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// リング半径（ピクセル）。0の場合は要素サイズに基づいて自動計算
        /// </summary>
        public float RingRadius
        {
            get => _ringRadius;
            set
            {
                if (!Mathf.Approximately(_ringRadius, value))
                {
                    _ringRadius = value;
                    MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// プログレス色
        /// </summary>
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                if (_progressColor != value)
                {
                    _progressColor = value;
                    MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// 背景リング色
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// セグメント数（円弧の滑らかさ）
        /// </summary>
        public int Segments
        {
            get => _segments;
            set
            {
                value = Mathf.Max(8, value);
                if (_segments != value)
                {
                    _segments = value;
                    MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// 背景リングを表示するか（デフォルト: false）
        /// </summary>
        public bool ShowBackground
        {
            get => _showBackground;
            set
            {
                if (_showBackground != value)
                {
                    _showBackground = value;
                    MarkDirtyRepaint();
                }
            }
        }

        public CircularProgressElement()
        {
            AddToClassList(UssClassNames.ussClassName);

            // カスタム描画を登録
            generateVisualContent += OnGenerateVisualContent;

            // ピッキングモードを無効化（タッチイベントを透過）
            pickingMode = PickingMode.Ignore;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            // 進捗が0または1の場合は描画しない
            if (_progress <= 0f || _progress >= 1f)
            {
                return;
            }

            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }

            // 中心座標
            float centerX = rect.width / 2f;
            float centerY = rect.height / 2f;

            // 半径を計算（自動または指定値）
            float radius = _ringRadius > 0 ? _ringRadius : (Mathf.Min(rect.width, rect.height) / 2f - _ringWidth / 2f);
            float innerRadius = radius - _ringWidth / 2f;
            float outerRadius = radius + _ringWidth / 2f;

            // 背景リングを描画（オプション）
            if (_showBackground && _backgroundColor.a > 0f)
            {
                DrawRing(mgc, centerX, centerY, innerRadius, outerRadius, 0f, 1f, _backgroundColor);
            }

            // プログレスリングのみを描画（12時位置から時計回り）
            DrawRing(mgc, centerX, centerY, innerRadius, outerRadius, 0f, _progress, _progressColor);
        }

        /// <summary>
        /// リング（円弧）を描画 - ストローク方式
        /// </summary>
        private void DrawRing(MeshGenerationContext mgc, float cx, float cy,
            float innerRadius, float outerRadius, float startProgress, float endProgress, Color color)
        {
            if (endProgress <= startProgress) return;

            var painter = mgc.painter2D;

            // 中心半径でストローク描画（リング幅 = outerRadius - innerRadius）
            float centerRadius = (innerRadius + outerRadius) / 2f;
            float strokeWidth = outerRadius - innerRadius;

            painter.strokeColor = color;
            painter.lineWidth = strokeWidth;
            painter.lineCap = LineCap.Butt; // 端を平らに

            // 角度を計算（12時位置 = -90度から時計回り）
            float startAngle = -90f + (startProgress * 360f);
            float endAngle = -90f + (endProgress * 360f);

            // 円弧をストロークで描画
            painter.BeginPath();
            painter.Arc(new Vector2(cx, cy), centerRadius, startAngle, endAngle);
            painter.Stroke();
        }
    }

    /// <summary>
    /// CircularProgressElementのUSS用カスタムスタイル
    /// </summary>
    public static class CircularProgressElementStyles
    {
        /// <summary>
        /// デフォルトのインラインスタイルを適用
        /// </summary>
        public static void ApplyDefaultStyle(CircularProgressElement element, float size = 35f)
        {
            element.style.width = size;
            element.style.height = size;
            element.style.position = Position.Absolute;
        }

        /// <summary>
        /// ターゲット要素を中心にオーバーレイ配置
        /// </summary>
        public static void PositionOverTarget(CircularProgressElement element, VisualElement target, float padding = 4f)
        {
            if (target == null) return;

            var targetBounds = target.worldBound;
            float size = Mathf.Max(targetBounds.width, targetBounds.height) + padding * 2;

            element.style.width = size;
            element.style.height = size;
            element.style.left = targetBounds.x - padding;
            element.style.top = targetBounds.y - padding;
        }
    }
}
