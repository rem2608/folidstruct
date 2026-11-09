using FolidStruct.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static FolidStruct.Core.I18n;

namespace FolidStruct.App;

/// <summary>
/// Editor for the project folder name format: separator and blocks
/// (formateditor.py on Linux). The same editor is used in Preferences (global
/// format) and in each template (custom format).
/// </summary>
internal sealed class FormatEditor
{
    private readonly NameFormat format;
    private readonly Action onChange;
    /// <summary>The next number to show, or null for the global format.</summary>
    private readonly Func<int>? counter;
    private readonly Card exampleCard;

    public StackPanel Panel { get; } = new();

    public FormatEditor(NameFormat format, Action onChange, Func<int>? counter = null)
    {
        this.format = format;
        this.onChange = onChange;
        this.counter = counter;
        exampleCard = new Card(T("Example"), "");
        exampleCard.Description.IsTextSelectionEnabled = true;
        Rebuild();
    }

    private static string[] SeparatorLabels() =>
        [T("Space"), T("Underscore (_)"), T("Hyphen (-)"), T("Spaced hyphen ( - )")];

    public static string KindTitle(string kind) => kind switch
    {
        BlockKind.Date => T("Date"),
        BlockKind.Counter => T("Progressive number"),
        BlockKind.Name => T("Project name"),
        BlockKind.Text => T("Fixed text"),
        BlockKind.Field => T("Field to fill in"),
        _ => T("Choice from a list"),
    };

    private int Counter() => counter?.Invoke() ?? 1;

    /// <summary>Redraw, e.g. after the counter changed.</summary>
    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        Panel.Children.Clear();

        var separator = Ui.Combo(SeparatorLabels(), Math.Max(0, Array.IndexOf(NameFormat.Separators, format.Separator)));
        separator.SelectionChanged += (_, _) =>
        {
            if (separator.SelectedIndex < 0)
                return;
            format.Separator = NameFormat.Separators[separator.SelectedIndex];
            Changed(rebuild: false);
        };
        Panel.Children.Add(new Card(T("Separator"), null, separator));

        for (var i = 0; i < format.Blocks.Count; i++)
            Panel.Children.Add(BlockCard(i, format.Blocks[i]));

        // "Add block" with a menu of block kinds.
        var menu = new MenuFlyout();
        foreach (var kind in BlockKind.Custom)
        {
            var item = new MenuFlyoutItem { Text = KindTitle(kind) };
            item.Click += (_, _) => Add(kind);
            menu.Items.Add(item);
        }
        var addButton = new DropDownButton
        {
            Content = Ui.Row(Ui.Icon(Glyph.Add, 12), new TextBlock { Text = T("Add block") }),
            Flyout = menu,
        };
        Panel.Children.Add(new Card(T("Add block"), T("Fixed text, field to fill in or choice"), addButton));

