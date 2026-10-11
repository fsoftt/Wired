using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using Wired.Core.Localization;

namespace Wired.Tests;

public class LocalizationTests
{
    private static readonly string[] Cultures = ["en", "pt"];

    [Fact]
    public void Every_key_exists_in_every_language_and_none_is_empty()
    {
        var spanish = ReadKeys("Strings.resx");
        Assert.NotEmpty(spanish);
        Assert.DoesNotContain(spanish, pair => string.IsNullOrWhiteSpace(pair.Value));

        foreach (var culture in Cultures)
        {
            var translated = ReadKeys($"Strings.{culture}.resx");

            Assert.Empty(spanish.Keys.Except(translated.Keys));
            Assert.Empty(translated.Keys.Except(spanish.Keys));
            Assert.DoesNotContain(translated, pair => string.IsNullOrWhiteSpace(pair.Value));
        }
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var spanish = ReadKeys("Strings.resx");

        foreach (var culture in Cultures)
        {
            var translated = ReadKeys($"Strings.{culture}.resx");
            foreach (var (key, text) in spanish)
                Assert.True(Placeholders(text).SequenceEqual(Placeholders(translated[key])), $"{culture}: {key}");
        }
    }

    [Fact]
    public void Compiled_resources_translate_each_language()
    {
        var resources = new ResourceManager("Wired.Core.Localization.Strings", typeof(L).Assembly);

        Assert.Equal("Reproducir", resources.GetString("Common_Play", CultureInfo.GetCultureInfo("es")));
        Assert.Equal("Play", resources.GetString("Common_Play", CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Reproduzir", resources.GetString("Common_Play", CultureInfo.GetCultureInfo("pt")));
        Assert.Equal("Reproduzir", resources.GetString("Common_Play", CultureInfo.GetCultureInfo("pt-BR")));
    }

    [Theory]
    [InlineData("", "es-CO", "es-CO")]
    [InlineData("", "pt-BR", "pt-BR")]
    [InlineData("", "en-GB", "en-GB")]
    [InlineData("", "fr-FR", "en")]
    [InlineData("pt", "es-CO", "pt")]
    [InlineData("xx", "fr-FR", "en")]
    public void Culture_follows_the_choice_or_the_device(string code, string device, string expected) =>
        Assert.Equal(expected, AppLanguage.CultureFor(code, CultureInfo.GetCultureInfo(device)).Name);

    [Fact]
    public void Counts_use_the_singular_only_for_one()
    {
        Assert.Equal("1 canción", L.N("Songs", 1));
        Assert.Equal("0 canciones", L.N("Songs", 0));
        Assert.Equal("5 canciones", L.N("Songs", 5));
    }

    [Fact]
    public void A_missing_key_shows_the_key()
    {
        Assert.Equal("No_Such_Key", L.T("No_Such_Key"));
    }

    private static string[] Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();

    private static Dictionary<string, string> ReadKeys(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Wired.Core", "Localization", fileName)))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "Wired.Core", "Localization", fileName);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
    }
}
