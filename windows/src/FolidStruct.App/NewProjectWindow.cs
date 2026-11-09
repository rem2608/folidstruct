using FolidStruct.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using static FolidStruct.Core.I18n;

namespace FolidStruct.App;

/// <summary>
/// The small window opened from the File Explorer menu: asks for the project
/// name and creates the folder structure (newproject.py on Linux).
/// </summary>
internal sealed class NewProjectWindow : Window
{
    private const double WindowWidth = 560;

    private readonly Config config;
    private readonly string parentDir;
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Now);
    private Template template;
    private NameFormat format;
    private int counter = 1;

    private readonly Grid root = new();
    private readonly ComboBox templateCombo;
    private readonly TextBox nameBox;
    private readonly Card nameCard;
    private readonly TextBlock detailsTitle;
    private readonly StackPanel detailsPanel = new();
    private readonly Dictionary<int, (Card Card, Control Input)> fields = [];
    private readonly ToggleSwitch dateSwitch = Ui.Toggle();
    private readonly ToggleSwitch counterSwitch = Ui.Toggle();
    private readonly Card counterCard;
    private readonly Card folderCard;
    private readonly Card subfoldersCard;
    private readonly InfoBar infoBar = new() { Severity = InfoBarSeverity.Error, IsClosable = true };
    private readonly Button createButton;

    public NewProjectWindow(Config config, Template template, string parentDir)
    {
        this.config = config;
        this.template = template;
        this.parentDir = parentDir;
        format = config.FormatFor(template);

        Title = T("New project");
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FolidStruct.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Template and name
        templateCombo = Ui.Combo(config.Templates.Select(t => t.Name), config.Templates.IndexOf(template));
        templateCombo.SelectionChanged += (_, _) => OnTemplateChanged();
        nameBox = Ui.TextField("", T("Project name"));
        nameBox.TextChanged += (_, _) => Update();
        nameBox.KeyDown += OnEnter;
        nameCard = new Card(T("Project name"), null, nameBox);

        // Fields and choices of the name format, rebuilt for each template
        detailsTitle = Ui.SectionTitle(T("Details"));

        // Options
        dateSwitch.Toggled += (_, _) => Update();
        counterSwitch.Toggled += (_, _) => Update();
        counterCard = new Card(T("Progressive number"), "", counterSwitch);

        // Preview
        folderCard = new Card(T("Folder"), "");
        folderCard.Description.IsTextSelectionEnabled = true;
        subfoldersCard = new Card(T("Subfolders"), "");
        var locationCard = new Card(T("Location"), parentDir);

        var content = new StackPanel { Padding = new Thickness(24, 4, 24, 20) };
        foreach (var element in new UIElement[]
                 {
                     infoBar,
                     new Card(T("Template"), null, templateCombo), nameCard,
                     detailsTitle, detailsPanel,
                     Ui.SectionTitle(T("Options")),
                     new Card(T("Date"), today.ToString("yyyy-MM-dd"), dateSwitch), counterCard,
                     Ui.SectionTitle(T("Result")),
                     folderCard, subfoldersCard, locationCard,
                 })
            content.Children.Add(element);

        // Buttons, at the bottom as in Windows dialogs
        createButton = new Button
        {
            Content = T("Create"),
            Style = Ui.Resource<Style>("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        createButton.Click += (_, _) => OnCreate();
        var cancelButton = new Button { Content = T("Cancel"), HorizontalAlignment = HorizontalAlignment.Stretch };
        cancelButton.Click += (_, _) => Close();
        var buttons = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(24),
            Background = Ui.Brush("SolidBackgroundFillColorBaseBrush"),
            BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.Children.Add(createButton);
        Grid.SetColumn(cancelButton, 1);
        buttons.Children.Add(cancelButton);

        var titleBar = new TextBlock
        {
            Text = T("New project"),
            Style = Ui.Resource<Style>("CaptionTextBlockStyle"),
            Margin = new Thickness(16, 10, 0, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroller = new ScrollViewer { Content = content };
        Grid.SetRow(scroller, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(titleBar);
        root.Children.Add(scroller);
        root.Children.Add(buttons);
        SetTitleBar(titleBar);

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) => { e.Handled = true; Close(); };
        root.KeyboardAccelerators.Add(escape);
        root.Loaded += (_, _) =>
        {
            FitToContent(center: true);
            nameBox.Focus(FocusState.Programmatic);
        };
        Content = root;

        OnTemplateChanged();
    }

    private void OnEnter(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OnCreate();
        }
    }

    private void OnTemplateChanged()
    {
        if (templateCombo.SelectedIndex < 0)
            return;
        template = config.Templates[templateCombo.SelectedIndex];
        format = config.FormatFor(template);
        // Fields and counter first: toggling the switches updates the preview.
        RebuildFields();
        RefreshCounter();
        dateSwitch.IsOn = format.Block(BlockKind.Date).Enabled;
        counterSwitch.IsOn = format.Block(BlockKind.Counter).Enabled;
        subfoldersCard.SetDescription(template.Folders.Count > 0 ? string.Join(", ", template.Folders)
                                                                  : T("No subfolders"));
        Update();
        if (root.IsLoaded)
            FitToContent(center: false);
    }

    private void RefreshCounter()
    {
        counter = Storage.ReadCounter(template);
        counterCard.SetDescription($"{counter}{Naming.CounterSuffix}");
    }

    private void RebuildFields()
    {
        detailsPanel.Children.Clear();
        fields.Clear();
        for (var index = 0; index < format.Blocks.Count; index++)
        {
            var block = format.Blocks[index];
            Control input;
            if (block.Kind == BlockKind.Field)
            {
                var box = Ui.TextField(block.Value);
                box.TextChanged += (_, _) => Update();
                box.KeyDown += OnEnter;
                input = box;
            }
            else if (block.Kind == BlockKind.Choice)
            {
                // The first entry leaves the block out of the name.
                var options = block.Options.Prepend("—").ToList();
                var combo = Ui.Combo(options, Math.Max(0, options.IndexOf(block.Value)));
                combo.SelectionChanged += (_, _) => Update();
                input = combo;
            }
            else
            {
                continue;
            }
            var card = new Card(block.Label, null, input);
            detailsPanel.Children.Add(card);
            fields[index] = (card, input);
        }
        var visibility = fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        detailsTitle.Visibility = visibility;
        detailsPanel.Visibility = visibility;
    }

    private Dictionary<int, string> Values()
    {
        var values = new Dictionary<int, string>();
        foreach (var (index, (_, input)) in fields)
        {
            values[index] = input switch
            {
                ComboBox { SelectedIndex: > 0 } combo => format.Blocks[index].Options[combo.SelectedIndex - 1],
                TextBox box => box.Text,
                _ => "",
            };
        }
        return values;
    }

    private string FolderName() => Naming.BuildProjectName(
        format,
        nameBox.Text.Length > 0 ? nameBox.Text : T("Project"),
        date: dateSwitch.IsOn ? today : null,
        counter: counterSwitch.IsOn ? counter : null,
        values: Values());

    private void Update()
    {
        var valid = nameBox.Text.Trim().Length > 0;
        var nameError = Naming.CheckChars(nameBox.Text);
        nameCard.SetDescription(nameError, error: true);
        valid &= nameError is null;
        foreach (var (card, input) in fields.Values)
        {
            if (input is not TextBox box)
                continue;
            var error = Naming.CheckChars(box.Text);
            card.SetDescription(error, error: true);
            valid &= error is null;
        }
        var folderName = FolderName();
        var folderError = nameError is null ? Naming.ValidateName(folderName) : null;
        folderCard.SetDescription(folderError is null ? folderName : $"{folderName}\n{folderError}",
                                  error: folderError is not null);
        createButton.IsEnabled = valid && folderError is null;
    }

    private void OnCreate()
    {
        if (!createButton.IsEnabled)
            return;
        try
        {
            Storage.CreateProject(parentDir, template, nameBox.Text, format,
                                  useDate: dateSwitch.IsOn, useCounter: counterSwitch.IsOn,
                                  values: Values(), today: today);
        }
        catch (AppException error)
        {
            infoBar.Title = T("Cannot create the project");
            infoBar.Message = error.Message;
            infoBar.IsOpen = true;
            // Another FolidStruct window may have used the number meanwhile.
            RefreshCounter();
            Update();
            FitToContent(center: false);
            return;
        }
        Close();
    }

    /// <summary>Size the window to its content, up to most of the screen height.</summary>
    private void FitToContent(bool center)
    {
        var scale = root.XamlRoot?.RasterizationScale ?? 1.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        root.Measure(new Windows.Foundation.Size(WindowWidth, double.PositiveInfinity));
        var height = Math.Min(root.DesiredSize.Height * scale, area.Height * 0.9);
        var size = new SizeInt32((int)Math.Ceiling(WindowWidth * scale), (int)Math.Ceiling(height));
        AppWindow.ResizeClient(size);
        if (center)
        {
            var outer = AppWindow.Size;
            AppWindow.Move(new PointInt32(area.X + (area.Width - outer.Width) / 2,
                                          area.Y + (area.Height - outer.Height) / 2));
        }
    }
}
