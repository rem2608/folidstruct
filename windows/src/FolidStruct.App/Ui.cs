using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FolidStruct.App;

/// <summary>Segoe Fluent Icons glyphs.</summary>
internal static class Glyph
{
    public const string Add = "";
    public const string Delete = "";
    public const string Up = "";
    public const string Down = "";
    public const string Edit = "";
    public const string Settings = "";
    public const string Info = "";
    public const string Folder = "";
    public const string NewFolder = "";
}

/// <summary>
/// Small builders for the settings-style layout used everywhere (the Windows 11
/// counterpart of the libadwaita rows of the Linux version).
/// </summary>
internal static class Ui
{
    public static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    public static Brush Brush(string key) => Resource<Brush>(key);

    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size };

    public static Button IconButton(string glyph, string tooltip, Action onClick, bool enabled = true)
    {
        var button = new Button
        {
            Content = Icon(glyph, 14),
            IsEnabled = enabled,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A switch without the "On"/"Off" text, which follows the system language.</summary>
    public static ToggleSwitch Toggle(bool isOn = false) =>
        new() { IsOn = isOn, OnContent = "", OffContent = "", MinWidth = 0 };

    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("TitleTextBlockStyle"),
        Margin = new Thickness(0, 0, 0, 8),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("BodyStrongTextBlockStyle"),
        Margin = new Thickness(1, 20, 0, 6),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("CaptionTextBlockStyle"),
        Foreground = Brush("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    public static Border CardBorder(UIElement child) => new()
    {
        Child = child,
        Background = Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = Resource<CornerRadius>("ControlCornerRadius"),
        Padding = new Thickness(16, 10, 12, 10),
        MinHeight = 60,
        Margin = new Thickness(0, 0, 0, 3),
    };

    /// <summary>A text field that does not stretch the card too much.</summary>
    public static TextBox TextField(string text, string? placeholder = null, double width = 280) => new()
    {
        Text = text,
        PlaceholderText = placeholder ?? "",
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static ComboBox Combo(IEnumerable<string> items, int selected, double minWidth = 200)
    {
        var combo = new ComboBox { MinWidth = minWidth, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = selected;
        return combo;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    public static ContentDialog Dialog(XamlRoot root, string title) => new()
    {
        XamlRoot = root,
        Title = title,
        Style = Resource<Style>("DefaultContentDialogStyle"),
        DefaultButton = ContentDialogButton.Primary,
    };
}

/// <summary>A settings card: title and description on the left, controls on the right.</summary>
internal sealed class Card
{
    public Border Root { get; }
    public TextBlock Header { get; }
    public TextBlock Description { get; }

    public Card(string header, string? description = null, UIElement? content = null, UIElement? icon = null)
    {
        Header = new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap };
        Description = Ui.Caption(description ?? "");
        Description.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(Header);
        text.Children.Add(Description);

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon is not null)
        {
            if (icon is FrameworkElement fe)
                fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (content is not null)
        {
            Grid.SetColumn((FrameworkElement)content, 2);
            grid.Children.Add(content);
        }
        Root = Ui.CardBorder(grid);
    }

    public void SetDescription(string? text, bool error = false)
    {
        Description.Text = text ?? "";
        Description.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        Description.Foreground = Ui.Brush(error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
    }

    public static implicit operator UIElement(Card card) => card.Root;
}
