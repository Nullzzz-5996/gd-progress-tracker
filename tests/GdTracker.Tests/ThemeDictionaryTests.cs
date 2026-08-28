using System.Xml.Linq;
using FluentAssertions;

namespace GdTracker.Tests;

/// <summary>
/// Словари тем обязаны разрешать одни и те же ключи приложения: при переключении темы
/// неразрешённый ключ оставит элемент без кисти (и, скорее всего, невидимым). Неоновая
/// тема вдобавок переопределяет ключи самой WPF-UI, поэтому она — надмножество тёмной.
/// </summary>
public class ThemeDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IReadOnlyCollection<string> KeysOf(string themeFile)
    {
        var path = Path.Combine(RepoRoot(), "src", "GdTracker.App", "Themes", themeFile);
        File.Exists(path).Should().BeTrue($"словарь темы {themeFile} должен лежать в Themes");

        return XDocument.Load(path).Descendants()
            .Select(e => e.Attribute(X + "Key")?.Value)
            .Where(key => key is not null)
            .Select(key => key!)
            .ToList();
    }

    /// <summary>Корень репозитория ищется вверх от каталога сборки по файлу решения.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GdTracker.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("тест ищет корень репозитория по GdTracker.slnx");
        return dir!.FullName;
    }

    [Fact]
    public void Dark_and_light_themes_define_exactly_the_same_keys()
    {
        KeysOf("Light.xaml").Should().BeEquivalentTo(KeysOf("Dark.xaml"));
    }

    [Fact]
    public void Neon_theme_defines_every_key_the_dark_theme_does()
    {
        KeysOf("Neon.xaml").Should().Contain(KeysOf("Dark.xaml"));
    }

    [Fact]
    public void No_theme_defines_the_same_key_twice()
    {
        foreach (var theme in new[] { "Dark.xaml", "Light.xaml", "Neon.xaml" })
        {
            var keys = KeysOf(theme);
            keys.Should().OnlyHaveUniqueItems($"в {theme} повторный ключ молча затирает предыдущий");
        }
    }
}
