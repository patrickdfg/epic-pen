// 마우스 위치를 따라가는 클릭 강조 원과 크기·색상 설정 창을 제공합니다.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EpicPen;

public sealed class MouseHighlightWindow : Window
{
    private const int ExStyle = -20;
    private const long Transparent = 0x20;
    private const long NoActivate = 0x08000000;
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private readonly Canvas surface = new();
    private readonly Ellipse ring = new();
    private readonly Ellipse ripple = new();
    private readonly DispatcherTimer timer;
    private bool lastPressed;
    private DateTime clickedAt;
    public bool Enabled { get; private set; }
    public double Diameter { get; private set; } = 80;
    public double OpacityLevel { get; private set; } = 0.7;
    public Color RingColor { get; private set; } = Color.FromRgb(65, 169, 255);
    public bool ClickEffect { get; private set; } = true;

    public MouseHighlightWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 180;
        Height = 180;
        IsHitTestVisible = false;
        ring.StrokeThickness = 2.5;
        ring.Fill = Brushes.Transparent;
        ripple.StrokeThickness = 3;
        ripple.Fill = Brushes.Transparent;
        ripple.Visibility = Visibility.Collapsed;
        surface.Children.Add(ring);
        surface.Children.Add(ripple);
        Content = surface;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(hwnd, ExStyle).ToInt64();
            SetWindowLongPtr(hwnd, ExStyle, new IntPtr(style | Transparent | NoActivate));
        };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => UpdateCursor();
        UpdateStyle();
    }

    public void SetEnabled(bool value)
    {
        Enabled = value;
        if (value)
        {
            if (!IsVisible) Show();
            timer.Start();
            UpdateCursor();
        }
        else
        {
            timer.Stop();
            Hide();
        }
    }

    public void Configure(double diameter, Color color, double opacity, bool clickEffect)
    {
        Diameter = diameter;
        RingColor = color;
        OpacityLevel = opacity;
        ClickEffect = clickEffect;
        UpdateStyle();
    }

    private void UpdateStyle()
    {
        var stroke = new SolidColorBrush(RingColor);
        ring.Stroke = stroke;
        ring.Fill = new SolidColorBrush(Color.FromArgb((byte)(OpacityLevel * 35), RingColor.R, RingColor.G, RingColor.B));
        ring.Opacity = OpacityLevel;
        ripple.Stroke = stroke;
        ring.Width = ring.Height = Diameter;
        Canvas.SetLeft(ring, (Width - Diameter) / 2);
        Canvas.SetTop(ring, (Height - Diameter) / 2);
    }

    private void UpdateCursor()
    {
        if (!GetCursorPos(out var cursor)) return;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var position = transform.Transform(new Point(cursor.X, cursor.Y));
        Left = position.X - Width / 2;
        Top = position.Y - Height / 2;
        bool pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
        if (pressed && !lastPressed) clickedAt = DateTime.UtcNow;
        lastPressed = pressed;
        var elapsed = (DateTime.UtcNow - clickedAt).TotalMilliseconds;
        if (ClickEffect && elapsed >= 0 && elapsed < 350)
        {
            var fraction = elapsed / 350;
            var size = Diameter + 45 * fraction;
            ripple.Width = ripple.Height = size;
            ripple.Opacity = (1 - fraction) * OpacityLevel;
            Canvas.SetLeft(ripple, (Width - size) / 2);
            Canvas.SetTop(ripple, (Height - size) / 2);
            ripple.Visibility = Visibility.Visible;
        }
        else ripple.Visibility = Visibility.Collapsed;
    }

    public void ShowSettings(Window owner)
    {
        var dialog = new Window
        {
            Title = "마우스 강조 설정",
            Owner = owner,
            Width = 310,
            Height = 330,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "원 크기" });
        var size = new Slider { Minimum = 30, Maximum = 140, Value = Diameter, TickFrequency = 10, IsSnapToTickEnabled = true };
        panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "원 색상", Margin = new Thickness(0, 12, 0, 3) });
        var choices = new (string Name, Color Color)[] {
            ("하늘색", Color.FromRgb(65, 169, 255)),
            ("노랑", Colors.Gold),
            ("빨강", Colors.Red),
            ("초록", Colors.LimeGreen),
            ("보라", Colors.MediumPurple),
            ("흰색", Colors.White)
        };
        var color = new ComboBox { ItemsSource = Array.ConvertAll(choices, c => c.Name), SelectedIndex = Array.FindIndex(choices, c => c.Color == RingColor) };
        if (color.SelectedIndex < 0) color.SelectedIndex = 0;
        panel.Children.Add(color);
        panel.Children.Add(new TextBlock { Text = "투명도", Margin = new Thickness(0, 12, 0, 3) });
        var opacity = new Slider { Minimum = 0.2, Maximum = 1, Value = OpacityLevel };
        panel.Children.Add(opacity);
        var pulse = new CheckBox { Content = "클릭할 때 퍼지는 원 표시", IsChecked = ClickEffect, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(pulse);
        var save = new Button { Content = "적용", Height = 30, Margin = new Thickness(0, 14, 0, 0) };
        save.Click += (_, _) =>
        {
            Configure(size.Value, choices[color.SelectedIndex].Color, opacity.Value, pulse.IsChecked == true);
            dialog.Close();
        };
        panel.Children.Add(save);
        dialog.Content = panel;
        dialog.ShowDialog();
    }
}
