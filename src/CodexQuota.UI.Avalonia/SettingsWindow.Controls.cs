using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed partial class SettingsWindow
{
    private StackPanel Page(string heading, string description) => new() { Spacing = 10, Children =
    {
        UiElements.Text(heading, 23, FontWeight.Bold, _palette.TextPrimary),
        UiElements.Text(description, 11.5, FontWeight.Normal, _palette.TextMuted),
        new Border { Height = 1, Background = _palette.Border, Margin = new Thickness(0, 0, 0, 3) }
    }};

    private Control Scroll(Control content) => new ScrollViewer { Content = content,
        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    private Control ToggleRow(string title, string description, bool value, Action<bool> changed) =>
        Row(title, description, Check(value, changed));

    private Control Row(string title, string description, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            UiElements.Text(title, 13.5, FontWeight.SemiBold, _palette.TextPrimary),
            UiElements.Text(description, 10.5, FontWeight.Normal, _palette.TextMuted)
        }});
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return UiElements.Card(grid, _palette, new Thickness(16, 12));
    }

    private Control ActionRow(string title, string description, params (string Label, Action Action)[] actions)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var action in actions)
        {
            var button = UiElements.Button(action.Label, _palette);
            button.Click += (_, _) => action.Action();
            buttons.Children.Add(button);
        }
        return Row(title, description, buttons);
    }

    private Control Check(bool value, Action<bool> changed)
    {
        var toggle = new TogglePillControl
        {
            IsChecked = value,
            OnBrush = _palette.Mint,
            OffBrush = _palette.Border,
            ThumbBrush = _palette.TextPrimary,
            FocusBrush = _palette.Blue,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.ValueChanged += next => { if (!_building) changed(next); };
        return toggle;
    }

    private ComboBox Combo<T>(IEnumerable<T> values, T selected, Action<T> changed) where T : struct, Enum
    {
        var choices = values.Select(value => new EnumChoice<T>(value, EnumLabel(value))).ToArray();
        var combo = new ComboBox { ItemsSource = choices,
            SelectedItem = choices.First(choice => EqualityComparer<T>.Default.Equals(choice.Value, selected)), MinWidth = 178,
            FontFamily = UiElements.AppFont, FontSize = 12.5 };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_building && combo.SelectedItem is EnumChoice<T> choice) changed(choice.Value);
        };
        combo.PointerWheelChanged += (_, e) => e.Handled = true;
        return combo;
    }

    private Control NumberSlider(int minimum, int maximum, int value, Action<int> changed)
    {
        var slider = new ValueSliderControl
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = 170,
            TrackBrush = _palette.Border,
            FillBrush = _palette.Mint,
            ThumbBrush = _palette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Avalonia's macOS template reserves more horizontal space for the two
        // spinner buttons than the Windows template. Keep enough fixed room for
        // three digits at 1x so values such as 100 and 150 are never reduced to
        // a single visible character.
        var number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value,
            Width = Math.Clamp(128 * UiElements.ScaleFactor, 128, 180),
            Increment = 1, FormatString = "0", FontFamily = UiElements.AppFont };
        var updating = false;
        slider.ValueChanged += next =>
        {
            if (_building || updating) return;
            updating = true;
            number.Value = (decimal)Math.Round(next);
            updating = false;
            changed((int)Math.Round(next));
        };
        number.ValueChanged += (_, _) =>
        {
            if (_building || updating || number.Value is null) return;
            updating = true;
            slider.Value = (double)number.Value.Value;
            updating = false;
            changed((int)number.Value.Value);
        };
        number.PointerWheelChanged += (_, e) => e.Handled = true;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, number } };
    }

    private TextBox ColorBox(string value, Action<string> changed)
    {
        var box = new TextBox { Text = value, Width = 110, FontFamily = UiElements.AppFont, FontSize = 12 };
        box.LostFocus += (_, _) => { if (!_building && box.Text is { } text) changed(text); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && box.Text is { } text) { changed(text); e.Handled = true; }
        };
        return box;
    }

    private ComboBox WindowCombo(int selected, Action<int> changed)
    {
        var items = _availableWindowMinutes
            .Append(selected).Where(minutes => minutes > 0).Distinct().OrderBy(minutes => minutes)
            .Select(minutes => new WindowChoice(minutes, FormatWindow(minutes))).ToArray();
        var combo = new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(item => item.Minutes == selected) ?? items[0],
            MinWidth = 178, FontFamily = UiElements.AppFont, FontSize = 12.5 };
        combo.SelectionChanged += (_, _) => { if (!_building && combo.SelectedItem is WindowChoice choice) changed(choice.Minutes); };
        combo.PointerWheelChanged += (_, e) => e.Handled = true;
        return combo;
    }

    private Control TimeRange(int start, int end, Action<int> startChanged, Action<int> endChanged)
    {
        var minutes = Enumerable.Range(0, 48).Select(index => index * 30)
            .Append(start).Append(end).Distinct().OrderBy(value => value)
            .Select(value => new WindowChoice(value, $"{value / 60:00}:{value % 60:00}"))
            .ToArray();
        ComboBox Make(int selected, Action<int> changed)
        {
            var combo = new ComboBox { ItemsSource = minutes,
                SelectedItem = minutes.First(item => item.Minutes == selected), MinWidth = 94,
                FontFamily = UiElements.AppFont, FontSize = 12.5 };
            combo.SelectionChanged += (_, _) =>
            {
                if (!_building && combo.SelectedItem is WindowChoice value) changed(value.Minutes);
            };
            combo.PointerWheelChanged += (_, e) => e.Handled = true;
            return combo;
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            Make(start, startChanged), UiElements.Text("—", 12, FontWeight.Normal, _palette.TextMuted), Make(end, endChanged)
        }};
    }

    private string FormatWindow(int minutes)
    {
        if (minutes % 10_080 == 0)
        {
            var days = minutes / 1_440;
            return T($"{days} 天窗口", $"{days}-day window");
        }
        if (minutes % 1_440 == 0)
        {
            var days = minutes / 1_440;
            return T($"{days} 天窗口", $"{days}-day window");
        }
        if (minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return T($"{hours} 小时窗口", $"{hours}-hour window");
        }
        return T($"{minutes} 分钟窗口", $"{minutes}-minute window");
    }

    private string EnumLabel<T>(T value) where T : struct, Enum => (object)value switch
    {
        StartupViewMode.RestorePrevious => this.T("恢复上次状态", "Restore previous"),
        StartupViewMode.Orb => this.T("悬浮球", "Orb"),
        StartupViewMode.Details => this.T("详情面板", "Details"),
        StartupViewMode.TrayOnly => this.T("仅托盘", "Tray only"),
        AppLanguage.SimplifiedChinese => "简体中文",
        AppLanguage.English => "English",
        AppTheme.System => this.T("跟随系统", "Follow system"),
        AppTheme.Dark => this.T("深色", "Dark"),
        AppTheme.Light => this.T("浅色", "Light"),
        ConsumptionFeedbackStyle.Ember => this.T("简约余烬", "Minimal ember"),
        ConsumptionFeedbackStyle.Fluid => this.T("流体火焰", "Fluid flame"),
        ConsumptionFeedbackStyle.Pixel => this.T("像素火焰", "Pixel flame"),
        _ => value.ToString()
    };

    private sealed record EnumChoice<T>(T Value, string Label) where T : struct, Enum
    {
        public override string ToString() => Label;
    }

}
