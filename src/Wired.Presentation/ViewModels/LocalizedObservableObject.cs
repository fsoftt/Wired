using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Wired.Presentation.Messages;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// A view model whose texts are built from the current language. When the user changes the language every property
/// is notified again, so bound labels are read again in the new language without reopening the screen.
/// </summary>
public abstract class LocalizedObservableObject : ObservableObject
{
    protected LocalizedObservableObject() =>
        WeakReferenceMessenger.Default.Register<LocalizedObservableObject, LanguageChangedMessage>(
            this, static (self, _) => self.OnPropertyChanged(string.Empty));
}
