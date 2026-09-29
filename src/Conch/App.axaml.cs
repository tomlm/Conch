using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Conch.Services;
using Conch.ViewModel;
using Conch.Views;

namespace Conch
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                var appViewModel = new AppViewModel();

                // Before the window is built, which is the only moment this is free: replacing
                // the theme later means tearing down the panel that holds every open app.
                ConsoleThemes.ApplyAtStartup(Styles, appViewModel.Settings.Theme);

                desktopLifetime.MainWindow = new MainWindow
                {
                    DataContext = appViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}