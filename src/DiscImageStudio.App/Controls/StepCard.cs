using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

/// <summary>Numbered step card for guided workflows: a state chip beside the title and a
/// collapsible body. State drives the :active / :done pseudo-classes used by its theme.</summary>
public class StepCard : Expander
{
    public static readonly StyledProperty<int> StepNumberProperty =
        AvaloniaProperty.Register<StepCard, int>(nameof(StepNumber), 1);
    public static readonly StyledProperty<string> StepTitleProperty =
        AvaloniaProperty.Register<StepCard, string>(nameof(StepTitle), string.Empty);
    public static readonly StyledProperty<StepCardState> StateProperty =
        AvaloniaProperty.Register<StepCard, StepCardState>(nameof(State), StepCardState.Pending);

    public StepCard() => UpdateState();

    public int StepNumber { get => GetValue(StepNumberProperty); set => SetValue(StepNumberProperty, value); }
    public string StepTitle { get => GetValue(StepTitleProperty); set => SetValue(StepTitleProperty, value); }
    public StepCardState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty) UpdateState();
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":active", State == StepCardState.Active);
        PseudoClasses.Set(":done", State == StepCardState.Done);
    }
}
