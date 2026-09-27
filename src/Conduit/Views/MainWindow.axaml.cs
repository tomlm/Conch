using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Conduit.Controls;
using Conduit.Utilities;
using Conduit.ViewModel;

namespace Conduit.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnExit(object sender, RoutedEventArgs e)
        {
            this.Close();

            //var lifetime = Application.Current!.ApplicationLifetime as IControlledApplicationLifetime;
            //lifetime!.Shutdown();
        }

        private void OnManageApps(object? sender, RoutedEventArgs e)
        {
            var dialog = new AppManagerDialog((AppViewModel)this.DataContext!);
            dialog.Show(Windows);
        }

        private void OnNewTerminal(object? sender, RoutedEventArgs e)
        {
            var terminalWindow = new ManagedTerminalWindow
            {
                Width = 80,
                Height = 25,
                FontFamily = "Cascadia Mono",
                CloseOnProcessExit = true
            };
            terminalWindow.Show(Windows);
        }


        private async void OnCustomTerminal(object? sender, RoutedEventArgs e)
        {
            var dialog = new CommandLineDialog();
            var result = await dialog.ShowDialog<bool?>(this);

            if (result == true && !string.IsNullOrWhiteSpace(dialog.CommandLine))
            {
                var commandLine = dialog.CommandLine.Trim();
                var parts = ParseCommandLine(commandLine);
                string process = "cmd.exe";
                List<string> args = new List<string>();
                if (parts.Count > 0)
                {
                    var processPath = PathUtils.ResolveOnPath(parts[0]);
                    if (processPath != null)
                    {
                        if (String.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            process = processPath;
                            args = parts.GetRange(1, parts.Count - 1).ToList();
                        }
                        else if (String.Equals(Path.GetExtension(processPath), ".cmd", StringComparison.OrdinalIgnoreCase))
                        {
                            process = "cmd.exe";
                            args.Add("/c");
                            args.Add(processPath);
                            args.AddRange(parts.GetRange(1, parts.Count - 1));
                        }
                        else
                        {
                            // for non-exe files, try to run via wsl
                            process = "wsl";
                            args = parts;
                        }
                    }
                    else
                    {
                        process = "wsl";
                        args = parts;
                    }
                }

                var terminalWindow = new ManagedTerminalWindow
                {
                    Process = process,
                    ProcessArgs = args,
                    Title = process,
                    Width = 80,
                    Height = 25,
                    FontFamily = "Cascadia Mono",
                    CloseOnProcessExit = true
                };
                terminalWindow.Show(Windows);
            }
        }



        private static List<string> ParseCommandLine(string commandLine)
        {
            var args = new List<string>();
            var current = "";
            var inQuotes = false;

            foreach (var c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        args.Add(current);
                        current = "";
                    }
                }
                else
                {
                    current += c;
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                args.Add(current);
            }

            return args;
        }
    }
}