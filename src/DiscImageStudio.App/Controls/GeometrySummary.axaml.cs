using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

public partial class GeometrySummary : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<GeometrySummary, string>(nameof(Title), "盘片几何");
    public static readonly StyledProperty<string> DetailsProperty =
        AvaloniaProperty.Register<GeometrySummary, string>(nameof(Details), string.Empty);
    public static readonly StyledProperty<string> NoteProperty =
        AvaloniaProperty.Register<GeometrySummary, string>(nameof(Note), string.Empty);
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<GeometrySummary, ICommand?>(nameof(EditCommand));
    public static readonly StyledProperty<object?> EditCommandParameterProperty =
        AvaloniaProperty.Register<GeometrySummary, object?>(nameof(EditCommandParameter));
    public static readonly StyledProperty<string> EditLabelProperty =
        AvaloniaProperty.Register<GeometrySummary, string>(nameof(EditLabel), "编辑…");

    public GeometrySummary()
    {
        InitializeComponent();
        UpdateVisuals();
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Details { get => GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public string Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }
    public ICommand? EditCommand { get => GetValue(EditCommandProperty); set => SetValue(EditCommandProperty, value); }
    public object? EditCommandParameter { get => GetValue(EditCommandParameterProperty); set => SetValue(EditCommandParameterProperty, value); }
    public string EditLabel { get => GetValue(EditLabelProperty); set => SetValue(EditLabelProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.OwnerType == typeof(GeometrySummary)) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        TitleText.Text = Title;
        DetailsText.Text = Details;
        DetailsText.IsVisible = !string.IsNullOrWhiteSpace(Details);
        PitchText.Text = Note;
        PitchText.IsVisible = !string.IsNullOrWhiteSpace(Note);
        EditButton.Content = EditLabel;
        EditButton.Command = EditCommand;
        EditButton.CommandParameter = EditCommandParameter;
        EditButton.IsVisible = EditCommand is not null;
    }
}
