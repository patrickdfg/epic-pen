// 윈도우 전역 단축키를 등록하고 사용자가 지정한 키를 로컬 설정 파일에 저장합니다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace EpicPen;

public sealed class ShortcutManager : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private const int WmHotkey = 0x0312;
    private const uint ControlShift = 0x0002 | 0x0004;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EpicPen", "shortcuts.json");
    private static readonly string[] Names = { "펜", "마우스", "지우개", "전체 지우기", "흰 배경" };
    private static readonly string[] Defaults = { "P", "M", "E", "D", "W" };
    private readonly Window owner;
    private readonly OverlayWindow overlay;
    private readonly IntPtr handle;
    private readonly HwndSource source;
    private string[] letters = (string[])Defaults.Clone();
    private readonly List<int> registered = new();

    public ShortcutManager(Window window, OverlayWindow target)
    {
        owner = window;
        overlay = target;
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle)!;
        source.AddHook(WndProc);
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(SettingsPath));
                if (saved != null && Valid(saved)) letters = saved;
            }
        }
        catch (IOException) { /* 저장 파일을 읽을 수 없으면 기본값을 사용합니다. */ }
        catch (JsonException) { /* 손상된 설정은 기본값을 사용합니다. */ }
        Register();
    }

    private static bool Valid(string[] values) =>
        values.Length == Names.Length &&
        values.All(x => x.Length == 1 && x[0] >= 'A' && x[0] <= 'Z') &&
        values.Distinct().Count() == values.Length;

    private void Unregister()
    {
        foreach (var id in registered) UnregisterHotKey(handle, id);
        registered.Clear();
    }

    private bool Register()
    {
        Unregister();
        for (int i = 0; i < letters.Length; i++)
        {
            if (!RegisterHotKey(handle, 4100 + i, ControlShift, letters[i][0]))
            {
                Unregister();
                return false;
            }
            registered.Add(4100 + i);
        }
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        int id = wParam.ToInt32() - 4100;
        if (id < 0 || id >= Names.Length) return IntPtr.Zero;
        handled = true;
        switch (id)
        {
            case 0: overlay.SetTool(DrawTool.Pen); break;
            case 1: overlay.SetTool(DrawTool.Mouse); break;
            case 2: overlay.SetTool(DrawTool.Eraser); break;
            case 3: overlay.Clear(); break;
            case 4: overlay.ToggleWhiteboard(); break;
        }
        return IntPtr.Zero;
    }

    public void ShowSettings(Window parent)
    {
        var dialog = new Window
        {
            Title = "단축키 설정",
            Owner = parent,
            Width = 340,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = "모든 단축키는 Ctrl + Shift + 선택한 문자입니다.", Margin = new Thickness(0, 0, 0, 12) });
        var boxes = new List<ComboBox>();
        for (int i = 0; i < Names.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = Names[i], Width = 115, VerticalAlignment = VerticalAlignment.Center });
            var combo = new ComboBox { Width = 130, ItemsSource = Enumerable.Range('A', 26).Select(x => ((char)x).ToString()).ToArray(), SelectedItem = letters[i] };
            row.Children.Add(combo);
            boxes.Add(combo);
            stack.Children.Add(row);
        }
        var save = new Button { Content = "저장", Height = 32, Margin = new Thickness(0, 12, 0, 0) };
        save.Click += (_, _) =>
        {
            var proposed = boxes.Select(x => x.SelectedItem?.ToString() ?? "").ToArray();
            if (!Valid(proposed))
            {
                MessageBox.Show(dialog, "단축키 문자가 중복되지 않도록 설정해 주세요.");
                return;
            }
            var previous = letters;
            letters = proposed;
            if (!Register())
            {
                letters = previous;
                Register();
                MessageBox.Show(dialog, "다른 프로그램이 사용 중인 단축키입니다. 다른 문자를 선택해 주세요.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(letters));
            dialog.Close();
        };
        stack.Children.Add(save);
        dialog.Content = stack;
        dialog.ShowDialog();
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(WndProc);
    }
}
