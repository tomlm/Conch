using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

public partial class AppManagerDialog : ManagedWindow
{
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
