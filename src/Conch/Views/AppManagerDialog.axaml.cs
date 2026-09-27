using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Conduit.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conduit.Views;

public partial class AppManagerDialog : ManagedWindow
{
    private AppViewModel? _appViewModel;

    public AppManagerDialog(AppViewModel appViewModel)
    {
        InitializeComponent();
        Opened += OnOpened;
        this.DataContext = new AppManagerViewModel(appViewModel);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        SearchBox.Focus();
    }
  
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
