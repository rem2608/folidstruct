using FolidStruct.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using static FolidStruct.Core.I18n;

namespace FolidStruct.App;

/// <summary>The main window: create and edit templates, set the global options (manager.py).</summary>
internal sealed class ManagerWindow : Window
{
    private const string Website = "https://github.com/rem2608/folidstruct";

    private readonly Config config;
    private readonly Grid root = new();
    private NavigationView nav = null!;
    private readonly InfoBar infoBar = new() { Severity = InfoBarSeverity.Error, IsClosable = true };
    private TemplateEditor? editor;

    public ManagerWindow(Config config)
    {
        this.config = config;
        Title = "FolidStruct";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FolidStruct.ico"));
        var scale = Native.ScaleFactor(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(720 * scale)));
        Content = root;
        Build();
        // Projects created from File Explorer use up numbers meanwhile.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                editor?.RefreshCounter();
        };
    }

    public void BringToFront()
    {
        Activate();
        Native.BringToFront(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    // --- Layout ----------------------------------------------------------------

    /// <summary>Build (or rebuild, after a language change) the whole interface.</summary>
    private void Build(object? select = null)
    {
        root.Children.Clear();
        root.RowDefinitions.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Thickness(16, 0, 0, 0),
        };
        titleBar.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png")),
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleBar.Children.Add(new TextBlock
        {
            Text = "FolidStruct",
            Style = Ui.Resource<Style>("CaptionTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(titleBar);
        SetTitleBar(titleBar);

        nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = true,
            OpenPaneLength = 280,
        };
        Grid.SetRow(nav, 1);
        root.Children.Add(nav);

        var newItem = new NavigationViewItem
        {
            Content = T("New template"),
            Icon = Ui.Icon(Glyph.NewFolder),
            SelectsOnInvoked = false,
            Tag = "new",
        };
        nav.MenuItems.Add(newItem);
        nav.MenuItems.Add(new NavigationViewItemSeparator());
        nav.MenuItems.Add(new NavigationViewItemHeader { Content = T("Templates") });
        foreach (var template in config.Templates)
            nav.MenuItems.Add(TemplateItem(template));

        var preferences = new NavigationViewItem
        {
            Content = T("Preferences"),
            Icon = Ui.Icon(Glyph.Settings),
            Tag = "preferences",
        };
        nav.FooterMenuItems.Add(preferences);
        nav.FooterMenuItems.Add(new NavigationViewItem
        {
            Content = T("About FolidStruct"),
            Icon = Ui.Icon(Glyph.Info),
            SelectsOnInvoked = false,
            Tag = "about",
        });

        nav.ItemInvoked += (_, e) =>
        {
            switch (e.InvokedItemContainer?.Tag)
            {
                case "new":
                    NewTemplate();
                    break;
                case "about":
                    ShowAbout();
                    break;
            }
        };
        nav.SelectionChanged += (_, e) => Show(e.SelectedItemContainer?.Tag);

        nav.SelectedItem = select switch
        {
            "preferences" => preferences,
            Template t when FindItem(t) is { } item => item,
            _ => nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag is Template),
        };
        if (nav.SelectedItem is null)
            Show(null);
    }

    private static NavigationViewItem TemplateItem(Template template) => new()
    {
        Content = template.Name,
        Icon = Ui.Icon(Glyph.Folder),
        Tag = template,
    };

    private NavigationViewItem? FindItem(Template template) =>
        nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, template));

    /// <summary>Show a page in the content area, with the error bar on top.</summary>
    private void SetPage(UIElement page)
    {
        var content = new StackPanel
        {
            MaxWidth = 1000,
            Padding = new Thickness(36, 8, 36, 36),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // The error bar is shared by every page: take it off the previous one first.
        if (infoBar.Parent is Panel previous)
            previous.Children.Remove(infoBar);
        infoBar.IsOpen = false;
        content.Children.Add(infoBar);
        content.Children.Add(page);
        nav.Content = new ScrollViewer { Content = content };
    }

    private void Show(object? tag)
    {
        editor = null;
        switch (tag)
        {
            case Template template:
                editor = new TemplateEditor(this, config, template);
                SetPage(editor.Page);
                break;
            case "preferences":
                SetPage(PreferencesPage());
                break;
            default:
                if (config.Templates.Count == 0)
                    SetPage(EmptyPage());
                break;
        }
    }

    private UIElement EmptyPage()
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 96, 0, 0),
        };
        panel.Children.Add(new FontIcon { Glyph = Glyph.NewFolder, FontSize = 64, Foreground = Ui.Brush("TextFillColorSecondaryBrush") });
        panel.Children.Add(new TextBlock
        {
            Text = T("No templates"),
            Style = Ui.Resource<Style>("SubtitleTextBlockStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = T("Create a template to define a folder structure"),
            Foreground = Ui.Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var create = new Button
        {
            Content = T("Create template"),
            Style = Ui.Resource<Style>("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        create.Click += (_, _) => NewTemplate();
        panel.Children.Add(create);
        return panel;
    }

    // --- Persistence -----------------------------------------------------------

    public void Save()
    {
        try
        {
            Storage.SaveConfig(config);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError(e.Message);
        }
    }

    public void ShowError(string message)
    {
        infoBar.Message = message;
        infoBar.IsOpen = true;
    }

    // --- Templates -------------------------------------------------------------

    public void OnTemplateRenamed(Template template)
    {
        if (FindItem(template) is { } item)
            item.Content = template.Name;
    }

    private void NewTemplate()
    {
        var template = config.NewTemplate(Naming.UniqueName(T("New template"), config.Templates.Select(t => t.Name)));
        config.Templates.Add(template);
        Save();
        var item = TemplateItem(template);
        nav.MenuItems.Add(item);
        nav.SelectedItem = item;
        editor?.FocusName();
    }

    public async void DeleteTemplate(Template template)
    {
        var dialog = Ui.Dialog(root.XamlRoot, T("Delete “{name}”?", ("name", template.Name)));
        dialog.Content = new TextBlock
        {
            Text = T("The template will be removed. Folders already created are not affected."),
            TextWrapping = TextWrapping.Wrap,
        };
        dialog.PrimaryButtonText = T("Delete");
        dialog.CloseButtonText = T("Cancel");
        dialog.DefaultButton = ContentDialogButton.Close;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        config.Templates.Remove(template);
        Save();
        Storage.ForgetCounter(template);
        if (FindItem(template) is { } item)
            nav.MenuItems.Remove(item);
        nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag is Template);
        if (nav.SelectedItem is null)
            Show(null);
    }

    // --- Preferences -----------------------------------------------------------

    private UIElement PreferencesPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(T("Preferences")));

        string[] choices = [I18n.Auto, .. I18n.LanguageNames.Keys];
        var labels = choices.Select(c => c == I18n.Auto ? T("Automatic") : I18n.LanguageNames[c]);
        var current = Array.IndexOf(choices, config.Language);
        var language = Ui.Combo(labels, current < 0 ? 0 : current);
        language.SelectionChanged += (_, _) =>
        {
            config.Language = choices[language.SelectedIndex];
            SetLanguage(config.Language);
            config.TranslateExample(CurrentLanguage);
            Save();
            // Rebuild the interface in the new language.
            DispatcherQueue.TryEnqueue(() => Build(select: "preferences"));
        };
        page.Children.Add(Ui.SectionTitle(T("Language")));
        page.Children.Add(new Card(T("Language"), T("“Automatic” uses the system language"), language));

        page.Children.Add(Ui.SectionTitle(T("Project folder name")));
        var description = Ui.Caption(T("Used by every template without custom options. Date and number can also be " +
                                       "switched on or off when creating a project."));
        description.Margin = new Thickness(1, 0, 0, 8);
        page.Children.Add(description);
        page.Children.Add(new FormatEditor(config.Format, Save).Panel);
        return page;
    }

    // --- About -----------------------------------------------------------------

    private async void ShowAbout()
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Square150x150Logo.png")),
            Width = 96,
            Height = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "FolidStruct",
            Style = Ui.Resource<Style>("SubtitleTextBlockStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = T("Version {version}", ("version", AppInfo.Version)),
            Foreground = Ui.Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (var text in new[]
                 {
                     T("Create project folder structures from templates"),
                     T("Right-click a folder in File Explorer and choose FolidStruct"),
                     T("License: GNU GPL version 3 or later"),
                 })
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        panel.Children.Add(new HyperlinkButton
        {
            Content = T("Website"),
            NavigateUri = new Uri(Website),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var dialog = Ui.Dialog(root.XamlRoot, "");
        dialog.Title = null;
        dialog.Content = panel;
        dialog.CloseButtonText = T("Close");
        dialog.DefaultButton = ContentDialogButton.Close;
        await dialog.ShowAsync();
    }
}

/// <summary>Edits one template; every valid change is saved immediately.</summary>
internal sealed class TemplateEditor
{
    private readonly ManagerWindow window;
    private readonly Config config;
    private readonly Template template;
    private readonly TextBox nameBox;
    private readonly Card nameCard;
    private readonly TextBlock title;
    private readonly FormatEditor formatEditor;
    private readonly NumberBox next;
    private readonly StackPanel foldersPanel = new();

    public StackPanel Page { get; } = new();

    public TemplateEditor(ManagerWindow window, Config config, Template template)
    {
        this.window = window;
        this.config = config;
        this.template = template;

        // Title and delete button
        title = Ui.PageTitle(template.Name);
        var delete = new Button
        {
            Content = Ui.Row(Ui.Icon(Glyph.Delete, 14), new TextBlock { Text = T("Delete template") }),
            VerticalAlignment = VerticalAlignment.Center,
        };
        delete.Click += (_, _) => window.DeleteTemplate(template);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        Grid.SetColumn(delete, 1);
        header.Children.Add(delete);
        Page.Children.Add(header);

        // Name
        nameBox = Ui.TextField(template.Name);
        nameBox.TextChanged += (_, _) => OnNameChanged();
        nameCard = new Card(T("Name"), null, nameBox);
        Page.Children.Add(Ui.SectionTitle(T("Template")));
        Page.Children.Add(nameCard);

        // Project folder name: this template's counter and optional format
        next = new NumberBox
        {
            Minimum = 1,
            Maximum = 999999,
            Value = Storage.ReadCounter(template),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 140,
            VerticalAlignment = VerticalAlignment.Center,
        };
        next.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue))
            {
                next.Value = Storage.ReadCounter(template);
                return;
            }
            try
            {
                Storage.WriteCounter(template, (int)e.NewValue);
            }
            catch (AppException error)
            {
                window.ShowError(error.Message);
            }
            formatEditor?.Refresh();
        };

        formatEditor = new FormatEditor(template.Format, window.Save, () => Storage.ReadCounter(template));
        var custom = Ui.Toggle(template.CustomFormat);
        var expanderHeader = new Card(T("Custom options"), T("Use this name format instead of the global one"), custom);
        expanderHeader.Root.Background = null;
        expanderHeader.Root.BorderThickness = new Thickness(0);
        expanderHeader.Root.Padding = new Thickness(0, 8, 0, 8);
        var expander = new Expander
        {
            Header = expanderHeader.Root,
            Content = formatEditor.Panel,
            IsExpanded = template.CustomFormat,
            IsEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 3),
        };
        formatEditor.Panel.IsHitTestVisible = template.CustomFormat;
        formatEditor.Panel.Opacity = template.CustomFormat ? 1 : 0.5;
        custom.Toggled += (_, _) =>
        {
            template.CustomFormat = custom.IsOn;
            expander.IsExpanded = custom.IsOn;
            formatEditor.Panel.IsHitTestVisible = custom.IsOn;
            formatEditor.Panel.Opacity = custom.IsOn ? 1 : 0.5;
            window.Save();
        };

        Page.Children.Add(Ui.SectionTitle(T("Project folder name")));
        Page.Children.Add(new Card(T("Next number"), T("Each template counts on its own"), next));
        Page.Children.Add(expander);

        // Folders
        var foldersHeader = new Grid();
        foldersHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foldersHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foldersHeader.Children.Add(Ui.SectionTitle(T("Folders")));
        var add = new Button
        {
            Content = Ui.Row(Ui.Icon(Glyph.Add, 12), new TextBlock { Text = T("Add folder") }),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };
        add.Click += (_, _) => AddFolder();
        Grid.SetColumn(add, 1);
        foldersHeader.Children.Add(add);
        Page.Children.Add(foldersHeader);
        Page.Children.Add(foldersPanel);
        RebuildFolders();
    }

    /// <summary>Show the current next number, which the New project window may have used.</summary>
    public void RefreshCounter()
    {
        var value = Storage.ReadCounter(template);
        if ((int)next.Value != value)
            next.Value = value; // also redraws the format editor
    }

    public void FocusName()
    {
        void Focus() { nameBox.Focus(FocusState.Programmatic); nameBox.SelectAll(); }
        if (nameBox.IsLoaded)
            Focus();
        else
            nameBox.Loaded += (_, _) => Focus();
    }

    // Name

    private void OnNameChanged()
    {
        var name = nameBox.Text.Trim();
        var error = Naming.ValidateName(name);
        if (error is null && config.Templates.Any(t => t != template && t.Name == name))
            error = T("A template with this name already exists");
        nameCard.SetDescription(error, error: true);
        if (error is null && name != template.Name)
        {
            template.Name = name;
            title.Text = name;
            window.OnTemplateRenamed(template);
            window.Save();
        }
    }

    // Folders

    private void RebuildFolders(int? focus = null)
    {
        foldersPanel.Children.Clear();
        var count = template.Folders.Count;
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var box = new TextBox { Text = template.Folders[i], VerticalAlignment = VerticalAlignment.Center };
            var error = Ui.Caption("");
            error.Foreground = Ui.Brush("SystemFillColorCriticalBrush");
            error.Visibility = Visibility.Collapsed;
            box.TextChanged += (_, _) => OnFolderChanged(box, error, index);

            var number = new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = Ui.Brush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 20,
            };
            var buttons = Ui.Row(
                Ui.IconButton(Glyph.Up, T("Move up"), () => Move(index, -1), i > 0),
                Ui.IconButton(Glyph.Down, T("Move down"), () => Move(index, 1), i < count - 1),
                Ui.IconButton(Glyph.Delete, T("Remove"), () => Remove(index)));

            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(box);
            text.Children.Add(error);
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(number);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, T("Folder {n}", ("n", i + 1)));
            foldersPanel.Children.Add(Ui.CardBorder(grid));

            if (focus == i)
                box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        }
    }

    private void OnFolderChanged(TextBox box, TextBlock errorText, int index)
    {
        var name = box.Text.Trim();
        var error = Naming.ValidateName(name);
        if (error is null && template.Folders.Where((f, j) => j != index)
                                             .Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
            error = T("This folder is already in the list");
        errorText.Text = error ?? "";
        errorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is null)
        {
            template.Folders[index] = name;
            window.Save();
        }
    }

    private void AddFolder()
    {
        template.Folders.Add(Naming.UniqueName(T("New folder"), template.Folders, ignoreCase: true));
        window.Save();
        RebuildFolders(focus: template.Folders.Count - 1);
    }

    private void Move(int index, int delta)
    {
        var folders = template.Folders;
        (folders[index], folders[index + delta]) = (folders[index + delta], folders[index]);
        window.Save();
        RebuildFolders(focus: index + delta);
    }

    private void Remove(int index)
    {
        template.Folders.RemoveAt(index);
        window.Save();
        RebuildFolders();
    }
}