        Panel.Children.Add(exampleCard);
        UpdateExample();
    }

    private string Subtitle(Block block)
    {
        switch (block.Kind)
        {
            case BlockKind.Date:
                return DateTime.Now.ToString("yyyy-MM-dd");
            case BlockKind.Counter:
                return counter is not null ? $"{Counter()}{Naming.CounterSuffix}" : T("Each template counts on its own");
            case BlockKind.Name:
                return T("Typed when creating the project");
            case BlockKind.Text:
                return block.Value;
        }
        var detail = KindTitle(block.Kind);
        if (block.Kind == BlockKind.Choice)
            detail += ": " + string.Join(", ", block.Options);
        if (block.Value.Length > 0)
            detail += " · " + T("default: {value}", ("value", block.Value));
        return detail;
    }

    private Card BlockCard(int index, Block block)
    {
        var custom = BlockKind.IsCustom(block.Kind);
        var title = block.Kind is BlockKind.Field or BlockKind.Choice ? block.Label : KindTitle(block.Kind);
        var controls = Ui.Row();

        if (block.Kind is BlockKind.Date or BlockKind.Counter)
        {
            var toggle = new ToggleSwitch { IsOn = block.Enabled, OnContent = "", OffContent = "", MinWidth = 0 };
            ToolTipService.SetToolTip(toggle, T("Included by default"));
            toggle.Toggled += (_, _) =>
            {
                block.Enabled = toggle.IsOn;
                Changed(rebuild: false);
            };
            controls.Children.Add(toggle);
        }
        if (custom)
            controls.Children.Add(Ui.IconButton(Glyph.Edit, T("Edit"), () => Edit(index)));
        controls.Children.Add(Ui.IconButton(Glyph.Up, T("Move up"), () => Move(index, -1), index > 0));
        controls.Children.Add(Ui.IconButton(Glyph.Down, T("Move down"), () => Move(index, 1),
                                            index < format.Blocks.Count - 1));
        if (custom)
            controls.Children.Add(Ui.IconButton(Glyph.Delete, T("Remove"), () => Remove(index)));

        return new Card(title, Subtitle(block), controls);
    }

    private void UpdateExample() =>
        exampleCard.SetDescription(Naming.ExampleName(format, counter: Counter()));

    // Changes

    private void Changed(bool rebuild = true)
    {
        onChange();
        if (rebuild)
            Rebuild();
        else
            UpdateExample();
    }

    private async void Add(string kind)
    {
        var block = await BlockDialog.Edit(Panel.XamlRoot, new Block(kind));
        if (block is null)
            return;
        format.Blocks.Add(block);
        Changed();
    }

    private async void Edit(int index)
    {
        var block = await BlockDialog.Edit(Panel.XamlRoot, format.Blocks[index].Clone());
        if (block is null)
            return;
        format.Blocks[index] = block;
        Changed();
    }

    private void Move(int index, int delta)
    {
        (format.Blocks[index], format.Blocks[index + delta]) = (format.Blocks[index + delta], format.Blocks[index]);
        Changed();
    }

    private void Remove(int index)
    {
        format.Blocks.RemoveAt(index);
        Changed();
    }
}

/// <summary>Asks for the settings of a custom block.</summary>
internal static class BlockDialog
{
    private static List<string> ParseOptions(string text) =>
        [.. text.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).Distinct()];

    /// <summary>The edited block, or null if the dialog was cancelled.</summary>
    public static async Task<Block?> Edit(XamlRoot root, Block block)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        var boxes = new Dictionary<string, TextBox>();

        void Entry(string key, string header, string text)
        {
            boxes[key] = new TextBox { Header = header, Text = text };
            panel.Children.Add(boxes[key]);
        }

        string body;
        if (block.Kind == BlockKind.Text)
        {
            body = T("Always added to the name, e.g. a studio or language code.");
            Entry("value", T("Text"), block.Value);
        }
        else
        {
            body = block.Kind == BlockKind.Field
                ? T("Asked when creating the project; left empty, it is skipped.")
                : T("Picked from a menu when creating the project, so names stay consistent; " +
                    "it can also be left empty.");
            Entry("label", T("Label, e.g. Client"), block.Label);
            if (block.Kind == BlockKind.Choice)
                Entry("options", T("Values, separated by commas"), string.Join(", ", block.Options));
            Entry("value", T("Default value (optional)"), block.Value);
        }
        panel.Children.Insert(0, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });

        var dialog = Ui.Dialog(root, FormatEditor.KindTitle(block.Kind));
        dialog.Content = panel;
        dialog.PrimaryButtonText = T("Save");
        dialog.CloseButtonText = T("Cancel");

        string Text(string key) => boxes.TryGetValue(key, out var box) ? box.Text.Trim() : "";

        void Validate()
        {
            var options = ParseOptions(Text("options"));
            var ok = boxes.Values.All(b => !Naming.HasInvalidChars(b.Text));
            ok &= block.Kind == BlockKind.Text ? Text("value").Length > 0 : Text("label").Length > 0;
            if (block.Kind == BlockKind.Choice)
                ok &= options.Count > 0 && (Text("value").Length == 0 || options.Contains(Text("value")));
            dialog.IsPrimaryButtonEnabled = ok;
        }

        foreach (var box in boxes.Values)
            box.TextChanged += (_, _) => Validate();
        Validate();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;
        block.Value = Text("value");
        block.Label = Text("label");
        block.Options = ParseOptions(Text("options"));
        return block;
    }
}
