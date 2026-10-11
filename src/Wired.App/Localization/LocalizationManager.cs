using System.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Localization;
using Wired.Presentation.Messages;

namespace Wired.App.Localization;

/// <summary>
/// Source of the XAML bindings created by <see cref="TranslateExtension"/>: when the language changes it notifies
/// every binding so labels are read again, with no restart.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged, IRecipient<LanguageChangedMessage>
{
    public static LocalizationManager Instance { get; } = new();

    private LocalizationManager() => WeakReferenceMessenger.Default.Register(this);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => L.T(key);

    /// <summary>Applies the saved language. Call it first thing when the app starts.</summary>
    public static void Initialize() => L.Apply(Preferences.Default.Get(AppLanguage.PreferenceKey, string.Empty));

    public void Receive(LanguageChangedMessage message)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
