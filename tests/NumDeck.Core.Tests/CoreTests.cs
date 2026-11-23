using NumDeck.Core;
using NumDeck.Core.Actions;
using NumDeck.Core.Apps;
using NumDeck.Core.Browsers;
using NumDeck.Core.Config;
using NumDeck.Core.Macros;
using NumDeck.Core.Models;

namespace NumDeck.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void RoundTrip_PreservesEveryActionType()
    {
        var cfg = AppConfig.CreateDefault();
        var p = cfg.Profiles[0];
        p.GetOrAdd(new KeyId(0x47, false)).Action = new OpenBrowserAction
        {
            BrowserId = "Google Chrome", BrowserName = "Google Chrome", Urls = { "a.com", "b.com" }, NewWindow = false, ProfileName = "Profile 1",
        };
        p.GetOrAdd(new KeyId(0x48, false)).Action = new LaunchAppAction
        {
            Apps =
            {
                new AppEntry { Path = @"C:\x\app.exe", Arguments = "--flag" },
                new AppEntry { Name = "Calculadora", Path = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
            },
        };
        p.GetOrAdd(new KeyId(0x49, false)).Action = new PlaySoundAction { FilePath = @"C:\s.wav", Volume = 0.5 };
        p.GetOrAdd(new KeyId(0x4B, false)).Action = new MacroAction { Script = "Ctrl+C\ntexto: oi" };
        p.GetOrAdd(new KeyId(0x4C, false)).Action = new MediaAction { Command = MediaCommand.MicrophoneMute };
        p.GetOrAdd(new KeyId(0x1C, true)).Action = new SwitchProfileAction { Mode = ProfileSwitchMode.Previous };
        p.Find(new KeyId(0x47, false))!.Label = "Trabalho";
        cfg.Device = new DeviceSelection { DevicePath = @"\\?\HID#VID_1C4F&PID_0002", VendorId = "1C4F", ProductId = "0002" };

        var json = ConfigStore.Serialize(cfg);
        var back = ConfigStore.Deserialize(json)!;

        Assert.Equal(json, ConfigStore.Serialize(back));
        var bp = back.Profiles[0];
        var browser = Assert.IsType<OpenBrowserAction>(bp.Find(new KeyId(0x47, false))!.Action);
        Assert.Equal(new[] { "a.com", "b.com" }, browser.Urls);
        Assert.False(browser.NewWindow);
        Assert.Equal("Trabalho", bp.Find(new KeyId(0x47, false))!.Label);
        Assert.IsType<SwitchProfileAction>(bp.Find(new KeyId(0x1C, true))!.Action);
        Assert.Equal(MediaCommand.MicrophoneMute, Assert.IsType<MediaAction>(bp.Find(new KeyId(0x4C, false))!.Action).Command);
        var launch = Assert.IsType<LaunchAppAction>(bp.Find(new KeyId(0x48, false))!.Action);
        Assert.Equal(2, launch.Apps.Count);
        Assert.Equal("--flag", launch.Apps[0].Arguments);
        Assert.Equal("Calculadora", launch.Apps[1].Name);
        Assert.Equal("app + 1", launch.Summary);
        Assert.Contains("\"type\": \"openBrowser\"", json);
        Assert.DoesNotContain("\"displayName\": \"Calculadora\"", json);
        // Computed, editor-only properties must not be persisted.
        Assert.DoesNotContain("\"summary\"", json);
        Assert.DoesNotContain("\"validation\"", json);
        Assert.DoesNotContain("\"urlsText\"", json);
        Assert.DoesNotContain("\"key\"", json);
    }

    [Fact]
    public void LaunchApp_OldSingleAppConfig_BecomesOneEntry()
    {
        const string json = """
            { "type": "launchApp", "path": "C:\\x\\old.exe", "arguments": "-a", "workingDirectory": "", "bringToFrontIfRunning": false }
            """;
        var a = Assert.IsType<LaunchAppAction>(System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(json, ConfigStore.JsonOptions));
        var app = Assert.Single(a.Apps);
        Assert.Equal(@"C:\x\old.exe", app.Path);
        Assert.Equal("-a", app.Arguments);
        Assert.False(a.BringToFrontIfRunning);
        Assert.Equal("Abrir old", a.Summary);

        var again = System.Text.Json.JsonSerializer.Serialize<ActionDefinition>(a, ConfigStore.JsonOptions);
        using var doc = System.Text.Json.JsonDocument.Parse(again);
        Assert.True(doc.RootElement.TryGetProperty("apps", out _));
        Assert.False(doc.RootElement.TryGetProperty("path", out _));
        Assert.False(doc.RootElement.TryGetProperty("arguments", out _));
        Assert.False(doc.RootElement.TryGetProperty("workingDirectory", out _));
    }

    [Theory]
    [InlineData("Cursor", "Anysphere.Cursor", @"C:\Users\u\AppData\Local\Programs\cursor\Cursor.exe", true)]
    [InlineData("Calculadora", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "", true)]
    [InlineData("Uninstall", @"C:\laragon\bin\laragon\unins000.exe", @"C:\laragon\bin\laragon\unins000.exe", false)]
    [InlineData("Desinstalar o Telegram", @"C:\t\unins000.exe", @"C:\t\unins000.exe", false)]
    [InlineData("Ajuda do IRPF", @"C:\x\AjudaIRPF.pdf", @"C:\x\AjudaIRPF.pdf", false)]
    [InlineData("Node.js website", "https://nodejs.org/", "https://nodejs.org/", false)]
    [InlineData("Inno Setup FAQ", @"C:\x\isfaq.url", @"C:\x\isfaq.url", false)]
    public void InstalledApps_Classify(string name, string id, string target, bool kept)
    {
        var app = InstalledApps.Classify(name, id, target);
        Assert.Equal(kept, app is not null);
        if (app is not null) Assert.Equal(InstalledApps.AppsFolderPrefix + id, app.LaunchPath);
    }

    [Fact]
    public void Profile_SwapKeys_ExchangesActionsAndLabels()
    {
        var p = new Profile();
        KeyId k7 = new(0x47, false), k8 = new(0x48, false), enter = new(0x1C, true), k9 = new(0x49, false);
        var a7 = new MediaAction();
        var a8 = new MacroAction { Script = "Ctrl+C" };
        p.GetOrAdd(k7).Action = a7;
        p.GetOrAdd(k7).Label = "Sete";
        p.GetOrAdd(k8).Action = a8;

        Assert.True(p.SwapKeys(k7, k8));
        Assert.Same(a8, p.Find(k7)!.Action);
        Assert.Same(a7, p.Find(k8)!.Action);
        Assert.Equal("Sete", p.Find(k8)!.Label);
        Assert.Null(p.Find(k7)!.Label);

        // Onto an empty key: it moves.
        Assert.True(p.SwapKeys(k8, enter));
        Assert.Null(p.Find(k8));
        Assert.Same(a7, p.Find(enter)!.Action);
        Assert.Equal(2, p.Bindings.Count);

        Assert.False(p.SwapKeys(k8, k9)); // both empty
        Assert.False(p.SwapKeys(k7, k7));
    }

    [Fact]
    public void OpenFolder_RoundTripAndSummary()
    {
        ActionDefinition a = new OpenFolderAction { FolderPath = @"C:\Projetos\Site", ReuseOpenWindow = false };
        var json = System.Text.Json.JsonSerializer.Serialize(a, ConfigStore.JsonOptions);
        Assert.Contains("\"type\": \"openFolder\"", json);
        Assert.DoesNotContain("\"target\"", json);
        var back = Assert.IsType<OpenFolderAction>(System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(json, ConfigStore.JsonOptions));
        Assert.Equal(@"C:\Projetos\Site", back.FolderPath);
        Assert.False(back.ReuseOpenWindow);
        Assert.Equal("📁 Site", back.Summary);
        Assert.Equal("📁 Downloads", new OpenFolderAction { Location = FolderLocation.Downloads }.Summary);
        Assert.Equal(@"📁 D:\", new OpenFolderAction { FolderPath = @"D:\" }.Summary);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(@"C:\Projetos\Meu Site\", "\"C:\\Projetos\\Meu Site\"")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("shell:Downloads", "\"shell:Downloads\"")]
    public void OpenFolder_ExplorerArgument(string target, string expected) =>
        Assert.Equal(expected, NumDeck.Core.Execution.FolderOpener.ExplorerArgument(target));

    [Fact]
    public void OpenFolder_ExpandsEnvironmentVariables() =>
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\x",
            new OpenFolderAction { FolderPath = @"%USERPROFILE%\x" }.Target);

    [Fact]
    public void InstalledApps_Enumerate_ListsStartMenuApps()
    {
        var apps = InstalledApps.Enumerate();
        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.StartsWith(InstalledApps.AppsFolderPrefix, a.LaunchPath));
        Assert.DoesNotContain(apps, a => a.Name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(apps.Count, apps.Select(a => a.LaunchPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void InstalledApps_SharedWindowsProgram_HasNoExe()
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\cmd.exe");
        var app = InstalledApps.Classify("Node.js command prompt", "Microsoft.AutoGenerated.{X}", cmd)!;
        Assert.Equal("", app.ExePath);
        Assert.Equal(@"C:\p\Cursor.exe", InstalledApps.Classify("Cursor", "Anysphere.Cursor", @"C:\p\Cursor.exe")!.ExePath);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaultAndKeepsCopy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "numdeck-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{ not json");
            var cfg = new ConfigStore(path).Load();
            Assert.Single(cfg.Profiles);
            Assert.True(File.Exists(path + ".corrompido"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_ThenLoad_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "numdeck-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(Path.Combine(dir, "config.json"));
            var cfg = AppConfig.CreateDefault();
            cfg.Profiles[0].Name = "Stream";
            store.Save(cfg);
            Assert.Equal("Stream", store.Load().Profiles[0].Name);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class BrowserCommandTests
{
    [Fact]
    public void Chromium_NewWindowAndProfile()
    {
        var args = BrowserCommand.BuildArguments(BrowserFamily.Chromium, new[] { "github.com", "https://x.com/a?b=1" }, true, "Profile 1");
        Assert.Equal(new[] { "--profile-directory=Profile 1", "--new-window", "https://github.com", "https://x.com/a?b=1" }, args);
    }

    [Fact]
    public void Chromium_ExistingWindow_JustUrls()
    {
        var args = BrowserCommand.BuildArguments(BrowserFamily.Chromium, new[] { "a.com", " ", "b.com" }, false, null);
        Assert.Equal(new[] { "https://a.com", "https://b.com" }, args);
    }

    [Fact]
    public void Firefox_FirstUrlNewWindow_RestAsTabs()
    {
        var args = BrowserCommand.BuildArguments(BrowserFamily.Firefox, new[] { "a.com", "b.com" }, true, "trabalho");
        Assert.Equal(new[] { "-P", "trabalho", "-new-window", "https://a.com", "-new-tab", "https://b.com" }, args);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "--incognito")]
    [InlineData(@"C:\Users\x\AppData\Local\Perplexity\Comet\Application\comet.exe", "--incognito")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "--inprivate")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Opera\launcher.exe", "--private")]
    public void Incognito_UsesEachBrowsersSwitch(string exe, string expectedSwitch)
    {
        var action = new OpenBrowserAction { Urls = { "a.com" }, NewWindow = false, Incognito = true };
        Assert.Equal(new[] { expectedSwitch, "https://a.com" }, BrowserCommand.BuildArguments(exe, action));
    }

    [Fact]
    public void Incognito_Firefox_PrivateWindowPerUrl()
    {
        var action = new OpenBrowserAction { Urls = { "a.com", "b.com" }, NewWindow = true, Incognito = true };
        Assert.Equal(new[] { "-private-window", "https://a.com", "-private-window", "https://b.com" },
            BrowserCommand.BuildArguments(@"C:\Program Files\Mozilla Firefox\firefox.exe", action));
    }

    [Fact]
    public void Incognito_ShownInSummary_AndPersisted()
    {
        var action = new OpenBrowserAction { BrowserName = "Edge", Urls = { "a.com" }, Incognito = true };
        Assert.Equal("Edge (1 aba, anônima)", action.Summary);
        var cfg = AppConfig.CreateDefault();
        cfg.Profiles[0].GetOrAdd(new KeyId(0x47, false)).Action = action;
        var back = ConfigStore.Deserialize(ConfigStore.Serialize(cfg))!;
        Assert.True(Assert.IsType<OpenBrowserAction>(back.Profiles[0].Bindings[0].Action).Incognito);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", BrowserFamily.Firefox)]
    [InlineData(@"C:\Users\x\AppData\Local\Perplexity\Comet\Application\comet.exe", BrowserFamily.Chromium)]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", BrowserFamily.Chromium)]
    public void DetectFamily(string exe, BrowserFamily expected) => Assert.Equal(expected, BrowserCommand.DetectFamily(exe));

    [Theory]
    [InlineData("about:blank", "about:blank")]
    [InlineData("edge://settings", "edge://settings")]
    [InlineData("localhost:3000", "https://localhost:3000")]
    public void NormalizeUrl(string input, string expected) => Assert.Equal(expected, BrowserCommand.NormalizeUrl(input));

    [Theory]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"", @"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Apps\browser.exe --single-argument %1", @"C:\Apps\browser.exe")]
    public void ParseRegistryCommand(string command, string expected) => Assert.Equal(expected, BrowserCatalog.ParseExe(command));
}

public class MacroTests
{
    [Theory]
    [InlineData("Ctrl+Shift+M", new ushort[] { 0x11, 0x10 }, 0x4D)]
    [InlineData("alt+f4", new ushort[] { 0x12 }, 0x73)]
    [InlineData("Win+D", new ushort[] { 0x5B }, 0x44)]
    [InlineData("Ctrl++", new ushort[] { 0x11 }, 0xBB)]
    [InlineData("Ctrl+NumAdd", new ushort[] { 0x11 }, 0x6B)]
    [InlineData("F13", new ushort[] { }, 0x7C)]
    [InlineData("0x41", new ushort[] { }, 0x41)]
    public void ParsesCombos(string text, ushort[] mods, ushort key)
    {
        Assert.True(KeyCombo.TryParse(text, out var combo));
        Assert.Equal(mods, combo!.Modifiers);
        Assert.Equal(key, combo.Key);
    }

    [Theory]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+K")]
    [InlineData("Ctrl+Banana")]
    public void RejectsInvalidCombos(string text) => Assert.False(KeyCombo.TryParse(text, out _));

    [Theory]
    [InlineData(true, true, false, false, 0x4D, "Ctrl+Shift+M")]
    [InlineData(false, false, true, false, 0x73, "Alt+F4")]
    [InlineData(false, false, false, true, 0x25, "Win+Left")]
    [InlineData(true, false, false, false, 0x6B, "Ctrl+NumAdd")]
    public void FormatRoundTrips(bool ctrl, bool shift, bool alt, bool win, ushort vk, string expected)
    {
        var text = KeyCombo.Format(ctrl, shift, alt, win, vk);
        Assert.Equal(expected, text);
        Assert.True(KeyCombo.TryParse(text, out var combo));
        Assert.Equal(vk, combo!.Key);
    }

    [Fact]
    public void ParsesScript()
    {
        var steps = MacroParser.Parse("Ctrl+C\n\n# comentário\ntexto:  dois espaços\nespera: 250\r\nEnter", out var errors);
        Assert.Empty(errors);
        Assert.Collection(steps,
            s => Assert.IsType<ComboStep>(s),
            s => Assert.Equal(" dois espaços", Assert.IsType<TextStep>(s).Text),
            s => Assert.Equal(250, Assert.IsType<DelayStep>(s).Milliseconds),
            s => Assert.Equal(0x0D, Assert.IsType<ComboStep>(s).Combo.Key));
    }

    [Fact]
    public void ReportsErrorsWithLineNumbers()
    {
        MacroParser.Parse("Ctrl+C\nCtrl+Banana\nespera: muito", out var errors);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("Linha 2", errors[0]);
        Assert.StartsWith("Linha 3", errors[1]);
    }

    [Fact]
    public void MacroActionValidation_UpdatesWithScript()
    {
        var a = new MacroAction { Script = "Ctrl+C" };
        Assert.Equal("", a.Validation);
        a.Script = "xyz+1";
        Assert.Contains("Linha 1", a.Validation);
    }
}

public class DeckEngineTests
{
    private static (AppConfig cfg, DeckEngine engine, List<ActionDefinition> ran) Setup()
    {
        var cfg = AppConfig.CreateDefault();
        cfg.Profiles.Add(new Profile { Id = "p2", Name = "Stream" });
        cfg.Profiles.Add(new Profile { Id = "p3", Name = "Jogo" });
        var ran = new List<ActionDefinition>();
        var engine = new DeckEngine(cfg, a => { ran.Add(a); return Task.CompletedTask; });
        return (cfg, engine, ran);
    }

    private static readonly KeyId Seven = new(0x47, false), Enter = new(0x1C, true);

    [Fact]
    public void ProfileKeyWinsOverGlobal()
    {
        var (cfg, engine, ran) = Setup();
        var profileAction = new MediaAction();
        cfg.Profiles[0].GetOrAdd(Seven).Action = profileAction;
        cfg.GlobalKeys.GetOrAdd(Seven).Action = new MacroAction();
        engine.HandleKeyDown(Seven);
        Assert.Same(profileAction, Assert.Single(ran));
    }

    [Fact]
    public void GlobalKeyUsedWhenProfileHasNoAction()
    {
        var (cfg, engine, ran) = Setup();
        cfg.Profiles[0].GetOrAdd(Seven).Label = "só nome, sem ação";
        var global = new MacroAction();
        cfg.GlobalKeys.GetOrAdd(Seven).Action = global;
        engine.HandleKeyDown(Seven);
        Assert.Same(global, Assert.Single(ran));
    }

    [Fact]
    public void UnassignedKeyDoesNothing()
    {
        var (_, engine, ran) = Setup();
        Assert.Null(engine.HandleKeyDown(Seven));
        Assert.Empty(ran);
    }

    [Fact]
    public void SwitchProfile_NextWrapsAround_PreviousToo()
    {
        var (cfg, engine, ran) = Setup();
        cfg.GlobalKeys.GetOrAdd(Enter).Action = new SwitchProfileAction { Mode = ProfileSwitchMode.Next };
        var changes = new List<string>();
        engine.ProfileChanged += p => changes.Add(p.Name);

        engine.HandleKeyDown(Enter);
        engine.HandleKeyDown(Enter);
        engine.HandleKeyDown(Enter);
        Assert.Equal(new[] { "Stream", "Jogo", "Principal" }, changes);
        Assert.Empty(ran); // profile switching isn't sent to the runner

        engine.SwitchProfile(new SwitchProfileAction { Mode = ProfileSwitchMode.Previous });
        Assert.Equal("Jogo", engine.ActiveProfile.Name);
    }

    [Fact]
    public void SwitchProfile_Specific()
    {
        var (cfg, engine, _) = Setup();
        engine.SwitchProfile(new SwitchProfileAction { Mode = ProfileSwitchMode.Specific, ProfileId = "p3" });
        Assert.Equal("p3", cfg.ActiveProfileId);
        engine.SwitchProfile(new SwitchProfileAction { Mode = ProfileSwitchMode.Specific, ProfileId = "missing" });
        Assert.Equal("p3", cfg.ActiveProfileId);
    }

    [Fact]
    public void NumLockMatchesRegardlessOfExtendedFlag()
    {
        Assert.True(new KeyId(0x45, false).Matches(0x45, true));
        Assert.False(new KeyId(0x1C, true).Matches(0x1C, false)); // numpad Enter != main Enter
    }
}

public class SummaryTests
{
    [Fact]
    public void SummaryNotifiesWhenPropertiesChange()
    {
        var a = new OpenBrowserAction { BrowserName = "Comet" };
        var changed = new List<string?>();
        a.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        a.UrlsText = "a.com\r\nb.com\n\n";
        Assert.Equal("Comet (2 abas)", a.Summary);
        Assert.Contains(nameof(ActionDefinition.Summary), changed);
    }
}
