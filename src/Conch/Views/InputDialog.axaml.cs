using Avalonia.Interactivity;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>Asks for one line of text, for <c>conch input</c>.</summary>
public partial class InputDialog : ManagedWindow
{
    public InputDialog(string title, string prompt, string? initial)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        AnswerBox.Text = initial ?? string.Empty;

        Opened += (_, _) =>
        {
            AnswerBox.Focus();
            AnswerBox.SelectAll();
        };
    }

    /// <summary>Closes with the text typed, which may be empty -- an answer, not a cancel.</summary>
    private void OnOk(object? sender, RoutedEventArgs e) => Close(AnswerBox.Text ?? string.Empty);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
