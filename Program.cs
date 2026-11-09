// 윈도우 화면 위에 투명 판서창과 독립 도구막대를 표시하는 판서 엔진입니다.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EpicPen;

public enum DrawTool { Mouse, Pen, Highlighter, Eraser, Line, Rectangle, Ellipse }

public sealed class AppEntry : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new AppEntry { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var overlay = new OverlayWindow();
        app.MainWindow = overlay;
        overlay.Show();
        // 판서창의 소유 창으로 두어야 판서창을 눌러도 도구막대가 그 아래로 가려지지 않습니다.
        var toolbar = new ToolbarWindow(overlay) { Owner = overlay };
        toolbar.Show();
        app.Run();
    }
}

public sealed class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    // 알파 0 픽셀은 윈도우가 클릭을 아래 창으로 넘겨 버리므로, 눈에 안 보이는 알파 1 배경을 깝니다.
    private static readonly Brush HitBackground = CreateHitBackground();

    private readonly InkCanvas ink = new();
    private readonly Canvas shapes = new();
    private readonly List<object> history = new();
    private Point? origin;
    private Shape? preview;
    private double thickness = 4;

    public DrawTool Tool { get; private set; } = DrawTool.Mouse;
    public Color PenColor { get; private set; } = Colors.Red;
    public double Thickness => thickness;
    public event Action? ToolStateChanged;

    public OverlayWindow()
    {
        Title = "Epic Pen - Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        var root = new Grid { Background = HitBackground };
        ink.Background = HitBackground;
        shapes.IsHitTestVisible = false;
        root.Children.Add(ink);
        root.Children.Add(shapes);
        Content = root;

        ink.EditingMode = InkCanvasEditingMode.None;
        ink.StrokeCollected += (_, e) => history.Add(e.Stroke);
        ink.MouseLeftButtonDown += StartShape;
        ink.MouseMove += MoveShape;
        ink.MouseLeftButtonUp += EndShape;
        ink.LostMouseCapture += (_, _) => FinishShape();
        root.PreviewMouseLeftButtonDown += (_, e) => EraseShapes(e);
        root.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) EraseShapes(e); };
        SourceInitialized += (_, _) => SetClickThrough(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetTool(DrawTool.Mouse); };
        ApplyDrawingAttributes();
    }

    private static Brush CreateHitBackground()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var old = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        var flags = enabled ? old | WsExTransparent | WsExLayered : (old & ~WsExTransparent) | WsExLayered;
        SetWindowLongPtr(handle, GwlExstyle, new IntPtr(flags));
    }

    public void SetTool(DrawTool next)
    {
        FinishShape();
        Tool = next;
        ink.EditingMode = next switch
        {
            DrawTool.Pen or DrawTool.Highlighter => InkCanvasEditingMode.Ink,
            DrawTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };
        ApplyDrawingAttributes();
        SetClickThrough(next == DrawTool.Mouse);
        if (next != DrawTool.Mouse) Activate();
        ToolStateChanged?.Invoke();
    }

    public void SetColor(Color value)
    {
        PenColor = value;
        if (Tool is DrawTool.Mouse or DrawTool.Eraser) SetTool(DrawTool.Pen);
        else { ApplyDrawingAttributes(); ToolStateChanged?.Invoke(); }
    }

    public void SetThickness(double value)
    {
        thickness = value;
        ApplyDrawingAttributes();
        ToolStateChanged?.Invoke();
    }

    private void ApplyDrawingAttributes()
    {
        var highlighter = Tool == DrawTool.Highlighter;
        var size = highlighter ? thickness * 4 : thickness;
        ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = PenColor,
            Width = size,
            Height = size,
            IsHighlighter = highlighter,
            FitToCurve = true,
            IgnorePressure = true
        };
    }

    private bool IsShapeTool => Tool is DrawTool.Line or DrawTool.Rectangle or DrawTool.Ellipse;

    private void StartShape(object sender, MouseButtonEventArgs e)
    {
        if (!IsShapeTool) return;
        origin = e.GetPosition(shapes);
        preview = Tool switch
        {
            DrawTool.Line => new Line(),
            DrawTool.Rectangle => new Rectangle(),
            _ => new Ellipse()
        };
        preview.Stroke = new SolidColorBrush(PenColor);
        preview.StrokeThickness = thickness;
        preview.StrokeStartLineCap = PenLineCap.Round;
        preview.StrokeEndLineCap = PenLineCap.Round;
        preview.IsHitTestVisible = false;
        shapes.Children.Add(preview);
        ink.CaptureMouse();
        UpdateShape(e.GetPosition(shapes));
        e.Handled = true;
    }

    private void MoveShape(object sender, MouseEventArgs e)
    {
        if (origin.HasValue && preview != null) UpdateShape(e.GetPosition(shapes));
    }

    private void EndShape(object sender, MouseButtonEventArgs e)
    {
        if (origin == null || preview == null) return;
        UpdateShape(e.GetPosition(shapes));
        FinishShape();
        e.Handled = true;
    }

    // 도형 그리기를 마무리합니다. 너무 작은 도형(단순 클릭)은 버립니다.
    private void FinishShape()
    {
        if (preview == null) return;
        var shape = preview;
        var start = origin;
        preview = null;
        origin = null;
        if (ink.IsMouseCaptured) ink.ReleaseMouseCapture();
        var tiny = shape is Line l
            ? Math.Abs(l.X2 - l.X1) < 2 && Math.Abs(l.Y2 - l.Y1) < 2
            : shape.Width < 2 && shape.Height < 2;
        if (start == null || tiny) shapes.Children.Remove(shape);
        else history.Add(shape);
    }

    private void UpdateShape(Point end)
    {
        if (origin is not Point start || preview == null) return;
        if (preview is Line line)
        {
            line.X1 = start.X; line.Y1 = start.Y;
            line.X2 = end.X; line.Y2 = end.Y;
        }
        else
        {
            Canvas.SetLeft(preview, Math.Min(start.X, end.X));
            Canvas.SetTop(preview, Math.Min(start.Y, end.Y));
            preview.Width = Math.Abs(end.X - start.X);
            preview.Height = Math.Abs(end.Y - start.Y);
        }
    }

    // 지우개는 펜 획(InkCanvas가 처리)뿐 아니라 직선·사각형·원도 닿으면 지웁니다.
    private void EraseShapes(MouseEventArgs e)
    {
        if (Tool != DrawTool.Eraser) return;
        var point = e.GetPosition(shapes);
        for (var i = shapes.Children.Count - 1; i >= 0; i--)
        {
            if (shapes.Children[i] is not Shape shape) continue;
            var local = shapes.TranslatePoint(point, shape);
            var pen = new Pen(Brushes.Black, shape.StrokeThickness + 12);
            if (shape.RenderedGeometry.StrokeContains(pen, local)) shapes.Children.RemoveAt(i);
        }
    }

    // 펜 획과 도형을 그린 순서대로 하나씩 되돌립니다. 지우개로 이미 지운 것은 건너뜁니다.
    public void Undo()
    {
        FinishShape();
        while (history.Count > 0)
        {
            var last = history[^1];
            history.RemoveAt(history.Count - 1);
            if (last is Stroke stroke && ink.Strokes.Contains(stroke))
            {
                ink.Strokes.Remove(stroke);
                return;
            }
            if (last is UIElement element && shapes.Children.Contains(element))
            {
                shapes.Children.Remove(element);
                return;
            }
        }
    }

    public void Clear()
    {
        FinishShape();
        ink.Strokes.Clear();
        shapes.Children.Clear();
        history.Clear();
    }
}

