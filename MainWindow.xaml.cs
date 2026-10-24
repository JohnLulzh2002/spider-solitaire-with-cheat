using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpiderSolitaireWithCheat.Controls;
using SpiderSolitaireWithCheat.Models;
using SpiderSolitaireWithCheat.Services;

namespace SpiderSolitaireWithCheat;

/// <summary>
/// 主窗口：负责渲染布局、拖拽交互、发牌动画与作弊功能。
/// </summary>
public partial class MainWindow : Window
{
    private GameState _game = null!;

    private readonly List<CardControl>[] _cols = new List<CardControl>[GameState.ColumnCount];
    private readonly List<Rectangle> _emptySlots = new();
    private readonly List<Rectangle> _foundationSlots = new();
    private readonly Canvas _cardHost = new();
    private readonly Canvas _foundationHost = new();
    private readonly Stack<GameState> _undo = new();
    private readonly List<CardControl> _glowing = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _glowTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private TimeSpan _elapsed;
    private StockPileControl _stock = null!;
    private DragInfo? _drag;
    private bool _forceMode;
    private bool _busy; // 组合动画进行中，屏蔽输入避免状态错乱

    // 提示轮换
    private List<Suggestion> _hints = new();
    private int _hintIndex;
    private string _hintKey = "";
    private Rectangle? _glowSlot;

    // 主题
    private bool _darkTheme;
    private SolidColorBrush _slotStroke = null!;
    private SolidColorBrush _slotFill = null!;

    // 布局度量
    private double _cardW, _cardH, _gap, _margin;

    public MainWindow()
    {
        InitializeComponent();
        SoundFx.PreloadAll();
        _overlayTimer.Tick += (_, _) => HideOverlay();

        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            _cols[i] = new List<CardControl>();
        }

