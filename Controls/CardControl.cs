using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SpiderSolitaireWithCheat.Models;

namespace SpiderSolitaireWithCheat.Controls;

/// <summary>
/// 单张牌的视觉控件。设计尺寸 100x140，通过 Viewbox 随外层 Width/Height 缩放。
/// </summary>
public class CardControl : Grid
{
    // 共享画刷（未冻结），主题切换时就地改 Color 即可全局生效
    private static readonly Brush FaceBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFE, 0xF6));
    private static readonly Brush FaceBorder = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly Brush RedFg = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
    private static readonly Brush DarkFg = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));

    /// <summary>应用深色/浅色主题到牌面（深色牌面配浅色文字）。</summary>
    internal static void ApplyTheme(bool dark)
    {
        ((SolidColorBrush)FaceBg).Color = dark ? Color.FromRgb(0x26, 0x31, 0x2B) : Color.FromRgb(0xFF, 0xFE, 0xF6);
        ((SolidColorBrush)FaceBorder).Color = dark ? Color.FromRgb(0x4E, 0x5C, 0x54) : Color.FromRgb(0x8A, 0x8A, 0x8A);
        ((SolidColorBrush)RedFg).Color = dark ? Color.FromRgb(0xFF, 0x6B, 0x7E) : Color.FromRgb(0xC8, 0x10, 0x2E);
        ((SolidColorBrush)DarkFg).Color = dark ? Color.FromRgb(0xE4, 0xED, 0xE7) : Color.FromRgb(0x20, 0x20, 0x20);
    }

    public Card Card { get; }

    private readonly UIElement _face;
    private readonly UIElement _back;

    // 左上角标元素引用，用于拥挤时动态调整（紧贴上缘 + 缩小字体）
    private StackPanel? _corner;
    private TextBlock? _rank;
    private TextBlock? _suit;
    private Border? _faceBorder;

    public event EventHandler<MouseButtonEventArgs>? CardPressed;

    public CardControl(Card card)
    {
        Card = card;
        Cursor = Cursors.Hand;
        _back = new Viewbox { Child = BuildBackDesign() };
        _face = new Viewbox { Child = BuildFaceDesign() };
        Children.Add(_back);
        Children.Add(_face);
        MouseLeftButtonDown += (_, e) => CardPressed?.Invoke(this, e);
        Refresh();
    }

    /// <summary>
    /// 设置被下一张牌覆盖后的可见条带比例（0..1）。拥挤时角标紧贴上缘并缩小字体；
    /// 字体设下限 15（设计单位），再小就不再缩小。
    /// </summary>
    public void SetStrip(double ratio)
    {
        if (_corner is null || _rank is null || _suit is null)
        {
            return;
        }

        double units = 140 * Math.Clamp(ratio, 0, 1);
        if (units >= 34)
        {
            // 正常间距：默认角标样式
            _corner.Margin = new Thickness(8, 6, 0, 0);
            _rank.FontSize = 24;
            _suit.FontSize = 17;
            _suit.Margin = new Thickness(0, -3, 0, 0);
        }
        else
        {
            // 拥挤：紧贴上缘，字体按可见高度比例缩小（下限 15）
            _corner.Margin = new Thickness(8, 1, 0, 0);
            double font = Math.Clamp(units * 0.7, 15, 19);
            _rank.FontSize = font;
            _suit.FontSize = font * 0.7;
            _suit.Margin = new Thickness(0, -2, 0, 0);
        }
    }

    public void Refresh()
    {
        _face.Visibility = Card.FaceUp ? Visibility.Visible : Visibility.Collapsed;
        _back.Visibility = Card.FaceUp ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetGlow(bool on)
    {
        if (!on)
        {
            Effect = null;
            if (_faceBorder is not null)
            {
                _faceBorder.BorderBrush = FaceBorder;
                _faceBorder.BorderThickness = new Thickness(1.4);
            }

            return;
        }

        // 加色光晕在白色牌面上不可见，因此同时把牌边框直接染成提示色
        var fx = new DropShadowEffect
        {
            Color = Color.FromRgb(0x35, 0xC4, 0xFF),
            BlurRadius = 34,
            ShadowDepth = 0,
            Opacity = 1,
        };
        fx.BeginAnimation(
            DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
        Effect = fx;
        if (_faceBorder is not null)
        {
            _faceBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xC4, 0xFF));
            _faceBorder.BorderThickness = new Thickness(3);
        }
    }

    /// <summary>牌面设计（100x140），供 CardControl 与完成序列展示共用。</summary>
    internal Viewbox BuildFaceDesign()
    {
        var fg = Card.IsRed ? RedFg : DarkFg;
        var grid = new Grid { Width = 100, Height = 140 };

        var corner = MakeCorner(fg, Card.RankText, Card.Glyph);
        _corner = corner;
        _rank = (TextBlock)corner.Children[0];
        _suit = (TextBlock)corner.Children[1];
        corner.HorizontalAlignment = HorizontalAlignment.Left;
        corner.VerticalAlignment = VerticalAlignment.Top;
        corner.Margin = new Thickness(8, 6, 0, 0);
        grid.Children.Add(corner);

        var corner2 = MakeCorner(fg, Card.RankText, Card.Glyph);
        corner2.HorizontalAlignment = HorizontalAlignment.Right;
        corner2.VerticalAlignment = VerticalAlignment.Bottom;
        corner2.Margin = new Thickness(0, 0, 8, 6);
        corner2.RenderTransformOrigin = new Point(0.5, 0.5);
        corner2.RenderTransform = new RotateTransform(180);
        grid.Children.Add(corner2);

        grid.Children.Add(new TextBlock
        {
            Text = Card.Glyph.ToString(),
            FontSize = 58,
            Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });

        return new Viewbox
        {
            Child = _faceBorder = new Border
            {
                Width = 100,
                Height = 140,
                CornerRadius = new CornerRadius(9),
                Background = FaceBg,
                BorderBrush = FaceBorder,
                BorderThickness = new Thickness(1.4),
                Child = grid,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 1.5,
                    Opacity = 0.4,
                },
            },
        };
    }

    private static StackPanel MakeCorner(Brush fg, string rankText, char glyph)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = rankText,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = fg,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = glyph.ToString(),
            FontSize = 17,
            Foreground = fg,
            Margin = new Thickness(0, -3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return panel;
    }

    /// <summary>牌背设计（100x140），CardControl 与 StockPileControl 共用。</summary>
    internal static Border BuildBackDesign()
    {
        var inner = new Border
        {
            Margin = new Thickness(7),
            CornerRadius = new CornerRadius(5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.2),
            Child = new TextBlock
            {
                Text = "\u2756",
                FontSize = 34,
                Foreground = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        return new Border
        {
            Width = 100,
            Height = 140,
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x1F, 0x3D)),
            BorderThickness = new Thickness(1.4),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x2E, 0x59, 0x9E), 0),
                    new GradientStop(Color.FromRgb(0x16, 0x30, 0x5C), 1),
                },
            },
            Child = inner,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 5,
                ShadowDepth = 1.5,
                Opacity = 0.4,
            },
        };
    }
}
