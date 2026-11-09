// 윈도우 화면 위에 투명 판서창과 독립 도구막대를 표시하는 1단계 판서 엔진입니다.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace EpicPen;

public enum DrawTool { Mouse, Pen, Eraser, Line, Rectangle, Ellipse }

public sealed class AppEntry : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new AppEntry { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var overlay = new OverlayWindow();
        app.MainWindow = overlay;
        overlay.Show();
        var toolbar = new ToolbarWindow(overlay);
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

    private readonly InkCanvas ink = new();
    private readonly Canvas shapes = new();
    private readonly List<UIElement> shapeHistory = new();
    private Point? origin;
    private Shape? preview;
    private DrawTool tool = DrawTool.Mouse;
    private Color color = Colors.Red;
    private double thickness = 4;

    public OverlayWindow()
    {
        Title = "Epic Pen - Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        var bounds = SystemParameters.VirtualScreenWidth;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = bounds;
        Height = SystemParameters.VirtualScreenHeight;
        var root = new Grid { Background = Brushes.Transparent };
        ink.Background = Brushes.Transparent;
        shapes.IsHitTestVisible = false;
        root.Children.Add(ink);
        root.Children.Add(shapes);
        Content = root;
        ink.EditingMode = InkCanvasEditingMode.None;
        ink.MouseLeftButtonDown += StartShape;
        ink.MouseMove += MoveShape;
        ink.MouseLeftButtonUp += EndShape;
        SourceInitialized += (_, _) => SetClickThrough(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetTool(DrawTool.Mouse); };
    }

    private void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var old = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        var flags = enabled ? old | WsExTransparent | WsExLayered : (old & ~WsExTransparent) | WsExLayered;
        SetWindowLongPtr(handle, GwlExstyle, new IntPtr(flags));
    }

    public void SetTool(DrawTool next)
    {
        tool = next;
        origin = null;
        if (preview != null) shapes.Children.Remove(preview);
        preview = null;
        ink.ReleaseMouseCapture();
        ink.EditingMode = next switch
        {
            DrawTool.Pen => InkCanvasEditingMode.Ink,
            DrawTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None
        };
        ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color, Width = thickness, Height = thickness,
            FitToCurve = true, IgnorePressure = true
        };
        SetClickThrough(next == DrawTool.Mouse);
    }

    public void SetColor(Color value) { color = value; SetTool(tool); }
    public void SetThickness(double value) { thickness = value; SetTool(tool); }

    private bool IsShapeTool => tool is DrawTool.Line or DrawTool.Rectangle or DrawTool.Ellipse;

    private void StartShape(object sender, MouseButtonEventArgs e)
    {
        if (!IsShapeTool) return;
        origin = e.GetPosition(shapes);
        preview = tool switch
        {
            DrawTool.Line => new Line(),
            DrawTool.Rectangle => new Rectangle(),
            _ => new Ellipse()
        };
        preview.Stroke = new SolidColorBrush(color);
        preview.StrokeThickness = thickness;
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
        shapeHistory.Add(preview);
        preview = null;
        origin = null;
        ink.ReleaseMouseCapture();
        e.Handled = true;
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

    public void Undo()
    {
        if (shapeHistory.Count > 0)
        {
            var last = shapeHistory[^1];
            shapes.Children.Remove(last);
            shapeHistory.RemoveAt(shapeHistory.Count - 1);
        }
        else if (ink.Strokes.Count > 0) ink.Strokes.RemoveAt(ink.Strokes.Count - 1);
    }

    public void Clear()
    {
        ink.Strokes.Clear();
        shapes.Children.Clear();
        shapeHistory.Clear();
        origin = null;
        preview = null;
    }
}

public sealed class ToolbarWindow : Window
{
    private readonly OverlayWindow overlay;
    private readonly StackPanel buttons = new() { Orientation = Orientation.Vertical };
    public ToolbarWindow(OverlayWindow target)
    {
        overlay = target;
        Title = "Epic Pen 도구";
        Width = 124; Height = 490;
        Left = SystemParameters.WorkArea.Right - 140;
        Top = SystemParameters.WorkArea.Top + 80;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new SolidColorBrush(Color.FromRgb(30, 35, 48));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = buttons;
        Content = scroll;
        Add("마우스", () => overlay.SetTool(DrawTool.Mouse));
        Add("펜", () => overlay.SetTool(DrawTool.Pen));
        Add("지우개", () => overlay.SetTool(DrawTool.Eraser));
        Add("직선", () => overlay.SetTool(DrawTool.Line));
        Add("사각형", () => overlay.SetTool(DrawTool.Rectangle));
        Add("원", () => overlay.SetTool(DrawTool.Ellipse));
        Add("빨강", () => overlay.SetColor(Colors.Red));
        Add("파랑", () => overlay.SetColor(Colors.DodgerBlue));
        Add("노랑", () => overlay.SetColor(Colors.Yellow));
        Add("굵기 4", () => overlay.SetThickness(4));
        Add("굵기 9", () => overlay.SetThickness(9));
        Add("되돌리기", overlay.Undo);
        Add("전체 지우기", overlay.Clear);
        Closed += (_, _) => overlay.Close();
    }

    private void Add(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(5, 3, 5, 0), Height = 30 };
        button.Click += (_, _) => action();
        buttons.Children.Add(button);
    }
}