        // z 顺序：先加占位槽，再加牌容器
        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            var r = MakeSlot();
            _emptySlots.Add(r);
            TableauCanvas.Children.Add(r);
        }

        for (int i = 0; i < GameState.TotalSequences; i++)
        {
            var r = MakeSlot();
            _foundationSlots.Add(r);
            BottomCanvas.Children.Add(r);
        }

        TableauCanvas.Children.Add(_cardHost);
        BottomCanvas.Children.Add(_foundationHost);

        _timer.Tick += (_, _) =>
        {
            if (_game is { IsWon: false })
            {
                _elapsed += TimeSpan.FromSeconds(1);
                UpdateStatus();
            }
        };
        _glowTimer.Tick += (_, _) => ClearGlow();

        SizeChanged += (_, _) => LayoutAll();
        Loaded += (_, _) => NewGame();
        SourceInitialized += (_, _) => UpdateTitleBarTheme();
        ApplyTheme(IsSystemDark());
    }

    // ---------- 主题 ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>让系统标题栏/窗口边框跟随深色模式（DWMWA_USE_IMMERSIVE_DARK_MODE）。</summary>
    private void UpdateTitleBarTheme()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int on = _darkTheme ? 1 : 0;
            // 属性 20 适用于 Win10 20H1+ / Win11；旧版本回退到 19
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // DWM 不可用时忽略，保持默认标题栏
        }
    }

    /// <summary>读取 Windows 应用主题（注册表），系统为深色时返回 true；读取失败默认深色。</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>应用深色/浅色主题：刷新窗口级画刷资源、牌槽描边与牌面配色。</summary>
    private void ApplyTheme(bool dark)
    {
        _darkTheme = dark;
        void Set(string key, byte a, byte r, byte g, byte b) =>
            Resources[key] = new SolidColorBrush(Color.FromArgb(a, r, g, b));

        CardControl.ApplyTheme(dark);

        if (dark)
        {
            Set("TableBrush", 0xFF, 0x08, 0x1F, 0x11);
            Set("BarBrush", 0xF2, 0x07, 0x14, 0x0B);
            Set("BottomBrush", 0xD8, 0x05, 0x0F, 0x09);
            Set("BarFgBrush", 0xFF, 0xD9, 0xE8, 0xDD);
            Set("StatFgBrush", 0xFF, 0xC4, 0xDA, 0xCB);
            Set("BtnBgBrush", 0xFF, 0x1D, 0x3A, 0x27);
            Set("BtnHoverBrush", 0xFF, 0x2A, 0x53, 0x3A);
            Set("BtnPressedBrush", 0xFF, 0x13, 0x2A, 0x1B);
            Set("BtnActiveBrush", 0xFF, 0x8A, 0x5A, 0x18);
            Set("BtnBorderBrush", 0x33, 0xFF, 0xFF, 0xFF);
            Set("OverlayBrush", 0xF0, 0x0A, 0x1F, 0x12);
            Set("OverlayFgBrush", 0xE6, 0xD9, 0xE8, 0xDD);
        }
        else
        {
            Set("TableBrush", 0xFF, 0x0B, 0x4A, 0x24);
            Set("BarBrush", 0xE6, 0x0D, 0x2B, 0x17);
            Set("BottomBrush", 0xC6, 0x09, 0x2F, 0x16);
            Set("BarFgBrush", 0xFF, 0xE8, 0xF5, 0xEC);
            Set("StatFgBrush", 0xFF, 0xCF, 0xE8, 0xD5);
            Set("BtnBgBrush", 0xFF, 0x2E, 0x5C, 0x3A);
            Set("BtnHoverBrush", 0xFF, 0x3A, 0x7A, 0x4C);
            Set("BtnPressedBrush", 0xFF, 0x24, 0x47, 0x2E);
            Set("BtnActiveBrush", 0xFF, 0xB7, 0x79, 0x1F);
            Set("BtnBorderBrush", 0x55, 0xFF, 0xFF, 0xFF);
            Set("OverlayBrush", 0xD9, 0x12, 0x2A, 0x1A);
            Set("OverlayFgBrush", 0xE6, 0xE8, 0xF5, 0xEC);
        }

        _slotStroke = new SolidColorBrush(dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        _slotFill = new SolidColorBrush(dark
            ? Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        foreach (var s in _emptySlots.Concat(_foundationSlots))
        {
            s.Stroke = _slotStroke;
            s.Fill = _slotFill;
        }

        ClearGlow();
        TglTheme.IsChecked = dark;
        TglTheme.Content = dark ? "🌙" : "☀";
        UpdateTitleBarTheme();
    }

    private void OnThemeToggled(object sender, RoutedEventArgs e)
    {
        ApplyTheme(TglTheme.IsChecked == true);
        SoundFx.Play("flip");
    }

    private static Rectangle MakeSlot() => new()
    {
        RadiusX = 9,
        RadiusY = 9,
        Fill = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
        Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1.4,
        StrokeDashArray = new DoubleCollection { 3, 2 },
        IsHitTestVisible = false,
    };

    // ---------- 新游戏 / 刷新 ----------

    private void NewGame()
    {
        if (_busy)
        {
            return;
        }

        int suits = CmbDifficulty.SelectedIndex switch { 0 => 1, 1 => 2, _ => 4 };
        _game = GameState.DealNew(suits, Random.Shared);
        _undo.Clear();
        _elapsed = TimeSpan.Zero;
        HideOverlay();
        ClearGlow();
        _timer.Start();
        RefreshAll();
        SoundFx.Play("deal");
    }

    // ---------- 窗口内浮层提示 ----------

    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    /// <summary>显示半透明浮层。autoHide 为 null 时需点击关闭；actionText 非空时显示操作按钮。</summary>
    private void ShowOverlay(string title, string body, TimeSpan? autoHide = null, string? actionText = null)
    {
        OverlayTitle.Text = title;
        OverlayBody.Text = body;
        if (actionText is null)
        {
            BtnOverlayAction.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnOverlayAction.Content = actionText;
            BtnOverlayAction.Visibility = Visibility.Visible;
        }

        _overlayTimer.Stop();
        if (autoHide is { } t)
        {
            _overlayTimer.Interval = t;
            _overlayTimer.Start();
        }

        OverlayRoot.Visibility = Visibility.Visible;
        OverlayRoot.Opacity = 0;
        OverlayRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    private void HideOverlay()
    {
        _overlayTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        anim.Completed += (_, _) => OverlayRoot.Visibility = Visibility.Collapsed;
        OverlayRoot.BeginAnimation(OpacityProperty, anim);
    }

    private void OnOverlayDismiss(object sender, MouseButtonEventArgs e) => HideOverlay();

    private void OnOverlayAction(object sender, RoutedEventArgs e)
    {
        HideOverlay();
        NewGame();
    }

    private void RefreshAll()
    {
        _cardHost.Children.Clear();
        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            _cols[i].Clear();
            foreach (var card in _game.Tableau[i])
            {
                var cc = new CardControl(card) { ToolTip = card.FaceUp ? $"{card.Glyph} {card.RankText}" : null };
                cc.CardPressed += OnCardPressed;
                cc.MouseMove += OnCardMouseMove;
                cc.MouseLeftButtonUp += OnCardMouseUp;
                cc.LostMouseCapture += OnCardLostCapture;
                _cols[i].Add(cc);
                _cardHost.Children.Add(cc);
            }
        }

        _foundationHost.Children.Clear();
        for (int i = 0; i < _game.Foundations.Count; i++)
        {
            var cc = new CardControl(new Card(_game.Foundations[i], 13, true))
            {
                IsHitTestVisible = false,
            };
            _foundationHost.Children.Add(cc);
        }

        if (_stock is not null)
        {
            BottomCanvas.Children.Remove(_stock);
        }

        _stock = new StockPileControl();
        _stock.StockClicked += OnStockClicked;
        BottomCanvas.Children.Add(_stock);

        LayoutAll();
        UpdateStatus();
    }

    private void LayoutAll()
    {
        if (_game is null || TableauCanvas.ActualWidth <= 0 || TableauCanvas.ActualHeight <= 0)
        {
            return;
        }

        double w = TableauCanvas.ActualWidth;
        double h = TableauCanvas.ActualHeight;
        _gap = Math.Max(6, w * 0.006);
        _margin = 2;
        _cardW = (w - 2 * _margin - 9 * _gap) / GameState.ColumnCount;
        _cardH = _cardW * 1.4;
        double maxCardH = (h - 8) / 3.4;
        if (_cardH > maxCardH)
        {
            _cardH = maxCardH;
            _cardW = _cardH / 1.4;
        }

        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            double x = XOf(i);
            var slot = _emptySlots[i];
            slot.Width = _cardW;
            slot.Height = _cardH;
            Canvas.SetLeft(slot, x);
            Canvas.SetTop(slot, 4);
            LayoutColumn(i, x);
        }

        LayoutBottom();
        UpdateStatus();
    }

    private double XOf(int col) => _margin + col * (_cardW + _gap);

    /// <summary>布局一列。count ≥ 0 时仅重排前 count 张（拖拽拾起后剩余牌堆松弛反馈）。返回下一张牌应处的 y。</summary>
    private double LayoutColumn(int col, double x, int count = -1)
    {
        var pile = _game.Tableau[col];
        var ctrls = _cols[col];
        int total = count < 0 ? pile.Count : Math.Min(count, pile.Count);
        int down = 0;
        for (int j = 0; j < total; j++)
        {
            if (!pile[j].FaceUp)
            {
                down++;
            }
        }

        int up = total - down;
        int ups = Math.Max(0, up - 1);
        double fd = _cardH * 0.16;
        double fu = _cardH * 0.26;
        double fdMin = _cardH * 0.07;
        double fuMin = _cardH * 0.17;
        double avail = TableauCanvas.ActualHeight - _cardH - 6;
        double need = down * fd + ups * fu;
        if (need > avail && need > 0)
        {
            // 列太长时分两级压缩：先压暗牌间距（无信息量），
            // 再把明牌间距压到下限（仍露出点数花色），最后才等比压缩。
            double take = Math.Min(down * (fd - fdMin), need - avail);
            if (take > 0 && down > 0)
            {
                fd -= take / down;
                need -= take;
            }

            take = Math.Min(ups * (fu - fuMin), need - avail);
            if (take > 0 && ups > 0)
            {
                fu -= take / ups;
                need -= take;
            }

            if (need > avail && need > 0)
            {
                double k = avail / need;
                fd *= k;
                fu *= k;
            }
        }

        double y = 4;
        for (int j = 0; j < ctrls.Count && j < total; j++)
        {
            ctrls[j].Width = _cardW;
            ctrls[j].Height = _cardH;
            Canvas.SetLeft(ctrls[j], x);
            Canvas.SetTop(ctrls[j], y);
            Panel.SetZIndex(ctrls[j], 0); // 复位拖拽残留的置顶 Z 序
            // 按该牌被覆盖后的可见条带比例调整角标（拥挤时紧贴上缘并缩小字体）
            ctrls[j].SetStrip(j == total - 1 || !pile[j].FaceUp ? 1.0 : fu / _cardH);
            ctrls[j].Refresh();
            y += pile[j].FaceUp ? fu : fd;
        }

        return y;
    }

    private void LayoutBottom()
    {
        double bw = BottomCanvas.ActualWidth;
        double bh = BottomCanvas.ActualHeight;
        double scale = Math.Min(0.78, 118 / _cardH);
        double fw = _cardW * scale;
        double fh = _cardH * scale;
        double y = Math.Max(2, (bh - fh) / 2);

        for (int i = 0; i < _foundationSlots.Count; i++)
        {
            var s = _foundationSlots[i];
            s.Width = fw;
            s.Height = fh;
            Canvas.SetLeft(s, 8 + i * (fw + 7));
            Canvas.SetTop(s, y);
        }

        for (int i = 0; i < _foundationHost.Children.Count; i++)
        {
            if (_foundationHost.Children[i] is CardControl cc)
            {
                cc.Width = fw;
                cc.Height = fh;
                Canvas.SetLeft(cc, 8 + i * (fw + 7));
                Canvas.SetTop(cc, y);
            }
        }

        _stock.Width = fw;
        _stock.Height = fh;
        double stockX = Math.Max(bw - fw - 8, 8 + GameState.TotalSequences * (fw + 7));
        Canvas.SetLeft(_stock, stockX);
        Canvas.SetTop(_stock, y);
        _stock.Update(_game.Stock.Count / GameState.StockDealSize);
    }

    private void UpdateStatus()
    {
        TxtScore.Text = $"得分 {_game.Score}";
        TxtMoves.Text = $"步数 {_game.Moves}";
        TxtTime.Text = _elapsed.ToString(@"mm\:ss");
    }

    // ---------- 拖拽交互 ----------

    private (int Col, int Index) Locate(CardControl cc)
    {
        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            int idx = _cols[i].IndexOf(cc);
            if (idx >= 0)
            {
                return (i, idx);
            }
        }

        return (-1, -1);
    }

    private int ColumnAt(double x)
    {
        int best = -1;
        double bestDist = _cardW * 0.85;
        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            double cx = XOf(i) + _cardW / 2;
            double dist = Math.Abs(x - cx);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    /// <summary>当前模式下的拾取校验：强制模式下任意明牌串都可拾取。</summary>
    private bool CanPickUpNow(int col, int index)
    {
        if (!_forceMode)
        {
            return _game.CanPickUp(col, index);
        }

        var pile = _game.Tableau[col];
        if (index < 0 || index >= pile.Count)
        {
            return false;
        }

        for (int i = index; i < pile.Count; i++)
        {
            if (!pile[i].FaceUp)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>当前模式下的落牌校验：强制模式下任意非源列都可落牌。</summary>
    private bool CanDropNow(int fromCol, int index, int toCol) =>
        _forceMode ? fromCol != toCol : _game.CanDrop(fromCol, index, toCol);

    private void OnCardPressed(object? sender, MouseButtonEventArgs e)
    {
        if (_game.IsWon || _busy)
        {
            return;
        }

        var cc = (CardControl)sender!;
        var (col, idx) = Locate(cc);
        if (col < 0)
        {
            return;
        }

        // 双击：自动把这串牌移到最优目标列
        if (e.ClickCount >= 2 && _drag is not { Active: true })
        {
            e.Handled = true;
            if (_drag is { } d)
            {
                d.Anchor.ReleaseMouseCapture();
                _drag = null;
            }

            TryAutoMoveRun(col, idx);
            return;
        }

        if (!cc.Card.FaceUp || !CanPickUpNow(col, idx))
        {
            return;
        }

        var controls = _cols[col].GetRange(idx, _cols[col].Count - idx);
        var lefts = controls.Select(c => Canvas.GetLeft(c)).ToList();
        var tops = controls.Select(c => Canvas.GetTop(c)).ToList();
        _drag = new DragInfo(col, idx, cc, controls, lefts, tops, e.GetPosition(TableauCanvas));
        cc.CaptureMouse();
        e.Handled = true;
    }

    private void OnCardMouseMove(object? sender, MouseEventArgs e)
    {
        if (_drag is null || !ReferenceEquals(sender, _drag.Anchor))
        {
            return;
        }

        var p = e.GetPosition(TableauCanvas);
        double dx = p.X - _drag.Start.X;
        double dy = p.Y - _drag.Start.Y;
        if (!_drag.Active)
        {
            if (dx * dx + dy * dy < 25)
            {
                return; // 未超过拖动阈值
            }

            _drag.Active = true;
            // 源列剩余牌松弛；手中牌串以锚牌原位为基准展开为常规间距，
            // 锚牌（光标下的第一张）不产生任何位移，避免拾起瞬间跳位。
            double handFu = _cardH * 0.26;
            LayoutColumn(_drag.Col, XOf(_drag.Col), _drag.Index);
            double anchorTop = _drag.Tops[0];
            for (int k = 1; k < _drag.Tops.Count; k++)
            {
                _drag.Tops[k] = anchorTop + k * handFu;
                _drag.Controls[k].SetStrip(handFu / _cardH); // 手中牌串间距恢复正常
            }
        }

        for (int k = 0; k < _drag.Controls.Count; k++)
        {
            Canvas.SetLeft(_drag.Controls[k], _drag.Lefts[k] + dx);
            Canvas.SetTop(_drag.Controls[k], _drag.Tops[k] + dy);
            Panel.SetZIndex(_drag.Controls[k], 500 + k);
        }

        e.Handled = true;
    }

    private void OnCardMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_drag is null || !ReferenceEquals(sender, _drag.Anchor))
        {
            return;
        }

        var d = _drag;
        _drag = null;
        d.Anchor.ReleaseMouseCapture();

        if (d.Active)
        {
            var p = e.GetPosition(TableauCanvas);
            int to = ColumnAt(p.X);
            bool ok = to >= 0 && to != d.Col && p.Y > -_cardH * 0.5 && CanDropNow(d.Col, d.Index, to);
            if (ok)
            {
                _undo.Push(_game.Clone());
                if (!_game.MoveCards(d.Col, d.Index, to, _forceMode))
                {
                    _undo.Pop();
                    LayoutColumn(d.Col, XOf(d.Col));
                }
                else
                {
                    SoundFx.Play("place");
                    AfterAction();
                }
            }
            else
            {
                LayoutColumn(d.Col, XOf(d.Col));
            }
        }

        e.Handled = true;
    }

    private void OnCardLostCapture(object? sender, MouseEventArgs e)
    {
        if (_drag is not { } d || !ReferenceEquals(sender, d.Anchor))
        {
            return;
        }

        bool wasActive = d.Active;
        _drag = null;
        if (wasActive)
        {
            LayoutColumn(d.Col, XOf(d.Col));
        }
    }

    // ---------- 动作 ----------

    private void AfterAction()
    {
        var fxs = CaptureCompletedRuns();
        var done = _game.CollectCompletedSequences();
        RefreshAll();
        if (fxs.Count > 0)
        {
            AnimateCompleted(fxs);
        }

        if (_game.IsWon)
        {
            HandleWin();
        }
        else if (done.Count > 0)
        {
            SoundFx.Play("complete");
        }
    }

    private void HandleWin()
    {
        SoundFx.Play("win");
        _timer.Stop();
        ClearGlow();
        ShowOverlay("恭喜获胜！",
            $"用时 {_elapsed:hh\\:mm\\:ss}，步数 {_game.Moves}，得分 {_game.Score}。",
            null, "再来一局");
    }

    private void OnStockClicked(object? sender, MouseButtonEventArgs e) => Deal();

    private void Deal()
    {
        if (_game.IsWon || _busy || _game.Stock.Count < GameState.StockDealSize)
        {
            return;
        }

        if (!_game.CanDeal && !_forceMode)
        {
            SoundFx.Play("error");
            ShowOverlay("无法发牌", "存在空列时不能发牌，请先移动牌填满空列。",
                TimeSpan.FromSeconds(2.5));
            return;
        }

        _undo.Push(_game.Clone());
        _game.DealRow(force: _forceMode);

        // 先呈现发牌后的完整状态，再捕获完成序列（此时发的新牌已有控件）
        RefreshAll();
        var pending = CaptureCompletedRuns();
        if (pending.Count > 0)
        {
            // 先播完发牌动画（凑齐的 A 落位），再收牌并播放起飞动画
            _busy = true;
            AnimateDeal(() =>
            {
                _game.CollectCompletedSequences();
                RefreshAll();
                AnimateCompleted(pending);
                SoundFx.Play("complete");
                _busy = false;
                if (_game.IsWon)
                {
                    HandleWin();
                }
            });
        }
        else
        {
            _game.CollectCompletedSequences();
            RefreshAll();
            AnimateDeal();
        }

        SoundFx.Play("deal");
    }

    private void AnimateDeal(Action? done = null)
    {
        int remaining = 0;
        bool finished = false;
        void Finish()
        {
            if (!finished)
            {
                finished = true;
                done?.Invoke();
            }
        }

        try
        {
            var from = _stock.TransformToAncestor(this).Transform(new Point(0, 0));
            for (int i = 0; i < GameState.ColumnCount; i++)
            {
                if (_cols[i].Count == 0)
                {
                    continue;
                }

                var cc = _cols[i][^1];
                remaining++;
                var to = cc.TransformToAncestor(this).Transform(new Point(0, 0));
                var tt = new TranslateTransform(from.X - to.X, from.Y - to.Y);
                cc.RenderTransform = tt;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var ax = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
                var ay = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
                ax.Completed += (_, _) =>
                {
                    remaining--;
                    if (remaining == 0)
                    {
                        Finish();
                    }
                };
                tt.BeginAnimation(TranslateTransform.XProperty, ax);
                tt.BeginAnimation(TranslateTransform.YProperty, ay);
            }

            if (remaining == 0)
            {
                Finish();
            }
        }
        catch
        {
            // 动画失败不影响游戏
            Finish();
        }
    }

    private sealed record CompleteFx(List<CardControl> Cards, List<Point> Starts);

    /// <summary>收牌前调用：捕获各列顶部完整的 K→A 序列及其控件位置。
    /// 此时 _cols 仍是本次移动前的视觉状态，与 _game.Tableau 长度可能不一致，
    /// 因此按牌对象身份（引用相等）映射到现有控件，而非按列索引取值。</summary>
    private List<CompleteFx> CaptureCompletedRuns()
    {
        var map = new Dictionary<Card, CardControl>();
        for (int i = 0; i < GameState.ColumnCount; i++)
        {
            foreach (var cc in _cols[i])
            {
                map[cc.Card] = cc;
            }
        }

        var result = new List<CompleteFx>();
        for (int col = 0; col < GameState.ColumnCount; col++)
        {
            var pile = _game.Tableau[col];
            int n = pile.Count;
            if (n < 13)
            {
                continue;
            }

            int start = n - 13;
            var suit = pile[start].Suit;
            bool ok = true;
            for (int j = start; j < n; j++)
            {
                var c = pile[j];
                if (!c.FaceUp || c.Suit != suit || c.Rank != 13 - (j - start))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                continue;
            }

            var cards = new List<CardControl>();
            var starts = new List<Point>();
            for (int j = start; j < n; j++)
            {
                if (!map.TryGetValue(pile[j], out var cc))
                {
                    cards.Clear();
                    break;
                }

                cards.Add(cc);
                starts.Add(new Point(Canvas.GetLeft(cc), Canvas.GetTop(cc)));
            }

            if (cards.Count == 13)
            {
                result.Add(new CompleteFx(cards, starts));
            }
        }

        return result;
    }

    /// <summary>RefreshAll 之后调用：让完成序列的 13 张牌依次飞向对应完成位。</summary>
    private void AnimateCompleted(List<CompleteFx> fxs)
    {
        try
        {
            int baseIdx = _game.Foundations.Count - fxs.Count;
            for (int i = 0; i < fxs.Count; i++)
            {
                if (baseIdx + i >= _foundationHost.Children.Count ||
                    _foundationHost.Children[baseIdx + i] is not CardControl target)
                {
                    continue;
                }

                var toWin = target.TransformToAncestor(this).Transform(new Point(0, 0));
                var origin = TableauCanvas.TransformToAncestor(this).Transform(new Point(0, 0));
                var fx = fxs[i];
                for (int k = 0; k < fx.Cards.Count; k++)
                {
                    var cc = fx.Cards[k];
                    var start = fx.Starts[k];
                    cc.IsHitTestVisible = false;
                    Canvas.SetLeft(cc, start.X);
                    Canvas.SetTop(cc, start.Y);
                    Panel.SetZIndex(cc, 900 + k);
                    TableauCanvas.Children.Add(cc);

                    var tt = new TranslateTransform();
                    cc.RenderTransform = tt;
                    double dx = toWin.X - (origin.X + start.X);
                    double dy = toWin.Y - (origin.Y + start.Y);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    var ax = new DoubleAnimation(dx, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = ease,
                        BeginTime = TimeSpan.FromMilliseconds(k * 22),
                    };
                    var ay = new DoubleAnimation(dy, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = ease,
                        BeginTime = TimeSpan.FromMilliseconds(k * 22),
                    };
                    ax.Completed += (_, _) => TableauCanvas.Children.Remove(cc);
                    tt.BeginAnimation(TranslateTransform.XProperty, ax);
                    tt.BeginAnimation(TranslateTransform.YProperty, ay);
                }
            }
        }
        catch
        {
            // 动画失败不影响游戏
        }
    }

    private void TryAutoMoveRun(int col, int idx)
    {
        if (_game.IsWon || !_game.CanPickUp(col, idx))
        {
            return;
        }

        var sug = MoveFinder.FindTargetFor(_game, col, idx);
        if (sug is null)
        {
            return;
        }

        _undo.Push(_game.Clone());
        _game.MoveCards(sug.FromCol, sug.Index, sug.ToCol);
        SoundFx.Play("place");
        AfterAction();
    }

    private void Undo()
    {
        if (_busy || _undo.Count == 0)
        {
            return;
        }

        _game = _undo.Pop();
        ClearGlow();
        RefreshAll();
        SoundFx.Play("undo");
    }

    private void Hint()
    {
        if (_game.IsWon)
        {
            return;
        }

        // 同一棋局内多次按提示：在所有可行走法间轮换；棋局一变则重新搜索
        string key = BoardKey();
        if (key != _hintKey)
        {
            _hintKey = key;
            _hints = MoveFinder.FindAll(_game);
            _hintIndex = 0;
        }
        else
        {
            _hintIndex++;
        }

        if (_hints.Count == 0)
        {
            bool canDealNow = _game.CanDeal || (_forceMode && _game.Stock.Count >= GameState.StockDealSize);
            if (canDealNow)
            {
                ClearGlow();
                _stock.SetGlow(true);
                _glowing.Clear();
                _glowTimer.Start();
            }
            else
            {
                ShowOverlay("提示", "没有可行的移动，请撤销或开始新游戏。",
                    TimeSpan.FromSeconds(2.5));
            }

            return;
        }

        var sug = _hints[_hintIndex % _hints.Count];
        ClearGlow();
        for (int j = sug.Index; j < _cols[sug.FromCol].Count; j++)
        {
            _cols[sug.FromCol][j].SetGlow(true);
            _glowing.Add(_cols[sug.FromCol][j]);
        }

        if (_cols[sug.ToCol].Count > 0)
        {
            var top = _cols[sug.ToCol][^1];
            top.SetGlow(true);
            _glowing.Add(top);
        }
        else
        {
            // 目标是空列时高亮空槽
            var slot = _emptySlots[sug.ToCol];
            slot.Stroke = new SolidColorBrush(Color.FromRgb(0x35, 0xC4, 0xFF));
            slot.StrokeThickness = 2.6;
            _glowSlot = slot;
        }

        _glowTimer.Start();
    }

    /// <summary>棋局签名：用于判断提示轮换期间棋盘是否发生变化。</summary>
    private string BoardKey() => string.Join("|",
        _game.Tableau.Select(p => string.Concat(p.Select(
            c => c.FaceUp ? (char)('a' + (int)c.Suit * 13 + c.Rank - 1) : '#')))) +
        "|" + _game.Stock.Count;

    private void ClearGlow()
    {
        _glowTimer.Stop();
        foreach (var c in _glowing)
        {
            c.SetGlow(false);
        }

        _glowing.Clear();
        if (_glowSlot is { } s)
        {
            s.Stroke = _slotStroke;
            s.StrokeThickness = 1.4;
            _glowSlot = null;
        }

        if (_stock is not null)
        {
            _stock.SetGlow(false);
        }
    }

    private void FlipAllHidden()
    {
        if (_game.IsWon || !_game.Tableau.Any(p => p.Any(c => !c.FaceUp)))
        {
            return;
        }

        _undo.Push(_game.Clone());
        foreach (var pile in _game.Tableau)
        {
            foreach (var c in pile)
            {
                c.FaceUp = true;
            }
        }

        RefreshAll();
        SoundFx.Play("cheat");
    }

    // ---------- 事件入口 ----------

    private void OnNewGame(object sender, RoutedEventArgs e) => NewGame();

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void OnHint(object sender, RoutedEventArgs e) => Hint();

    private void OnFlipHidden(object sender, RoutedEventArgs e) => FlipAllHidden();

    private void OnForceToggled(object sender, RoutedEventArgs e)
    {
        _forceMode = TglForce.IsChecked == true;
        if (_forceMode)
        {
            SoundFx.Play("cheat");
        }
    }

    private void OnSoundToggled(object sender, RoutedEventArgs e)
    {
        SoundFx.Enabled = TglSound.IsChecked == true;
        TglSound.Content = SoundFx.Enabled ? "🔊" : "🔇";
        if (SoundFx.Enabled)
        {
            SoundFx.Play("flip");
        }
    }

    private void OnHelp(object sender, RoutedEventArgs e) =>
        ShowOverlay("规则",
            "· 104 张牌（2 副），把牌按降序叠放；只可整体移动同花色的连续序列。\n" +
            "· 任意花色都可以叠在比它大 1 点的牌上；空列可放任何牌。\n" +
            "· 凑齐同花色 K→A 的 13 张即自动移出，完成 8 组获胜。\n" +
            "· 点击右下角牌堆发牌（每次 10 张，有空列时不能发牌）。\n" +
            "· 拖拽移动，双击自动移动，Ctrl+Z 撤销，H 提示，D 发牌，M 音效开关，T 深色模式。\n\n" +
            "（点击任意处关闭）");

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _game is null || _busy)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                Undo();
                e.Handled = true;
                break;
            case Key.H when Keyboard.Modifiers == ModifierKeys.None:
                Hint();
                e.Handled = true;
                break;
            case Key.P when Keyboard.Modifiers == ModifierKeys.None:
                FlipAllHidden();
                e.Handled = true;
                break;
            case Key.D when Keyboard.Modifiers == ModifierKeys.None:
                Deal();
                e.Handled = true;
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.None:
                TglForce.IsChecked = TglForce.IsChecked != true;
                _forceMode = TglForce.IsChecked == true;
                if (_forceMode)
                {
                    SoundFx.Play("cheat");
                }

                e.Handled = true;
                break;
            case Key.M when Keyboard.Modifiers == ModifierKeys.None:
                TglSound.IsChecked = TglSound.IsChecked != true;
                OnSoundToggled(TglSound, e);
                e.Handled = true;
                break;
            case Key.T when Keyboard.Modifiers == ModifierKeys.None:
                ApplyTheme(!_darkTheme);
                SoundFx.Play("flip");
                e.Handled = true;
                break;
        }
    }

    private sealed class DragInfo
    {
        public int Col { get; }
        public int Index { get; }
        public CardControl Anchor { get; }
        public List<CardControl> Controls { get; }
        public List<double> Lefts { get; }
        public List<double> Tops { get; }
        public Point Start { get; }
        public bool Active { get; set; }

        public DragInfo(int col, int index, CardControl anchor,
            List<CardControl> controls, List<double> lefts, List<double> tops, Point start)
        {
            Col = col;
            Index = index;
            Anchor = anchor;
            Controls = controls;
            Lefts = lefts;
            Tops = tops;
            Start = start;
        }
    }
}
