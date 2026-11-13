using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EpicPen;

public sealed partial class OverlayWindow
{
    private readonly Canvas textLayer = new() { Focusable = true, IsHitTestVisible = false };
    private TextAnnotation? selectedText;
    private TextBox? textEditor;
    private bool isNewText;

    private void InitializeTextTool()
    {
        textLayer.Background = HitBackground;
        textLayer.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != textLayer) return;
            FinishTextEdit();
            var annotation = new TextAnnotation(this, new SolidColorBrush(PenColor));
            var position = e.GetPosition(textLayer);
            Canvas.SetLeft(annotation, position.X);
            Canvas.SetTop(annotation, position.Y);
            textLayer.Children.Add(annotation);
            BeginTextEdit(annotation, isNew: true);
            e.Handled = true;
        };
    }

    internal void HandleAnnotationKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FinishTextEdit(cancel: true);
            SetTool(DrawTool.Mouse);
            e.Handled = true;
        }
        else if (textEditor == null && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.T)
            {
                if (!e.IsRepeat) SetTool(Tool == DrawTool.Text ? DrawTool.Mouse : DrawTool.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && selectedText is { } annotation)
            {
                RemoveText(annotation);
                e.Handled = true;
            }
        }
    }

    private void SelectText(TextAnnotation? annotation)
    {
        selectedText?.ShowSelection(false);
        selectedText = annotation;
        selectedText?.ShowSelection(true);
    }

    private void BeginTextEdit(TextAnnotation annotation, bool isNew = false)
    {
        SelectText(annotation);
        isNewText = isNew;
        annotation.Visibility = Visibility.Hidden;
        var editor = new TextBox
        {
            Text = annotation.Label.Text,
            FontSize = annotation.Label.FontSize,
            Foreground = annotation.Label.Foreground,
            Background = HitBackground,
            CaretBrush = annotation.Label.Foreground,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            MinWidth = 80,
            MaxWidth = Math.Max(80, textLayer.ActualWidth - Canvas.GetLeft(annotation)),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        Canvas.SetLeft(editor, Canvas.GetLeft(annotation));
        Canvas.SetTop(editor, Canvas.GetTop(annotation));
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            FinishTextEdit();
            e.Handled = true;
        };
        textEditor = editor;
        textLayer.Children.Add(editor);
        editor.Focus();
        editor.SelectAll();
    }

    private void FinishTextEdit(bool cancel = false)
    {
        if (textEditor is not { } editor || selectedText is not { } annotation) return;
        textEditor = null;
        textLayer.Children.Remove(editor);
        annotation.Visibility = Visibility.Visible;
        if (!cancel && !string.IsNullOrWhiteSpace(editor.Text))
        {
            annotation.Label.Text = editor.Text;
            if (isNewText) history.Add(annotation);
        }
        else if (isNewText)
        {
            RemoveText(annotation);
        }
        if (Tool == DrawTool.Text && IsActive) textLayer.Focus();
    }

    private void RemoveText(TextAnnotation annotation)
    {
        if (selectedText == annotation) SelectText(null);
        textLayer.Children.Remove(annotation);
        history.Remove(annotation);
    }

    private sealed class TextAnnotation : Grid
    {
        private readonly OverlayWindow owner;
        private readonly Border outline;
        private readonly Thumb[] handles = new Thumb[4];

        public TextBlock Label { get; }

        public TextAnnotation(OverlayWindow owner, Brush color)
        {
            this.owner = owner;
            Background = HitBackground;
            Label = new TextBlock
            {
                FontSize = 32,
                Foreground = color,
                Margin = new Thickness(5),
                IsHitTestVisible = false
            };
            Children.Add(Label);

            var move = new Thumb { Cursor = Cursors.SizeAll, Template = HandleTemplate(HitBackground) };
            move.PreviewMouseLeftButtonDown += (_, e) =>
            {
                owner.FinishTextEdit();
                owner.SelectText(this);
                owner.textLayer.Focus();
                if (e.ClickCount != 2) return;
                owner.BeginTextEdit(this);
                e.Handled = true;
            };
            move.DragDelta += (_, e) =>
            {
                Canvas.SetLeft(this, Canvas.GetLeft(this) + e.HorizontalChange);
                Canvas.SetTop(this, Canvas.GetTop(this) + e.VerticalChange);
            };
            Children.Add(move);

            outline = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };
            Children.Add(outline);
            handles[0] = AddCorner(HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
            handles[1] = AddCorner(HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
            handles[2] = AddCorner(HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
            handles[3] = AddCorner(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
            ShowSelection(false);
        }

        public void ShowSelection(bool show)
        {
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;
            outline.Visibility = visibility;
            foreach (var handle in handles) handle.Visibility = visibility;
        }

        private Thumb AddCorner(HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor)
        {
            var handle = new Thumb
            {
                Width = 9,
                Height = 9,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Cursor = cursor,
                Template = HandleTemplate(Brushes.DodgerBlue)
            };
            bool left = horizontal == HorizontalAlignment.Left;
            bool top = vertical == VerticalAlignment.Top;
            Point start = default;
            Point position = default;
            Size size = default;
            double fontSize = 0;
            handle.DragStarted += (_, _) =>
            {
                start = Mouse.GetPosition(owner.textLayer);
                position = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
                size = RenderSize;
                fontSize = Label.FontSize;
            };
            handle.DragDelta += (_, _) =>
            {
                var delta = Mouse.GetPosition(owner.textLayer) - start;
                var diagonal = new Vector(left ? -size.Width : size.Width, top ? -size.Height : size.Height);
                var scale = 1 + (delta * diagonal) / diagonal.LengthSquared;
                Label.FontSize = Math.Clamp(fontSize * scale, 8, 256);
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (left) Canvas.SetLeft(this, position.X + size.Width - DesiredSize.Width);
                if (top) Canvas.SetTop(this, position.Y + size.Height - DesiredSize.Height);
            };
            Children.Add(handle);
            return handle;
        }

        private static ControlTemplate HandleTemplate(Brush brush)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, brush);
            return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        }
    }
}
