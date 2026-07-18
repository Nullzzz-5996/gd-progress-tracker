using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class AccountStatsParserTests
{
    // Структура реального CCGameManager.dat: числовые ключи вперемешку с unique_<id>_<coin>.
    private const string Sample =
        "<?xml version=\"1.0\"?><plist version=\"1.0\" gjver=\"2.0\"><dict>" +
        "<k>GLM_01</k><d><k>1</k><d><k>k1</k><i>1</i></d></d>" +
        "<k>GS_value</k><d>" +
            "<k>1</k><i>258487</i>" +      // прыжки
            "<k>2</k><i>43329</i>" +       // попытки
            "<k>3</k><i>27</i>" +          // официальные уровни
            "<k>4</k><i>322</i>" +         // онлайн-уровни
            "<k>5</k><i>17</i>" +          // демоны
            "<k>6</k><i>886</i>" +         // звёзды
            "<k>8</k><i>84</i>" +          // секретные монеты
            "<k>12</k><i>82</i>" +         // пользовательские монеты (не путать с 8)
            "<k>14</k><i>10682</i>" +      // текущий баланс сфер (не путать с 22)
            "<k>22</k><i>49359</i>" +      // сферы за всё время
            "<k>28</k><i>84</i>" +         // луны
            "<k>99</k><i>7</i>" +          // неизвестный ключ — должен попасть только в RawValues
            "<k>unique_128_1</k><i>1</i>" +
            "<k>unique_128_2</k><i>1</i>" +
        "</d>" +
        "</dict></plist>";

    [Fact]
    public void Reads_all_nine_displayed_metrics()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats.Should().NotBeNull();
        stats!.Jumps.Should().Be(258487);
        stats.Attempts.Should().Be(43329);
        stats.OfficialLevelsCompleted.Should().Be(27);
        stats.OnlineLevelsCompleted.Should().Be(322);
        stats.Demons.Should().Be(17);
        stats.Stars.Should().Be(886);
        stats.SecretCoins.Should().Be(84);
        stats.TotalOrbs.Should().Be(49359);
        stats.Moons.Should().Be(84);
    }

    [Fact]
    public void Secret_coins_and_total_orbs_are_not_confused_with_their_neighbours()
    {
        var stats = AccountStatsParser.Parse(Sample);

        // Ключ 8 — секретные монеты (84), ключ 12 — пользовательские (82).
        stats!.SecretCoins.Should().Be(84);
        // Ключ 22 — сферы за всё время (49359), ключ 14 — текущий баланс (10682).
        stats.TotalOrbs.Should().Be(49359);
    }

    [Fact]
    public void Ignores_non_numeric_unique_coin_keys()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats!.RawValues.Should().NotContainKey("unique_128_1");
        stats.RawValues.Keys.Should().OnlyContain(k => k.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Keeps_unknown_numeric_keys_in_raw_values()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats!.RawValues["99"].Should().Be(7);
        stats.RawValues["12"].Should().Be(82);
    }

    [Fact]
    public void Missing_keys_default_to_zero()
    {
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GS_value</k><d><k>6</k><i>5</i></d></dict></plist>";

        var stats = AccountStatsParser.Parse(xml);

        stats!.Stars.Should().Be(5);
        stats.Moons.Should().Be(0);      // ключ 28 отсутствует в сейвах до 2.2
        stats.Demons.Should().Be(0);
    }

    [Fact]
    public void Returns_null_when_gs_value_block_is_absent()
    {
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GLM_01</k><d><k>1</k><d><k>k1</k><i>1</i></d></d></dict></plist>";

        AccountStatsParser.Parse(xml).Should().BeNull();
    }

    [Fact]
    public void Self_closing_empty_gs_value_yields_zeroed_stats_not_null()
    {
        // GS_value — самозакрывающийся пустой словарь <d/>: блок есть, просто пуст.
        // null здесь означал бы «блока нет», что неверно.
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GS_value</k><d/></dict></plist>";

        var stats = AccountStatsParser.Parse(xml);

        stats.Should().NotBeNull();
        stats!.Jumps.Should().Be(0);
        stats.Attempts.Should().Be(0);
        stats.OfficialLevelsCompleted.Should().Be(0);
        stats.OnlineLevelsCompleted.Should().Be(0);
        stats.Demons.Should().Be(0);
        stats.Stars.Should().Be(0);
        stats.SecretCoins.Should().Be(0);
        stats.TotalOrbs.Should().Be(0);
        stats.Moons.Should().Be(0);
        stats.RawValues.Should().BeEmpty();
    }

    [Fact]
    public void Self_closing_empty_gs_value_followed_by_other_dict_keys_does_not_throw()
    {
        // После пустого GS_value в документе идут другие ключи со словарями —
        // ровно как в реальном CCGameManager.dat. Скан не должен «уехать» дальше
        // самозакрывающегося <d/> и склеить следующий словарь в тот же фрагмент.
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GS_value</k><d/>" +
                  "<k>after</k><d><k>x</k><i>1</i></d>" +
                  "</dict></plist>";

        var act = () => AccountStatsParser.Parse(xml);

        act.Should().NotThrow();
        var stats = act();
        stats.Should().NotBeNull();
        stats!.RawValues.Should().BeEmpty();
    }

    [Fact]
    public void Non_dict_gs_value_returns_null_without_throwing()
    {
        // Если значением GS_value оказывается не словарь, а скажем целое число,
        // сканер должен вернуть null, не цепляясь за следующий попавшийся словарь.
        // Раньше это приводило к XmlException: There are multiple root elements.
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GS_value</k><i>5</i>" +
                  "<k>after</k><d><k>x</k><i>1</i></d>" +
                  "</dict></plist>";

        var act = () => AccountStatsParser.Parse(xml);

        act.Should().NotThrow();
        var stats = act();
        stats.Should().BeNull();
    }
}
