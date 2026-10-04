using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>
/// Picks the app to open a file with, optionally remembering it for the file's type.
/// </summary>
public partial class OpenWithDialog : ManagedWindow
{
    private readonly IReadOnlyList<OpenWithChoice> _choices;

    /// <summary>What was chosen, once the dialog closes with OPEN.</summary>
    public OpenWithChoice? Chosen { get; private set; }

    /// <summary>Whether to open this type with <see cref="Chosen"/> from now on.</summary>
    public bool Always { get; private set; }

    public OpenWithDialog(string path, IReadOnlyList<OpenWithChoice> choices)
    {
        InitializeComponent();

        _choices = choices;
        PromptText.Text = $"Open {Path.GetFileName(path)} with:";
        ChoicesBox.ItemsSource = choices;
        ChoicesBox.SelectedIndex = 0;

        var extension = Services.FileOpeners.Extension(path);
        AlwaysBox.Content = $"Always open {extension} files with this";
        ChoicesBox.SelectionChanged += (_, _) => UpdateAlways();
        UpdateAlways();

        Opened += (_, _) => ChoicesBox.Focus();
    }

    private void UpdateAlways()
    {
        var canRemember = (ChoicesBox.SelectedItem as OpenWithChoice)?.CanRemember == true;
        AlwaysBox.IsVisible = _choices.Any(c => c.CanRemember);
        AlwaysBox.IsEnabled = canRemember;
        if (!canRemember)
        {
            AlwaysBox.IsChecked = false;
        }
    }

    private void OnChoiceDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void OnOpenClicked(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void Accept()
    {
        if (ChoicesBox.SelectedItem is not OpenWithChoice choice)
        {
            return;
        }

        Chosen = choice;
        Always = choice.CanRemember && AlwaysBox.IsChecked == true;
        Close(true);
    }
}
