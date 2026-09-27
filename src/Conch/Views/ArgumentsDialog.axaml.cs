using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Interactivity;
using Conch.Utilities;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>
/// Collects the values for a registration's <c>args</c> placeholders.
/// </summary>
public partial class ArgumentsDialog : ManagedWindow
{
    private readonly string? _template;

    /// <summary>
    /// The values the user supplied, split positionally.
    /// </summary>
    public IReadOnlyList<string> Values { get; private set; } = Array.Empty<string>();

    public ArgumentsDialog(ToolViewModel tool)
    {
        InitializeComponent();

        _template = tool.Args;
        Title = tool.Name;

        var parameters = ArgumentTemplate.GetParameters(_template);
        var required = parameters.Where(p => !p.IsOptional).ToList();

        PromptText.Text = required.Count == 0
            ? $"Arguments for {tool.Name} (optional):"
            : $"Arguments for {tool.Name} ({required.Count} required):";

        Opened += (s, e) => ArgumentsTextBox.Focus();
    }

    private void OnLaunchClicked(object? sender, RoutedEventArgs e)
    {
        var values = ArgumentTemplate.Tokenize(ArgumentsTextBox.Text ?? string.Empty);

        // Validate here so a missing required argument is reported in the dialog the user is
        // already looking at, rather than as a window that opens and immediately dies.
        if (!ArgumentTemplate.TryBuild(_template, values, out _, out var error))
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        Values = values;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
