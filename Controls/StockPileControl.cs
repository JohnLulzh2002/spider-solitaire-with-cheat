using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SpiderSolitaireWithCheat.Controls;

/// <summary>右下角待发牌堆：显示剩余发牌次数，点击发牌。</summary>
public class StockPileControl : Border
{
    private readonly TextBlock _count = new()
    {
        Text = string.Empty,
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public event EventHandler<MouseButtonEventArgs>? StockClicked;

    public StockPileControl()
    {
        Cursor = Cursors.Hand;
        ToolTip = "点击发牌 (D)";
        var design = CardControl.BuildBackDesign();
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "发牌",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(_count);
        panel.Margin = new Thickness(0, 30, 0, 0);

        if (design.Child is Border inner && inner.Child is TextBlock ornament)
        {
            ornament.Text = string.Empty; // 隐藏原有装饰，改放发牌文字
        }

        var host = new Grid { Width = 100, Height = 140 };
        host.Children.Add(design);
        host.Children.Add(panel);

        Child = new Viewbox { Child = host };
        MouseLeftButtonDown += (_, e) => StockClicked?.Invoke(this, e);
    }

    public void Update(int deals)
    {
        _count.Text = deals > 0 ? $"× {deals}" : "已空";
        Opacity = deals > 0 ? 1.0 : 0.45;
    }

    public void SetGlow(bool on)
    {
        if (!on)
        {
            Effect = null;
            return;
        }

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
    }
}
