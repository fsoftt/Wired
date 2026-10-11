using Wired.Core.Plugins;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class PluginCatalogTests
{
    [Fact]
    public void Get_returns_plugin_by_id()
    {
        var demo = new FakePlugin("demo");
        var catalog = new PluginCatalog([new FakePlugin("other"), demo]);

        Assert.Same(demo, catalog.Get("demo"));
    }

    [Fact]
    public void Get_throws_for_unknown_plugin()
    {
        var catalog = new PluginCatalog([new FakePlugin("demo")]);

        var error = Assert.Throws<PluginNotFoundException>(() => catalog.Get("soundcloud"));
        Assert.Equal("soundcloud", error.PluginId);
    }

    [Fact]
    public void Duplicated_ids_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => new PluginCatalog([new FakePlugin("a"), new FakePlugin("a")]));
    }
}
