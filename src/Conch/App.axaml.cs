using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                desktopLifetime.MainWindow = new MainWindow
                {
                    DataContext = new AppViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}