using System;
using System.Collections.Specialized;
using Avalonia.Interactivity;
using Conch.Utilities;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>
/// Shows the shell's diagnostics. Conch draws a TUI over stdout, so console output is
/// invisible; when Conch is the only UI on the machine this window is the only way to read
/// what went wrong without shelling out to the log file.
/// </summary>
public partial class LogDialog : ManagedWindow
{
    public LogDialog()
    {
        InitializeComponent();

        LogPathText.Text = $"Log file: {Log.LogFilePath}";
        EntriesListBox.ItemsSource = Log.Entries;

        Log.Entries.CollectionChanged += OnEntriesChanged;
        Closed += OnClosed;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e) => ScrollToEnd();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        if (Log.Entries.Count > 0)
        {
            EntriesListBox.ScrollIntoView(Log.Entries.Count - 1);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Log.Entries outlives this window; leaving the handler attached would leak it.
        Log.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
