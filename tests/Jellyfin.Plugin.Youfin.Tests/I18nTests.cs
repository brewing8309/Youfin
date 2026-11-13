using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>Guards translations: every key used by the UI exists in English, no dead keys, every language is complete.</summary>
public class I18nTests
{
    private static readonly System.Reflection.Assembly Asm = typeof(Jellyfin.Plugin.Youfin.Core.YouTubeIdParser).Assembly;
    private static readonly Regex Plural = new(@"_(zero|one|two|few|many|other)$");
    private static readonly Regex Placeholder = new(@"\{(\w+)\}");

    private static string Read(string resource)
    {
        using var s = Asm.GetManifestResourceStream("Jellyfin.Plugin.Youfin.Web." + resource) ?? throw new FileNotFoundException(resource);
        return new StreamReader(s).ReadToEnd();
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Read($"app.i18n.{code}.json"))!;

    private static string[] Codes() =>
        JsonSerializer.Deserialize<List<Dictionary<string, string>>>(Read("app.languages.json"))!.Select(l => l["code"]).ToArray();

    private static string Base(string key) => Plural.Replace(key, "");

    private static HashSet<string> UsedKeys() =>
        Regex.Matches(Read("app.app.js"), @"\btr\('([\w.]+)'").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(Read("app.index.html") + Read("index.html"), @"data-i18n(?:-[a-z-]+)?=""([\w.]+)""").Select(m => m.Groups[1].Value))
            .ToHashSet();

    [Fact]
    public void EnglishIsListedAndComplete()
    {
        Assert.Contains("en", Codes());
        var en = Load("en");
        var missing = UsedKeys().Where(k => !en.ContainsKey(k) && !en.ContainsKey(k + "_other")).ToList();
        Assert.True(missing.Count == 0, "Missing in en.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void EnglishHasNoDeadKeys()
    {
        var used = UsedKeys();
        var dead = Load("en").Keys.Where(k => !used.Contains(Base(k))).ToList();
        Assert.True(dead.Count == 0, "Unused keys in en.json: " + string.Join(", ", dead));
    }

    [Fact]
    public void EveryLanguageIsCompleteAndConsistent()
    {
        var en = Load("en");
        var enBase = en.Keys.Select(Base).ToHashSet();
        foreach (var code in Codes().Where(c => c != "en"))
        {
            var d = Load(code);
            Assert.True(enBase.SetEquals(d.Keys.Select(Base)), $"{code}: keys differ from English: {string.Join(", ", enBase.Except(d.Keys.Select(Base)).Concat(d.Keys.Select(Base).Except(enBase)))}");
            foreach (var g in d.GroupBy(kv => Base(kv.Key)))
            {
                var want = en.Where(kv => Base(kv.Key) == g.Key).SelectMany(kv => Placeholder.Matches(kv.Value).Select(m => m.Groups[1].Value)).ToHashSet();
                var got = g.SelectMany(kv => Placeholder.Matches(kv.Value).Select(m => m.Groups[1].Value)).ToHashSet();
                Assert.True(want.SetEquals(got), $"{code}/{g.Key}: placeholders differ ({string.Join(",", want)} vs {string.Join(",", got)})");
            }
        }
    }
}
