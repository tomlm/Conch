using Avalonia.Input;
using Avalonia.Interactivity;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>
/// Asks yes or no -- or just says something, with <see cref="Inform"/>.
/// </summary>
/// <remarks>
/// In place of Consolonia's MessageBox, which gives Esc nothing to do -- so "Log out?" could
/// not be answered No from the keyboard -- and which crashed Conch when a second key reached
/// it after it had answered: an access key, then Enter, both completing the same result.
/// This closes once, with Enter on Yes and Esc on No, through the window manager's own
/// default and cancel buttons.
/// </remarks>
public partial class ConfirmDialog : ManagedWindow
{
    private bool _answered;

    public ConfirmDialog(string title, string question)
    {
        InitializeComponent();

        Title = title;
        QuestionText.Text = question;
        Opened += (_, _) => YesButton.Focus();

        // Avalonia fires access keys only with Alt held; a yes/no question should take the
        // bare letter, as every dialog of this kind does.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & ~KeyModifiers.Alt) != KeyModifiers.None || !NoButton.IsVisible)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Y:
                e.Handled = true;
                Answer(true);
                break;
            case Key.N:
                e.Handled = true;
                Answer(false);
                break;
        }
    }

    /// <summary>A message with only OK, which Enter and Esc both press.</summary>
    public static ConfirmDialog Inform(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        dialog.YesButton.Content = "_OK";
        dialog.YesButton.IsCancel = true;
        dialog.NoButton.IsVisible = false;
        return dialog;
    }

    private void OnYes(object? sender, RoutedEventArgs e) => Answer(true);

    private void OnNo(object? sender, RoutedEventArgs e) => Answer(false);

    private void Answer(bool yes)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        Close(yes);
    }
}
