using System.Windows.Input;
using CommunityToolkit.Maui.Behaviors;

namespace Wired.App.Controls;

/// <summary>
/// Row container with tap (<see cref="Command"/>) and long press (<see cref="LongPressCommand"/>),
/// plus pressed feedback. Wraps the Toolkit's TouchBehavior so XAML binds plain properties with
/// compiled bindings (behaviors do not inherit the binding context).
/// </summary>
public sealed class Pressable : ContentView
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(Pressable),
        propertyChanged: (bindable, _, value) => ((Pressable)bindable)._touch.Command = (ICommand?)value);

    public static readonly BindableProperty LongPressCommandProperty = BindableProperty.Create(
        nameof(LongPressCommand), typeof(ICommand), typeof(Pressable),
        propertyChanged: (bindable, _, value) => ((Pressable)bindable)._touch.LongPressCommand = (ICommand?)value);

    private readonly TouchBehavior _touch = new()
    {
        PressedOpacity = 0.6,
        DefaultAnimationDuration = 100,
        LongPressDuration = 450,
        // Rows contain their own buttons (like, delete...): they must stay tappable.
        ShouldMakeChildrenInputTransparent = false,
    };

    public Pressable() => Behaviors.Add(_touch);

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ICommand? LongPressCommand
    {
        get => (ICommand?)GetValue(LongPressCommandProperty);
        set => SetValue(LongPressCommandProperty, value);
    }
}
