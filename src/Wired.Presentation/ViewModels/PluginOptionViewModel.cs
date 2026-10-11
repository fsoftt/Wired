using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Plugins;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// A selectable chip in the search screen representing a music source.
/// </summary>
public sealed partial class PluginOptionViewModel(PluginInfo info, Action<PluginOptionViewModel> onSelected)
    : ObservableObject
{
    public PluginInfo Info { get; } = info;

    public string Id => Info.Id;

    public string Name => Info.DisplayName;

    public string Description => Info.Description;

    public string AccentColor => Info.AccentColor;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => onSelected(this);
}
