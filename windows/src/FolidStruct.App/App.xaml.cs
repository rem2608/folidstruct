using FolidStruct.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using static FolidStruct.Core.I18n;

namespace FolidStruct.App;

public partial class App : Application
{
    public static new App? Current { get; private set; }

    private ManagerWindow? manager;
    private DispatcherQueue? dispatcher;

    public App()
    {
        Current = this;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Native.ShowError("FolidStruct", e.Exception.Message);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        var launch = Program.Args;
        Config config;
        try
        {
            // The File Explorer menu lists the templates of this file.
            Storage.EnsureConfig();
            config = Storage.LoadConfig();
        }
        catch (Exception e) when (e is AppException or IOException or UnauthorizedAccessException)
        {
            Native.ShowError(launch.NewProject ? T("Cannot create the project") : "FolidStruct", e.Message);
            Exit();
            return;
        }
        SetLanguage(config.Language);

        if (!launch.NewProject)
        {
            manager = new ManagerWindow(config);
            manager.Activate();
            return;
        }

        if (config.Templates.Count == 0)
        {
            Native.ShowError(T("No templates"), T("Open FolidStruct from the Start menu to create one."));
            Exit();
            return;
        }
        var template = config.Templates.FirstOrDefault(t => t.Id == launch.TemplateId)
                       ?? config.FindTemplate(launch.TemplateName)
                       ?? config.Templates[0];
        var directory = launch.Directory ?? Environment.CurrentDirectory;
        new NewProjectWindow(config, template, Path.GetFullPath(directory)).Activate();
    }

    /// <summary>Replace the manager window, e.g. after a language change, in the same place.</summary>
    public void ReopenManager(string page)
    {
        if (manager is not { } old)
            return;
        manager = new ManagerWindow(old.Config, page);
        manager.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            old.AppWindow.Position.X, old.AppWindow.Position.Y, old.AppWindow.Size.Width, old.AppWindow.Size.Height));
        if (old.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized }
            && manager.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();
        manager.Activate();
        old.Close();
    }

    /// <summary>Bring the manager forward; called when FolidStruct is launched again.</summary>
    public void ShowManager() => dispatcher?.TryEnqueue(() => manager?.BringToFront());
}