public sealed class ToolbarWindow : Window
{
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(52, 58, 74));
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(160, 168, 186));

    private readonly OverlayWindow overlay;
    private readonly StackPanel panel = new() { Margin = new Thickness(6) };
    private readonly Dictionary<DrawTool, Button> toolButtons = new();
    private readonly List<(Button Button, Color Color)> colorButtons = new();
    private readonly List<(Button Button, double Size)> sizeButtons = new();

    public ToolbarWindow(OverlayWindow target)
    {
        overlay = target;
        Title = "Epic Pen";
        Width = 136;
        SizeToContent = SizeToContent.Height;
        Left = SystemParameters.WorkArea.Right - 156;
        Top = SystemParameters.WorkArea.Top + 80;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(30, 35, 48));
        Content = panel;

        AddTool("마우스 (Esc)", DrawTool.Mouse);
        AddTool("펜", DrawTool.Pen);
        AddTool("형광펜", DrawTool.Highlighter);
        AddTool("지우개", DrawTool.Eraser);
        AddTool("직선", DrawTool.Line);
        AddTool("사각형", DrawTool.Rectangle);
        AddTool("원", DrawTool.Ellipse);

        AddLabel("색");
        var colors = new WrapPanel();
        foreach (var (name, color) in new[] { ("빨강", Colors.Red), ("파랑", Colors.DodgerBlue), ("노랑", Colors.Gold), ("초록", Colors.LimeGreen), ("검정", Colors.Black), ("흰색", Colors.White) })
        {
            var button = MakeButton("", () => overlay.SetColor(color));
            button.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Width = 32; button.Height = 26;
            button.Margin = new Thickness(2);
            button.Background = new SolidColorBrush(color);
            button.BorderThickness = new Thickness(3);
            colors.Children.Add(button);
            colorButtons.Add((button, color));
        }
        panel.Children.Add(colors);

        AddLabel("굵기");
        var sizes = new WrapPanel();
        foreach (var (label, size) in new[] { ("가늘게", 2.0), ("보통", 4.0), ("굵게", 9.0) })
        {
            var button = MakeButton(label, () => overlay.SetThickness(size));
            button.Width = 50; button.Height = 26;
            button.Margin = new Thickness(2);
            sizes.Children.Add(button);
            sizeButtons.Add((button, size));
        }
        panel.Children.Add(sizes);

        AddLabel("편집");
        panel.Children.Add(MakeButton("되돌리기", overlay.Undo));
        panel.Children.Add(MakeButton("전체 지우기", overlay.Clear));
        panel.Children.Add(MakeButton("종료", Close));

        overlay.ToolStateChanged += Refresh;
        Refresh();
        Closed += (_, _) => overlay.Close();
    }

    private void AddTool(string label, DrawTool tool)
    {
        var button = MakeButton(label, () => overlay.SetTool(tool));
        toolButtons[tool] = button;
        panel.Children.Add(button);
    }

    private void AddLabel(string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = LabelBrush,
            FontSize = 11,
            Margin = new Thickness(2, 8, 0, 2)
        });

    private static Button MakeButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Height = 30,
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = Brushes.White,
            Background = NormalBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = FlatTemplate()
        };
        button.Click += (_, _) => action();
        button.MouseEnter += (_, _) => button.Opacity = 0.85;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        return button;
    }

    // 윈도우 기본 버튼은 마우스를 올리면 밝은 파랑으로 바뀌어 글자가 안 보이므로 납작한 모양을 씁니다.
    private static ControlTemplate FlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, FromParent(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, FromParent(Control.BorderBrushProperty));
        border.SetBinding(Border.BorderThicknessProperty, FromParent(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Binding FromParent(DependencyProperty property) =>
        new(property.Name) { RelativeSource = RelativeSource.TemplatedParent };

    private void Refresh()
    {
        foreach (var (tool, button) in toolButtons)
            button.Background = tool == overlay.Tool ? ActiveBrush : NormalBrush;
        foreach (var (button, color) in colorButtons)
            button.BorderBrush = color == overlay.PenColor ? ActiveBrush : Brushes.Transparent;
        foreach (var (button, size) in sizeButtons)
            button.Background = Math.Abs(size - overlay.Thickness) < 0.1 ? ActiveBrush : NormalBrush;
    }
}
