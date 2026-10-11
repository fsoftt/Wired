namespace Wired.App.Localization;

/// <summary>Use in XAML: <c>Text="{l:Translate Common_Play}"</c>. It updates by itself when the language changes.</summary>
[ContentProperty(nameof(Key))]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]", BindingMode.OneWay, source: LocalizationManager.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
