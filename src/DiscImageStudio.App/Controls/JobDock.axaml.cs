using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Controls;

public partial class JobDock : UserControl
{
    private ShellViewModel? _shell;

    public JobDock()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachShell();
        ActualThemeVariantChanged += (_, _) => UpdateStatusIcon();
        AttachedToVisualTree += (_, _) =>
        {
            AttachShell();
            LogTextBox.Text = _shell?.LogText ?? string.Empty;
            UpdateStatusIcon();
            ScrollToEnd();
        };
    }

    private void AttachShell()
    {
        if (ReferenceEquals(_shell, DataContext)) return;
        if (_shell is not null)
        {
            _shell.LogAppended -= OnLogAppended;
            _shell.LogCleared -= OnLogCleared;
            _shell.PropertyChanged -= OnShellPropertyChanged;
        }

        _shell = DataContext as ShellViewModel;
        if (_shell is not null)
        {
            _shell.LogAppended += OnLogAppended;
            _shell.LogCleared += OnLogCleared;
            _shell.PropertyChanged += OnShellPropertyChanged;
        }

        UpdateStatusIcon();
    }

    private void OnLogAppended(string text)
    {
        LogTextBox.Text += text;
        ScrollToEnd();
    }

    private void OnLogCleared() => LogTextBox.Text = string.Empty;

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsJobRunning) or nameof(ShellViewModel.JobHasResult)
            or nameof(ShellViewModel.IsJobDockExpanded))
        {
            UpdateStatusIcon();
            if (e.PropertyName is nameof(ShellViewModel.IsJobDockExpanded) && _shell?.IsJobDockExpanded == true)
            {
                LogTextBox.Text = _shell.LogText;
                ScrollToEnd();
            }
        }
    }

    private void UpdateStatusIcon()
    {
        if (_shell is null) return;
        (string kind, string brush) = _shell.IsJobRunning
            ? ("LoaderCircle", "PrimaryBrush")
            : _shell.JobHasResult ? ("CircleCheck", "SuccessBrush") : ("CircleDashed", "TextMutedBrush");
        StatusIcon.Kind = kind;
        if (this.TryFindResource(brush, ActualThemeVariant, out object? value) && value is Avalonia.Media.IBrush resolved)
        {
            StatusIcon.Foreground = resolved;
        }
    }

    private void ScrollToEnd()
        => Dispatcher.UIThread.Post(() => LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0, DispatcherPriority.Background);
}
