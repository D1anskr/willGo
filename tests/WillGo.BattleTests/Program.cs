using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.Infrastructure.Config;
using FloatingDeskAssistant.Infrastructure.Api;
using FloatingDeskAssistant.Infrastructure.Security;
using FloatingDeskAssistant.Models;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using NAudio.Wave;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;
using Image = System.Windows.Controls.Image;
using TabControl = System.Windows.Controls.TabControl;
using ListBox = System.Windows.Controls.ListBox;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--app-startup")) return TestFullAppStartup();
        if (args.Contains("--data-startup")) return TestFullAppStartup(dataRole: true);
        if (args.Contains("--local-login-startup")) return TestFullAppStartup(localAccess: true);
        if (args.Contains("--login-preview")) return LoginPresentationTests.Run();
        if (args.Contains("--activate-existing")) return new FloatingDeskAssistant.App().Run();
        if (args.Contains("--login-cancel")) return TestLoginExit(false);
        if (args.Contains("--login-reject")) return TestLoginExit(true);
        var exitCode = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                if (args.Contains("--fusion-only"))
                {
                    await FusionDisplayTests.RunAsync(Check);
                    Console.WriteLine($"PASS: {_checks} fusion display checks");
                    return;
                }
                if (args.Contains("--dual-machine-only"))
                {
                    await DualMachineTests.RunAsync(Check);
                    Console.WriteLine($"PASS: {_checks} dual-machine checks");
                    return;
                }
                if (args.Contains("--dual-machine-live"))
                {
                    await DualMachineTests.RunLiveAsync(Check);
                    Console.WriteLine($"PASS: {_checks} live dual-machine checks");
                    return;
                }
                if (!args.Contains("--speech-only") && !args.Contains("--integration-only"))
                {
                    await TestCoreAsync();
                    await SpeechEchoTests.RunAsync(Check);
                    await TestContextAsync();
                    await DoubaoCloudTests.RunAsync(Check);
                    await DualMachineTests.RunAsync(Check);
                    await FusionDisplayTests.RunAsync(Check);
                    await SpeechArchiveTests.RunAsync(Check);
                    await BattleHistoryTests.RunAsync(Check);
                    await LoginTests.RunAsync(Check);
                    await TestCaptureLifecycleAsync();
                    await TestRecordingChoiceAsync();
                    await TestScreenshotsAsync();
                    await ScreenshotWorkspaceTests.RunAsync(Check);
                    await TestWindowAsync();
                }
                if (args.Contains("--archive-live")) await SpeechArchiveTests.RunLiveAsync(args[Array.IndexOf(args, "--archive-live") + 1], Check);
                if (args.Contains("--cloud-capture")) await DoubaoCloudTests.RunCaptureAsync(Check);
                if (args.Contains("--cloud-connect")) await DoubaoCloudTests.RunConfiguredConnectionAsync(Check);
                if (args.Contains("--cloud-evaluate")) await CloudSpeechEvaluation.RunAsync(args[Array.IndexOf(args, "--cloud-evaluate") + 1], Check);
                if (args.Contains("--cloud-probe")) await CloudSpeechEvaluation.ProbeAsync(args[Array.IndexOf(args, "--cloud-probe") + 1], args.Contains("--cloud-probe-vad"));
                if (args.Contains("--recording-dialog"))
                {
                    await using var dialogVm = new BattleViewModel(new FakeSpeech(), new FakeRouter(), AppConfig.CreateDefault, Dispatcher.CurrentDispatcher);
                    await RecordingChoiceDialogTests.RunAsync(dialogVm, Check);
                }
                if (args.Contains("--model")) await TestConfiguredModelAsync();
                if (args.Contains("--screenshot-model")) await TestConfiguredScreenshotModelAsync();
                if (args.Contains("--screenshot-algorithm")) await ScreenshotWorkspaceTests.RunConfiguredAsync(Check);
                Console.WriteLine($"PASS: {_checks} battle-mode checks");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exitCode;
    }

    private static int TestFullAppStartup(bool localAccess = false, bool dataRole = false)
    {
        var result = 0;
        var app = new FloatingDeskAssistant.App();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var deadline = DateTime.UtcNow.AddSeconds(12);
        var loginHandled = false;
        timer.Tick += async (_, _) =>
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!loginHandled && app.Windows.OfType<LoginWindow>().FirstOrDefault() is { } login)
            {
                timer.Stop();
                try
                {
                    Check(app.MainWindow == login && typeof(FloatingDeskAssistant.App).GetField("_battleWindow", flags)!.GetValue(app) is null,
                        "login appears before the main workspace is created");
                    Check(!login.ShowInTaskbar && (GetWindowLongPtr(new WindowInteropHelper(login).Handle, -20).ToInt64() & 0x40080) == 0x80,
                        "login is a native tool window without a taskbar entry before validation");
                    Check(((SolidColorBrush)login.Background).Color == Colors.White && ((SolidColorBrush)((TextBox)login.FindName("UserIdTextBox")).Foreground).Color.R < 64,
                        "login uses white surfaces with dark readable account text");
                    Check(new[] { "_trayIcon", "_interactionHotkeys", "_opacityHotkeys", "_remoteCaptureServer" }.All(name => typeof(FloatingDeskAssistant.App).GetField(name, flags)!.GetValue(app) is null),
                        "no tray actions, feature hotkeys or phone service are enabled before validation");
                    typeof(FloatingDeskAssistant.App).GetMethod("ShowWorkspace", flags)!.Invoke(app, null);
                    Check(!app.Windows.OfType<BattleWindow>().Any(), "show-workspace callback cannot bypass login");
                    typeof(FloatingDeskAssistant.App).GetMethod("ShowBattleHistory", flags)!.Invoke(app, null);
                    Check(!app.Windows.OfType<BattleHistoryWindow>().Any(), "history callback cannot open archive access before login");
                    login.WindowState = WindowState.Minimized;
                    using (var duplicate = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--activate-existing")
                        { UseShellExecute = false, CreateNoWindow = true })!)
                    {
                        await Until(() => login.IsVisible && login.WindowState == WindowState.Normal && duplicate.HasExited);
                        Check(duplicate.ExitCode == 0 && app.Windows.OfType<LoginWindow>().Count() == 1,
                            "opening a second instance restores the existing login window without another login or an already-running dialog");
                    }
                    typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.SetValue(app, new LoginTests.MemoryConfig
                    {
                        BeforeSave = dataRole ? config => config.DualMachine = new() { Enabled = true, Role = DualMachineRole.Data, Port = 39657, FusionDisplayEnabled = true } : null
                    });
                    var loginVm = (LoginViewModel)login.DataContext;
                    var validator = new LoginTests.FakeValidation { Result = new() { StatusCode = "USER_DISABLED" } };
                    typeof(LoginViewModel).GetField("_validationService", flags)!.SetValue(loginVm, validator);
                    typeof(LoginViewModel).GetField("_ipResolver", flags)!.SetValue(loginVm, new LoginTests.FixedIp());
                    loginVm.UserId = "test-user-0001";
                    loginVm.RememberUserId = true;
                    SaveWindowImage(login, "login.png");
                    loginVm.LoginCommand.Execute(null);
                    await Until(() => loginVm.LoginCommand.CanExecute(null));
                    Check(login.IsVisible && loginVm.SuccessfulResult is null && !app.Windows.OfType<BattleWindow>().Any(), "denied account remains at login without workspace access");
                    validator.Result = localAccess ? new() { IsServerUnavailable = true } : new() { Success = true, IsAllowed = true, StatusCode = "VALID" };
                    if (localAccess) loginVm.UserId = "d1anskr";
                    loginHandled = true;
                    deadline = DateTime.UtcNow.AddSeconds(12);
                    loginVm.LoginCommand.Execute(null);
                    if (localAccess) Check(validator.Calls == 1 && loginVm.SuccessfulResult?.StatusCode == "LOCAL_ACCESS",
                        "local code completes the real login dialog without another validation request");
                    timer.Start();
                }
                catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); app.Shutdown(); }
                return;
            }
            var battle = typeof(FloatingDeskAssistant.App).GetField("_battleWindow", flags)!.GetValue(app) as BattleWindow;
            if (dataRole && battle is not null && typeof(FloatingDeskAssistant.App).GetField("_dualMachine", flags)!.GetValue(app) is DualMachineService { IsData: true, PairingOffers.Count: > 0 } dataPeer)
            {
                timer.Stop();
                try
                {
                    Check(!battle.IsVisible && !app.Windows.OfType<SettingsWindow>().Any(), "data startup is quiet without a workspace or settings window");
                    Check(!app.Windows.OfType<BallWindow>().Any() && !app.Windows.OfType<ChatWindow>().Any(), "data startup creates no legacy windows");
                    Check(typeof(FloatingDeskAssistant.App).GetField("_trayIcon", flags)!.GetValue(app) is TrayIconService { Visible: true }, "data startup retains a registered tray icon");
                    Check(!dataPeer.IsAudioStreaming && !((BattleViewModel)battle.DataContext).IsRecording, "data startup and listening do not activate microphones");
                    var displayConfig = AppConfig.CreateDefault();
                    displayConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Display, PeerOffer = (DualMachineOffer.Parse(dataPeer.PairingOffers.First()) with { Host = "127.0.0.1" }).ToString() };
                    await using var displayPeer = new DualMachineService(() => displayConfig, new FakeScreenshots());
                    await displayPeer.ApplyAsync(displayConfig.DualMachine);
                    Check(dataPeer.IsConnected && !battle.IsVisible, "production data service accepts a paired display while its window stays hidden");
                    var main = (MainViewModel)typeof(FloatingDeskAssistant.App).GetField("_mainViewModel", flags)!.GetValue(app)!;
                    var remoteActions = new ConcurrentQueue<DisplayControlAction>();
                    displayPeer.DisplayControlReceived += (action, _) => remoteActions.Enqueue(action);
                    var keys = (GlobalInteractionHotkeys)typeof(FloatingDeskAssistant.App).GetField("_interactionHotkeys", flags)!.GetValue(app)!;
                    var opacity = (GlobalOpacityHotkeys)typeof(FloatingDeskAssistant.App).GetField("_opacityHotkeys", flags)!.GetValue(app)!;
                    var originalOpacity = main.WindowOpacity;
                    ((Action)typeof(GlobalInteractionHotkeys).GetField("_toggleMousePassthrough", flags)!.GetValue(keys)!)();
                    foreach (var direction in new[] { -1, 1 })
                        ((Action<int>)typeof(GlobalInteractionHotkeys).GetField("_selectQuestion", flags)!.GetValue(keys)!)(direction);
                    foreach (var direction in new[] { -1, 1 })
                        ((Action<int>)typeof(GlobalInteractionHotkeys).GetField("_scrollConversation", flags)!.GetValue(keys)!)(direction);
                    foreach (var delta in new[] { -.05, .05 })
                        ((Action<double>)typeof(GlobalOpacityHotkeys).GetField("_adjustOpacity", flags)!.GetValue(opacity)!)(delta);
                    await Until(() => remoteActions.Count == 7);
                    Check(remoteActions.SequenceEqual(Enum.GetValues<DisplayControlAction>()), "all production data hotkey callbacks forward the intended display actions in order");
                    Check(!WindowInteraction.IsClickThrough && main.WindowOpacity == originalOpacity && !battle.IsVisible,
                        "forwarded hotkeys leave data opacity, mouse interaction and hidden workspace unchanged");
                    var single = main.GetConfigSnapshot(); single.DualMachine.Enabled = false;
                    typeof(MainViewModel).GetField("_config", flags)!.SetValue(main, single);
                    await (Task)typeof(FloatingDeskAssistant.App).GetMethod("ApplyDualMachineModeAsync", flags)!.Invoke(app, new object[] { single })!;
                    Check(battle.IsVisible && !dataPeer.IsConnected && !dataPeer.IsData, "leaving data role restores existing workspace without restarting app");
                    single.DualMachine.Enabled = true;
                    typeof(MainViewModel).GetField("_config", flags)!.SetValue(main, single);
                    await (Task)typeof(FloatingDeskAssistant.App).GetMethod("ApplyDualMachineModeAsync", flags)!.Invoke(app, new object[] { single })!;
                    Check(dataPeer.IsData && !battle.IsVisible && !app.Windows.OfType<SettingsWindow>().Any(), "switching into data role hides workspace and does not open another window");
                    Console.WriteLine($"PASS: {_checks} data startup checks");
                }
                catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); }
                finally { await (Task)typeof(FloatingDeskAssistant.App).GetMethod("ExitAppAsync", flags)!.Invoke(app, null)!; }
                return;
            }
            if (battle?.IsVisible != true)
            {
                if (DateTime.UtcNow < deadline) return;
                timer.Stop(); result = 1; Console.Error.WriteLine("FAIL: full app did not finish startup"); app.Shutdown(); return;
            }
            timer.Stop();
            try
            {
                Check(typeof(FloatingDeskAssistant.App).GetField("_trayIcon", flags)!.GetValue(app) is TrayIconService { Visible: true },
                    "complete application startup creates its real tray icon");
                var vm = (BattleViewModel)typeof(FloatingDeskAssistant.App).GetField("_battleViewModel", flags)!.GetValue(app)!;
                Check(vm.ConfirmAudioRecording is not null, "real application attaches the per-session audio recording question");
                vm.ConfirmAudioRecording = () => false;
                Check(((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved is { RememberUserId: true } savedLogin &&
                    savedLogin.RememberedUserId == (localAccess ? "d1anskr" : "test-user-0001"), "successful login saves remembered ID without persisting authorization");
                var speech = new FakeSpeech();
                await ((IBattleSpeechService)typeof(BattleViewModel).GetField("_speech", flags)!.GetValue(vm)!).DisposeAsync();
                typeof(BattleViewModel).GetField("_speech", flags)!.SetValue(vm, speech);
                await Task.Delay(350);
                Check(app.MainWindow == battle && battle.Owner is null && !battle.ShowInTaskbar &&
                    (GetWindowLongPtr(new WindowInteropHelper(battle).Handle, -20).ToInt64() & 0x40080) == 0x80,
                    "battle is the tray-only native tool window without a taskbar entry");
                Check(!app.Windows.OfType<BallWindow>().Any() && !app.Windows.OfType<ChatWindow>().Any(), "startup creates no floating ball or normal chat window");
                Check(!vm.IsRecording && speech.Starts == 0 && vm.RecordingButtonText == "开始战斗", "startup leaves speech recognition off until requested");
                var recordingButton = (System.Windows.Controls.Button)battle.FindName("RecordingButton");
                recordingButton.Command.Execute(null);
                await Until(() => vm.IsRecording);
                Check(speech.Starts == 1 && vm.RecordingButtonText == "结束战斗", "header button starts speech recognition explicitly");
                battle.Hide();
                typeof(FloatingDeskAssistant.App).GetMethod("ActivateExistingWindow", flags)!.Invoke(app, null);
                Check(battle.IsVisible && vm.IsRecording && speech.Starts == 1, "restoring main window preserves active recognition");
                await Until(() => recordingButton.Command.CanExecute(null));
                recordingButton.Command.Execute(null);
                await Until(() => !vm.IsRecording);
                Check(speech.Stops > 0 && vm.RecordingButtonText == "开始战斗", "same header button turns recognition off");
                var main = (MainViewModel)typeof(FloatingDeskAssistant.App).GetField("_mainViewModel", flags)!.GetValue(app)!;
                await main.SetAnswerFontSizeAsync(22);
                Check(main.GetConfigSnapshot().Battle.AnswerFontSize == 22 && ((FloatingDeskAssistant.UI.Controls.AnswerContentPresenter)battle.FindName("AnswerContent")).ContentFontSize == 22 &&
                    ((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved!.Battle.AnswerFontSize == 22,
                    "answer font changes reach the real workspace and persistent configuration");
                ((ColumnDefinition)battle.FindName("QuestionColumn")).Width = new GridLength(245);
                battle.UpdateLayout();
                typeof(BattleWindow).GetMethod("SaveQuestionPaneWidth", flags)!.Invoke(battle, null);
                await Until(() => ((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved!.Battle.QuestionPaneWidth == 245);
                Check(main.GetConfigSnapshot().Battle.QuestionPaneWidth == 245,
                    "divider completion reaches the real application's saved configuration");
                var snapshot = main.GetConfigSnapshot(); snapshot.RemoteCaptureMode = RemoteCaptureMode.PresetRegion;
                snapshot.RemoteCaptureRegionX = 34; snapshot.RemoteCaptureRegionY = 45;
                snapshot.RemoteCaptureRegionWidth = 320; snapshot.RemoteCaptureRegionHeight = 180;
                snapshot.Battle.SpeechArchiveDirectory = Path.Combine(AppContext.BaseDirectory, "startup-history-empty");
                snapshot.PromptPresets.Add(new PromptPresetConfig { Id = "workspace-test", Title = "Test preset", Prompt = "Preset question" });
                typeof(MainViewModel).GetField("_config", flags)!.SetValue(main, snapshot);
                var screenshots = new FakeScreenshots { Result = new ScreenshotCaptureResult { ImageBytes = CreateScreenshotFixture() } };
                typeof(BattleViewModel).GetField("_screenshots", flags)!.SetValue(vm, screenshots);
                typeof(BattleViewModel).GetField("_router", flags)!.SetValue(vm, new FakeRouter());
                var messages = main.Messages.Count;
                await main.CaptureAndSendAsync();
                await Until(() => vm.Entries.Count == 1 && vm.Entries.All(e => !e.Busy));
                Check(vm.Selected!.IsScreenshot && main.Messages.Count == messages && screenshots.LastKind == "region" && screenshots.SelectionCalls == 0,
                    "screenshot command captures the saved region into the question list while recognition is off");
                var keys = (GlobalInteractionHotkeys)typeof(FloatingDeskAssistant.App).GetField("_interactionHotkeys", flags)!.GetValue(app)!;
                ((Action)typeof(GlobalInteractionHotkeys).GetField("_screenshot", flags)!.GetValue(keys)!)();
                await Until(() => vm.Entries.Count == 2 && vm.Entries.All(e => !e.Busy));
                Check(vm.Selected!.IsScreenshot && screenshots.LastKind == "region" && screenshots.Region == new System.Drawing.Rectangle(34, 45, 320, 180) && screenshots.SelectionCalls == 0,
                    "application global screenshot callback directly captures the exact saved region without selection");
                var server = (RemoteCaptureServer)typeof(FloatingDeskAssistant.App).GetField("_remoteCaptureServer", flags)!.GetValue(app)!;
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var reply = await http.PostAsync($"http://127.0.0.1:{server.Port}/capture", new System.Net.Http.StringContent(""));
                await Until(() => vm.Entries.Count == 3 && vm.Entries.All(e => !e.Busy));
                Check(reply.IsSuccessStatusCode && vm.Selected!.IsScreenshot && main.Messages.Count == messages,
                    "complete application phone endpoint routes captures into battle questions");
                ((Action<int>)typeof(GlobalInteractionHotkeys).GetField("_selectQuestion", flags)!.GetValue(keys)!)(1);
                Check(vm.Selected == vm.Entries[1], "global down selects the next older question in newest-first order");
                ((Action<int>)typeof(GlobalInteractionHotkeys).GetField("_selectQuestion", flags)!.GetValue(keys)!)(-1);
                Check(vm.Selected == vm.Entries[0], "global up returns to the newest question at the top");
                using var textReply = await http.PostAsync($"http://127.0.0.1:{server.Port}/prompt/send", new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string> { ["prompt"] = "Phone question" }));
                await Until(() => vm.Entries.Count == 4 && vm.Entries.All(e => !e.Busy));
                Check(textReply.IsSuccessStatusCode && vm.Selected!.Text == "Phone question" && main.Messages.Count == messages, "phone text question is answered in the sole main window");
                Check(main.QueueRemotePromptPreset("workspace-test").Accepted, "phone preset accepts the existing configured question format");
                await Until(() => vm.Entries.Count == 5 && vm.Entries.All(e => !e.Busy));
                Check(vm.Selected!.Text == "Preset question" && main.Messages.Count == messages, "phone preset answer stays in the main question list");
                using var page = await http.GetAsync($"http://127.0.0.1:{server.Port}/");
                var html = await page.Content.ReadAsStringAsync();
                Check(!html.Contains("Remote Ball Control") && !html.Contains("action=\"/compact/toggle\""), "phone page has no obsolete ball or normal-chat mode control");
                var historyButton = (Button)battle.FindName("HistoryButton");
                historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => app.Windows.OfType<BattleHistoryWindow>().Any());
                var history = app.Windows.OfType<BattleHistoryWindow>().Single();
                await history.RefreshAsync();
                Check(history.Owner == battle && !history.ShowInTaskbar && ((BattleHistoryViewModel)history.DataContext).Entries.Count == 0,
                    "authenticated history button opens the configured archive in an owned tray-only window");
                historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(app.Windows.OfType<BattleHistoryWindow>().Count() == 1, "reopening history reuses the same dashboard window");
                history.Close();
                Check(typeof(FloatingDeskAssistant.App).GetField("_historyWindow", flags)!.GetValue(app) is null, "closing history clears the application window reference");
                battle.Close();
                Check(!battle.IsVisible && !vm.IsRecording, "closing the main window hides it to the tray without enabling recognition");
                typeof(FloatingDeskAssistant.App).GetMethod("ShowWorkspace", flags)!.Invoke(app, null);
                Check(battle.IsVisible && !vm.IsRecording && speech.Starts == 1 &&
                    (GetWindowLongPtr(new WindowInteropHelper(battle).Handle, -20).ToInt64() & 0x40080) == 0x80,
                    "tray restore retains tool-window identity without turning recognition back on");
                await TestRemoteDisplayControlsAsync(app, main, battle, vm);
                Console.WriteLine($"PASS: {_checks} full application startup checks");
            }
            catch (Exception ex) { result = 1; Console.Error.WriteLine(ex); }
            finally { await (Task)typeof(FloatingDeskAssistant.App).GetMethod("ExitAppAsync", flags)!.Invoke(app, null)!; }
        };
        timer.Start(); app.Run(); return result;
    }

    private static async Task TestRemoteDisplayControlsAsync(FloatingDeskAssistant.App app, MainViewModel main, BattleWindow battle, BattleViewModel vm)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var dataConfig = AppConfig.CreateDefault();
        dataConfig.DualMachine = new() { Enabled = true, Role = DualMachineRole.Data, Port = DualMachineFreePort() };
        await using var data = new DualMachineService(() => dataConfig, new FakeScreenshots(), DualMachineIdentity.Create);
        await data.ApplyAsync(dataConfig.DualMachine);
        var snapshot = main.GetConfigSnapshot();
        snapshot.DualMachine = new() { Enabled = true, Role = DualMachineRole.Display,
            PeerOffer = (DualMachineOffer.Parse(data.PairingOffers.First()) with { Host = "127.0.0.1" }).ToString() };
        typeof(MainViewModel).GetField("_config", flags)!.SetValue(main, snapshot);
        await (Task)typeof(FloatingDeskAssistant.App).GetMethod("ApplyDualMachineModeAsync", flags)!.Invoke(app, new object[] { snapshot })!;
        await Until(() => data.IsConnected);
        void Send(DisplayControlAction action) { if (!data.TrySendDisplayControl(action, out var error)) throw new InvalidOperationException(error); }
        vm.Selected = vm.Entries[0];
        Send(DisplayControlAction.NextQuestion);
        await Until(() => vm.Selected == vm.Entries[1]);
        Send(DisplayControlAction.PreviousQuestion);
        await Until(() => vm.Selected == vm.Entries[0]);
        Check(true, "production display receiver switches questions in both directions");
        vm.Selected!.Answer = string.Join("\n\n", Enumerable.Range(1, 100).Select(i => $"Long answer paragraph {i}"));
        await Task.Delay(100); battle.UpdateLayout();
        var scroll = (ScrollViewer)battle.FindName("AnswerScroll");
        scroll.ScrollToTop(); await Task.Delay(50);
        Send(DisplayControlAction.ScrollDown);
        await Until(() => scroll.VerticalOffset > 0);
        var offset = scroll.VerticalOffset;
        Send(DisplayControlAction.ScrollUp);
        await Until(() => scroll.VerticalOffset < offset);
        Check(true, "production display receiver scrolls the actual answer viewport in both directions");
        var opacity = main.WindowOpacity;
        Send(DisplayControlAction.DecreaseOpacity);
        await Until(() => Math.Abs(main.WindowOpacity - Math.Max(.3, opacity - .05)) < .001);
        await Until(() => ((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved!.WindowOpacity == main.WindowOpacity);
        Send(DisplayControlAction.IncreaseOpacity);
        await Until(() => Math.Abs(main.WindowOpacity - Math.Min(1, Math.Max(.3, opacity - .05) + .05)) < .001);
        Check(true, "remote opacity uses the original five-percent steps and persists display settings");
        Send(DisplayControlAction.ToggleClickThrough);
        await Until(() => WindowInteraction.IsClickThrough);
        Send(DisplayControlAction.ToggleClickThrough);
        await Until(() => !WindowInteraction.IsClickThrough);
        Check(battle.IsVisible, "remote mouse passthrough toggles twice without hiding the display window");
        var settingsCallback = main.OpenSettingsDialogAsync;
        main.OpenSettingsDialogAsync = config =>
        {
            config.DualMachine.FusionDisplayEnabled = true;
            return Task.FromResult<AppConfig?>(config);
        };
        main.OpenSettingsCommand.Execute(null);
        await Until(() => battle.Owner?.Title == "willGo 融合器幕布");
        main.OpenSettingsDialogAsync = settingsCallback;
        Check(data.IsConnected && !vm.IsRecording && ((FloatingDeskAssistant.UI.Controls.AnswerContentPresenter)battle.FindName("AnswerContent")).ContentFontSize == 22,
            "saving fusion preferences creates the backdrop without reconnecting transport or starting capture");
        var normalOpacity = main.WindowOpacity;
        Send(DisplayControlAction.DecreaseOpacity);
        await Until(() => main.GetConfigSnapshot().DualMachine.FusionOpacity == .95);
        await Until(() => ((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved!.DualMachine.FusionOpacity == .95);
        Check(main.WindowOpacity == normalOpacity && battle.Owner is { IsVisible: true },
            "forwarded opacity updates and persists only fusion opacity while backdrop stays opaque");
        ((Button)battle.FindName("IncreaseAnswerFontButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => main.GetConfigSnapshot().DualMachine.FusionAnswerFontSize == 23);
        Check(main.GetConfigSnapshot().Battle.AnswerFontSize == 22,
            "production font toolbar preserves the normal answer size in fusion mode");
        ((Button)battle.FindName("FusionExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => battle.Owner is null && !main.GetConfigSnapshot().DualMachine.FusionDisplayEnabled);
        Check(data.IsConnected && battle.IsVisible && ((LoginTests.MemoryConfig)typeof(FloatingDeskAssistant.App).GetField("_configService", flags)!.GetValue(app)!).Saved!.DualMachine.FusionDisplayEnabled == false,
            "production fusion exit button restores desktop and persists its preference without disconnecting");
    }

    private static int DualMachineFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static int TestLoginExit(bool forgedResult)
    {
        var result = 0;
        var observed = false;
        var app = new FloatingDeskAssistant.App();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        var deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Tick += (_, _) =>
        {
            if (app.Windows.OfType<LoginWindow>().FirstOrDefault() is { } login)
            {
                timer.Stop(); observed = true;
                login.DialogResult = forgedResult;
            }
            else if (DateTime.UtcNow > deadline) { timer.Stop(); result = 1; app.Shutdown(); }
        };
        timer.Start(); app.Run();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Check(observed && result == 0 && new[] { "_battleWindow", "_trayIcon", "_interactionHotkeys", "_remoteCaptureServer" }.All(name => typeof(FloatingDeskAssistant.App).GetField(name, flags)!.GetValue(app) is null),
            forgedResult ? "dialog success without validated server result cannot initialize workspace" : "closing login exits without enabling workspace or capture services");
        return result;
    }

    private static async Task TestCoreAsync()
    {
        Check(BattlePrompt.MatchesTrigger("嗯，让 我 想 一 想，这个问题", "我想一想\n稍等一下"), "Chinese trigger ignores ASR spacing and punctuation");
        Check(!BattlePrompt.MatchesTrigger("我正在回答", "\n ; ；"), "empty triggers never match");
        Check(BattlePrompt.MatchesTrigger("LET ME THINK about it", "let me think"), "Latin trigger is case insensitive");
        Check(!BattlePrompt.MatchesTrigger("让我回答", "我想一想"), "non-trigger microphone speech is not submitted");
        var config = AppConfig.CreateDefault();
        config.Battle.ConversationPrompt = "Conversation-only prompt";
        var entry = new BattleEntry { Source = SpeechSource.Speaker, Text = "Question" };
        var built = BattlePrompt.Build(config, entry);
        Check(built.Config.Primary.SystemPrompt == config.Battle.ConversationPrompt + "\n\n" + config.Battle.ContextPrompt + BattlePrompt.AnswerFormatting && built.Config.Secondary.SystemPrompt == built.Config.Primary.SystemPrompt,
            "battle prompt applies to primary and fallback endpoints");
        Check(built.Turns.Length == 1 && built.Turns[0].Text == "对方说：\nQuestion", "only finalized sentence is sent as user content");
        Check(config.Primary.SystemPrompt != config.Battle.ConversationPrompt, "normal screenshot prompt remains intact");
        var clone = config.Clone(); clone.Battle.TriggerPhrases = "different";
        Check(clone.Battle.TriggerPhrases != config.Battle.TriggerPhrases, "battle settings clone independently");
        var service = new AppConfigService(new ApiKeyProtector(), new TestLogger());
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "battle-test-settings.json");
        typeof(AppConfigService).GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, settingsPath);
        config.Battle.Speech.ApiKey = "test-speech-secret";
        config.Battle.Speech.AccessToken = "test-legacy-secret";
        config.Battle.Speech.AppId = "test-app";
        config.Battle.SpeechPauseSeconds = 1.3;
        config.Battle.SpeechArchiveDirectory = Path.Combine(Path.GetTempPath(), "willgo-config-test");
        config.Battle.SaveSpeechSessions = false;
        config.Battle.AnswerFontSize = 23;
        config.Battle.QuestionPaneWidth = 267;
        config.Battle.ScreenshotQuestionRatio = .46;
        await service.SaveAsync(config);
        var loaded = await service.LoadAsync();
        Check(loaded.Battle.ConversationPrompt == config.Battle.ConversationPrompt && loaded.Battle.TriggerPhrases == config.Battle.TriggerPhrases, "battle configuration persists");
        Check(loaded.Battle.AnswerFontSize == 23, "answer font size persists independently of legacy message size");
        Check(loaded.Battle.QuestionPaneWidth == 267, "question pane width round-trips through the real config service");
        Check(loaded.Battle.ScreenshotQuestionRatio == .46, "screenshot question ratio round-trips independently of the sidebar width");
        Check(loaded.Battle.SpeechPauseSeconds == 1.3, "cloud pause duration persists independently of credentials");
        Check(loaded.Battle.Speech.ApiKey == "test-speech-secret" && loaded.Battle.Speech.AccessToken == "test-legacy-secret" && loaded.Battle.Speech.AppId == "test-app", "cloud credentials round-trip through Windows DPAPI");
        var savedText = await File.ReadAllTextAsync(settingsPath);
        Check(!savedText.Contains("test-speech-secret") && !savedText.Contains("test-legacy-secret"), "neither cloud credential is stored in plaintext");
        Check(!savedText.Contains("secondPass") && !savedText.Contains("speechHotwords") && !savedText.Contains("speechRefinerContext"), "saved cloud settings contain no ASR second-pass or vocabulary options");
        Check(!loaded.Battle.SaveSpeechSessions && loaded.Battle.SpeechArchiveDirectory == config.Battle.SpeechArchiveDirectory, "recording preference and custom archive directory persist");
        var legacy = JsonSerializer.Serialize(new PersistedAppConfig());
        await File.WriteAllTextAsync(settingsPath, legacy);
        Check((await service.LoadAsync()).Battle.TriggerPhrases.Contains("我想一想"), "legacy configuration gets default trigger phrases");
        await File.WriteAllTextAsync(settingsPath, "{\"battle\":{\"triggerPhrases\":\"old-trigger\",\"speechHotwords\":\"old-list\",\"speechRefinerContext\":\"old-context\"}}");
        var migratedSpeech = (await service.LoadAsync()).Battle;
        Check(migratedSpeech.Speech.ApiKey == "" && migratedSpeech.SpeechPauseSeconds == 1.1 && migratedSpeech.TriggerPhrases == "old-trigger",
            "legacy local settings migrate to cloud defaults with no forced vocabulary and preserve triggers");
        Check(migratedSpeech.SaveSpeechSessions && Path.IsPathFullyQualified(migratedSpeech.SpeechArchiveDirectory), "legacy settings gain enabled session recording and an absolute default save directory");
        Check(migratedSpeech.AnswerFontSize == 18, "legacy settings gain readable 18px answers without retaining the old 14px ceiling");
        Check(migratedSpeech.ContextPrompt == BattleConfig.DefaultContextPrompt && migratedSpeech.SuppressSpeakerEcho,
            "existing installations gain default interview context and enabled echo filtering without a settings migration");

        var floats = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(0), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(4), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(8), -1f);
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(12), 1f);
        var mono = AudioPcm16Converter.Convert(floats, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Check(BinaryPrimitives.ReadInt16LittleEndian(mono) == 16384 && BinaryPrimitives.ReadInt16LittleEndian(mono.AsSpan(2)) == 0,
            "stereo float audio downmixes to mono PCM16 without changing sample rate");
        byte[] pcm24 = [0, 0, 128, 255, 255, 127];
        var converted24 = AudioPcm16Converter.Convert(pcm24, new WaveFormat(48000, 24, 1));
        Check(BinaryPrimitives.ReadInt16LittleEndian(converted24) == -32768 && BinaryPrimitives.ReadInt16LittleEndian(converted24.AsSpan(2)) == 32767,
            "24-bit PCM conversion handles both extrema");

        var speech = new FakeSpeech();
        var router = new FakeRouter();
        await using var vm = new BattleViewModel(speech, router, () => config, Dispatcher.CurrentDispatcher);
        Check(!vm.IsRecording && speech.Starts == 0 && vm.RecordingButtonText == "开始战斗", "initial state leaves speech recognition off and offers explicit start");
        await vm.ToggleRecordingAsync();
        speech.Emit(new(SpeechSource.Speaker, "尚未说完", false));
        speech.Emit(new(SpeechSource.Microphone, "我正在回答", true));
        await Task.Delay(60);
        Check(vm.Entries.Count == 0 && vm.Transcript.Contains("我正在回答"), "partials and ordinary microphone speech never generate questions");
        speech.Emit(new(SpeechSource.Microphone, "下一句话正在说", false, Sequence: 2));
        speech.Emit(new(SpeechSource.Microphone, "上一句话的最终文字", true, Sequence: 1));
        await Task.Delay(30);
        Check(vm.MicrophonePartial == "下一句话正在说", "delayed refinement does not clear the next sentence's live transcript");
        speech.Emit(new(SpeechSource.Microphone, "我 想 一 想，这是什么", true));
        speech.Emit(new(SpeechSource.Speaker, "什么是哈希表", true));
        await Until(() => vm.Entries.Count == 2 && vm.Entries.All(e => !e.Busy));
        Check(router.Calls == 2 && vm.Entries.All(e => e.Answer == "Test answer"), "microphone trigger and other-person sentence generate separate answers");
        Check(vm.Entries[0].Text == "什么是哈希表" && vm.Entries[1].Text == "我 想 一 想，这是什么" && vm.Selected == vm.Entries[0],
            "new recognized question is selected at the top and older speech questions move down");
        await vm.StopCaptureAsync();
        speech.Emit(new(SpeechSource.Speaker, "停止后的句子", true));
        await Task.Delay(30);
        Check(vm.Entries.Count == 2 && !vm.IsRecording && speech.Stops > 0, "paused capture ignores subsequent callbacks");
        router.Fail = true;
        vm.AddSentence(SpeechSource.Speaker, "failed request", true);
        await Until(() => vm.Selected?.Busy == false);
        Check(vm.Selected!.Status.Contains("请求失败"), "failed model request is visible in its question entry");
        router.Fail = false;
        vm.RetryCommand.Execute(null);
        await Until(() => vm.Selected?.Busy == false);
        Check(vm.Selected.Answer == "Test answer", "failed request can be retried");
        router.Delay = 2000;
        for (var i = 0; i < 24; i++) vm.AddSentence(SpeechSource.Speaker, "queued " + i, true);
        Check(vm.Entries.Any(e => e.Status.Contains("队列已满")), "overflow stays visible for manual retry");
        await Task.Delay(40);
        Check(router.Peak <= 2, "at most two model requests run concurrently");
        vm.CancelRequests();
        await Until(() => vm.Entries.All(e => !e.Busy));
        Check(vm.Entries.Any(e => e.Status.Contains("已取消")), "cancellation includes queued and active requests");
    }

    private static async Task TestContextAsync()
    {
        var config = AppConfig.CreateDefault();
        config.Battle.ContextPrompt = "测试面试语境：结合最近两轮解释可能的识别错误。";
        var router = new FakeRouter { Delay = 80 };
        await using var vm = new BattleViewModel(new FakeSpeech(), router, () => config, Dispatcher.CurrentDispatcher);
        vm.AddSentence(SpeechSource.Speaker, "MySQL 索引的数据结构是什么", true);
        vm.AddSentence(SpeechSource.Speaker, "为什么要用毕加索", true);
        vm.AddSentence(SpeechSource.Speaker, "B+ 树和红黑树的区别", true);
        vm.AddSentence(SpeechSource.Speaker, "那么范围查询呢", true);
        await Until(() => vm.Entries.All(entry => !entry.Busy));
        Check(router.Requests.Select(request => request.Turns.Count).SequenceEqual(new[] { 1, 3, 5, 5 }),
            "dialogue requests carry at most two preceding question-answer pairs");
        var third = router.Requests[2];
        Check(third.Turns[0].Text!.Contains("MySQL") && third.Turns[1].Role == ChatRole.Assistant && third.Turns[1].Text == "Test answer" &&
            third.Turns[2].Text!.Contains("毕加索") && third.Turns[3].Text == "Test answer" && third.Turns[^1].Text!.Contains("B+ 树"),
            "rapidly queued questions wait for previous answers and send history in chronological order");
        Check(router.Requests.All(request => request.Config.Primary.SystemPrompt.Contains(config.Battle.ContextPrompt) &&
            request.Config.Secondary.SystemPrompt.Contains(config.Battle.ContextPrompt)), "custom context instructions reach primary and fallback dialogue models");
        Check(vm.Entries.Any(entry => entry.Text == "为什么要用毕加索") && vm.Transcript.Contains("为什么要用毕加索"),
            "contextual interpretation never silently rewrites the original transcription or question title");
        Check(!router.Requests[3].Turns.Any(turn => turn.Text!.Contains("MySQL")), "only the nearest two preceding rounds are sent");
        vm.Selected = vm.Entries[2]; vm.RetryCommand.Execute(null);
        await Until(() => !vm.Selected!.Busy);
        Check(router.Requests[^1].Turns.Count == 3 && !router.Requests[^1].Turns.Any(turn => turn.Text!.Contains("范围查询") || turn.Text.Contains("红黑树")),
            "retrying an older question does not leak future rounds into its context");
        vm.ClearCommand.Execute(null); vm.AddSentence(SpeechSource.Speaker, "新话题", true);
        await Until(() => vm.Selected?.Busy == false);
        Check(router.Requests[^1].Turns.Count == 1, "clearing questions also clears the next request's conversation context");
        var service = new AppConfigService(new ApiKeyProtector(), new TestLogger());
        typeof(AppConfigService).GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, Path.Combine(AppContext.BaseDirectory, "context-test-settings.json"));
        config.Battle.SuppressSpeakerEcho = false;
        await service.SaveAsync(config);
        var loaded = await service.LoadAsync();
        Check(loaded.Battle.ContextPrompt == config.Battle.ContextPrompt && !loaded.Battle.SuppressSpeakerEcho,
            "custom context prompt and echo preference round-trip through settings persistence");
    }

    private static async Task TestWindowAsync()
    {
        WindowCaptureProtection.Initialize(new TestLogger());
        WindowCaptureProtection.SetEnabled(true);
        var config = AppConfig.CreateDefault(); config.WindowOpacity = 0.82;
        await using var vm = new BattleViewModel(new FakeSpeech(), new FakeRouter(), () => config, Dispatcher.CurrentDispatcher);
        vm.AddSentence(SpeechSource.Speaker, "什么是 Agent？", true);
        await Until(() => vm.Selected?.Busy == false);
        vm.Selected!.Answer = "Agent 是围绕目标进行观察、决策和行动的智能体。\n\n" + string.Join("\n\n", Enumerable.Repeat("核心流程：观察环境，制定计划，调用工具，检查结果。", 60));
        var window = new BattleWindow(vm, config);
        window.Show();
        await Task.Delay(500);
        var handle = new WindowInteropHelper(window).Handle;
        var indicator = (System.Windows.Shapes.Ellipse)window.FindName("InteractionIndicator");
        Check(!window.ShowInTaskbar && ((SolidColorBrush)indicator.Fill).Color == Color.FromRgb(48, 52, 52), "main window is hidden from taskbar and has a grey-black interactive indicator");
        Check(GetWindowDisplayAffinity(handle, out var affinity) && affinity == 0x11, "battle window retains screen capture exclusion");
        WindowInteraction.SetClickThrough(true);
        Check(((SolidColorBrush)indicator.Fill).Color == Colors.White, "header indicator turns white with mouse passthrough");
        Check(!window.IsHitTestVisible && (GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0, "battle window enters full mouse passthrough");
        WindowInteraction.SetClickThrough(false);
        Check(((SolidColorBrush)indicator.Fill).Color == Color.FromRgb(48, 52, 52), "header indicator returns to grey-black when clicks are enabled");
        Check(window.IsHitTestVisible, "battle window leaves mouse passthrough");
        var scroll = (ScrollViewer)window.FindName("AnswerScroll");
        await Until(() => scroll.ScrollableHeight > 0);
        window.ScrollConversation(1); await Task.Delay(60);
        Check(scroll.VerticalOffset > 0, "answer supports global downward scrolling");
        window.ScrollConversation(-1); await Task.Delay(60);
        Check(scroll.VerticalOffset == 0, "answer supports global upward scrolling");
        for (var i = 0; i < 35; i++) vm.Entries.Add(new BattleEntry { Source = SpeechSource.Speaker, Text = "Question " + i, Answer = "Answer " + i });
        vm.Selected = vm.Entries[0];
        vm.SelectQuestion(-1);
        Check(vm.Selected == vm.Entries[0], "previous question stops at first title");
        WindowInteraction.SetClickThrough(true);
        var opacity = window.Opacity;
        vm.SelectQuestion(1); await Task.Delay(100);
        Check(vm.Selected == vm.Entries[1] && vm.Selected.Answer == "Answer 0" && window.Opacity == opacity && !window.IsHitTestVisible,
            "question selection changes displayed answer without changing opacity or click-through");
        for (var i = 0; i < 40; i++) vm.SelectQuestion(1);
        await Task.Delay(120);
        var list = (ListBox)window.FindName("QuestionList");
        Check(vm.Selected == vm.Entries.Last() && list.SelectedItem == vm.Selected, "next question stops at last title and updates list selection");
        var item = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(vm.Selected);
        Check(item is not null && item.TransformToAncestor(list).Transform(new Point()).Y >= 0 && item.TransformToAncestor(list).Transform(new Point()).Y < list.ActualHeight,
            "keyboard navigation scrolls the selected title into view");
        vm.SelectQuestion(-1);
        Check(vm.Selected == vm.Entries[^2], "previous question returns to adjacent title");
        WindowInteraction.SetClickThrough(false);
        foreach (var size in new[] { (1120d, 700d), (760d, 480d) })
        {
            window.Width = size.Item1; window.Height = size.Item2;
            await Task.Delay(180);
            var manual = (TextBox)window.FindName("ManualInput");
            Check(manual.ActualWidth > 180 && scroll.ActualHeight > 50, $"battle layout remains usable at {size.Item1}x{size.Item2}");
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"battle-{size.Item1}.png")); encoder.Save(file);
        }
        vm.AddSentence(SpeechSource.Speaker, "最新问题：新问题应显示在列表顶部", true);
        await Until(() => vm.Selected?.Busy == false);
        await Task.Delay(100);
        var newestItem = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(vm.Selected);
        Check(list.Items[0] == vm.Selected && newestItem is not null && newestItem.TransformToAncestor(list).Transform(new Point()).Y >= 0
            && newestItem.TransformToAncestor(list).Transform(new Point()).Y < list.ActualHeight,
            "new question at top becomes visible even after browsing older titles");
        SaveWindowImage(window, "battle-newest-first.png");
        using (var tray = new TrayIconService(() => { }, () => { }, () => { }, () => { }))
        {
            Check(tray.Visible, "real system notification icon is registered");
            var icon = typeof(TrayIconService).GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
            var fields = icon.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            var added = fields.Single(f => f.Name.Equals("_added", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("added", StringComparison.OrdinalIgnoreCase));
            Check((bool)added.GetValue(icon)!, "Explorer accepted the notification-area icon");
        }
        await AnswerTypographyTests.RunAsync(window, vm, config, Check);
        await WorkspaceInteractionTests.RunAsync(window, vm, config, Check);
        window.Close();
    }

    private static byte[] CreateScreenshotFixture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 180));
            drawing.DrawText(new FormattedText("Question: What is 7 + 5?", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Arial"), 30, Brushes.Black, 1), new Point(24, 54));
        }
        var bitmap = new RenderTargetBitmap(640, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private static async Task TestCaptureLifecycleAsync()
    {
        var speech = new FakeSpeech { StartDelay = 150 };
        await using var vm = new BattleViewModel(speech, new FakeRouter(), AppConfig.CreateDefault, Dispatcher.CurrentDispatcher);
        vm.SelectQuestion(1);
        Check(vm.Selected is null, "question navigation on an empty list is harmless");
        var first = vm.StartCaptureAsync();
        Check(vm.IsStarting && !vm.ToggleRecordingCommand.CanExecute(null), "automatic startup displays loading state and prevents duplicate button actions");
        var second = vm.StartCaptureAsync();
        await Task.WhenAll(first, second);
        Check(vm.IsRecording && !vm.IsStarting && speech.Starts == 1, "concurrent automatic start requests initialize audio once");
        await vm.StopCaptureAsync();
        Check(!vm.IsRecording, "manual pause still stops automatic capture");
        first = vm.StartCaptureAsync();
        second = vm.StartCaptureAsync();
        await Task.Delay(20);
        var stop = vm.StopCaptureAsync();
        await Task.WhenAll(first, second, stop);
        Check(!vm.IsRecording && !vm.IsStarting && speech.Starts == 2, "leaving during startup cancels loading and discards queued duplicate starts");
        await vm.StartCaptureAsync();
        Check(vm.IsRecording && speech.Starts == 3, "re-entering battle can restart capture after cancellation");
    }

    private static async Task TestRecordingChoiceAsync()
    {
        var config = AppConfig.CreateDefault();
        config.Battle.SaveSpeechSessions = false;
        var path = config.Battle.SpeechArchiveDirectory;
        var speech = new FakeSpeech();
        await using var vm = new BattleViewModel(speech, new FakeRouter(), () => config, Dispatcher.CurrentDispatcher);
        bool? answer = true;
        var questions = 0;
        vm.ConfirmAudioRecording = () =>
        {
            questions++;
            Check(vm.IsStarting && !vm.IsRecording && speech.Starts == questions - 1, "recording question precedes capture and disables duplicate starts");
            return answer;
        };
        await vm.StartCaptureAsync();
        Check(vm.IsRecording && speech.RecordingChoices.SequenceEqual(new bool?[] { true }) && vm.Status.Contains("录音保存中"), "yes starts this session with audio and transcript even when the old saved switch is off");
        await vm.StartCaptureAsync();
        Check(questions == 1 && speech.Starts == 1, "already active recognition does not show another recording question");
        await vm.StopCaptureAsync();
        Check(questions == 1, "stopping recognition does not ask whether to record");
        answer = false;
        await vm.ToggleRecordingAsync();
        Check(vm.IsRecording && speech.RecordingChoices.SequenceEqual(new bool?[] { true, false }) && vm.Status.Contains("仅保存文字"), "no starts recognition with text-only archival and an accurate status");
        await vm.ToggleRecordingAsync();
        Check(!vm.IsRecording && questions == 2, "toggle-off stops the selected session without a second confirmation");
        answer = null;
        await vm.StartCaptureAsync();
        Check(!vm.IsRecording && !vm.IsStarting && speech.Starts == 2 && vm.Status.Contains("已取消"), "cancel creates no capture session and restores the start control");
        vm.ConfirmAudioRecording = () => { questions++; return true; };
        await vm.StartCaptureAsync();
        Check(questions == 4 && speech.RecordingChoices.SequenceEqual(new bool?[] { true, false, true }), "a fresh start asks again and never reuses the previous no or cancel choice");
        await vm.StopCaptureAsync();
        Check(!config.Battle.SaveSpeechSessions && config.Battle.SpeechArchiveDirectory == path, "per-session choices do not mutate saved recording preferences or the save directory");
        vm.ConfirmAudioRecording = () => { _ = vm.StopCaptureAsync(); return true; };
        await vm.StartCaptureAsync();
        await Until(() => !vm.IsStarting);
        Check(!vm.IsRecording && speech.Starts == 3, "stop requested during confirmation prevents audio startup after the dialog closes");
    }

    private static async Task TestScreenshotsAsync()
    {
        var config = AppConfig.CreateDefault();
        config.Primary.Model = "vision-primary"; config.Secondary.Model = "vision-fallback";
        config.RemoteCaptureMode = RemoteCaptureMode.PresetRegion;
        config.RemoteCaptureRegionX = 10; config.RemoteCaptureRegionY = 20; config.RemoteCaptureRegionWidth = 300; config.RemoteCaptureRegionHeight = 200;
        config.ConversationModels.Primary.Model = "conversation-primary";
        config.ConversationModels.Secondary.Model = "conversation-fallback";
        config.ConversationModels.PreferredModel = PreferredModelTarget.Secondary;
        config.Primary.ApiKey = "dummy-screenshot-key";
        config.ConversationModels.Primary.ApiKey = "dummy-conversation-key";
        config.ConversationModels.Secondary.ApiKey = "dummy-conversation-fallback-key";
        var route = BattlePrompt.Build(config, new BattleEntry { Source = SpeechSource.Speaker, Text = "speech" });
        Check(route.Config.Primary.Model == "conversation-primary" && route.Config.Secondary.Model == "conversation-fallback" && route.Config.PreferredModel == PreferredModelTarget.Secondary,
            "conversation uses its own primary, fallback and preferred model");
        Check(config.Primary.Model == "vision-primary", "conversation routing leaves screenshot configuration intact");
        var service = new AppConfigService(new ApiKeyProtector(), new TestLogger());
        var path = Path.Combine(AppContext.BaseDirectory, "screenshot-test-settings.json");
        typeof(AppConfigService).GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, path);
        await service.SaveAsync(config);
        var loaded = await service.LoadAsync();
        Check(loaded.ConversationModels.Primary.ApiKey == config.ConversationModels.Primary.ApiKey && loaded.ConversationModels.Secondary.ApiKey == config.ConversationModels.Secondary.ApiKey,
            "both conversation credentials survive protected persistence");
        Check(loaded.Primary.ApiKey == config.Primary.ApiKey && loaded.ConversationModels.Primary.Model != loaded.Primary.Model, "screenshot and conversation settings persist independently");
        Check(loaded.RemoteCaptureMode == RemoteCaptureMode.PresetRegion && loaded.RemoteCaptureRegionX == 10 && loaded.RemoteCaptureRegionY == 20 &&
            loaded.RemoteCaptureRegionWidth == 300 && loaded.RemoteCaptureRegionHeight == 200, "saved shortcut capture region survives configuration persistence");
        var json = await File.ReadAllTextAsync(path);
        Check(!json.Contains("dummy-"), "all API keys are protected at rest");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        legacy.AsObject().Remove("conversationModels");
        await File.WriteAllTextAsync(path, legacy.ToJsonString());
        var migrated = await service.LoadAsync();
        Check(migrated.ConversationModels.Primary.Model == config.Primary.Model && migrated.ConversationModels.Primary.ApiKey == config.Primary.ApiKey,
            "legacy model settings migrate to usable independent conversation settings");
        migrated.ConversationModels.Primary.ApiKey = "changed";
        Check(migrated.Primary.ApiKey == config.Primary.ApiKey, "editing migrated conversation credentials does not edit screenshot credentials");

        var fake = new FakeScreenshots { Result = new ScreenshotCaptureResult { ImageBytes = CreateScreenshotFixture() } };
        var router = new FakeRouter();
        await using var vm = new BattleViewModel(new FakeSpeech(), router, () => config, Dispatcher.CurrentDispatcher, fake);
        WindowInteraction.SetClickThrough(true);
        vm.ScreenshotCommand.Execute(null);
        await Until(() => vm.Selected?.Busy == false && !vm.IsScreenshotBusy);
        Check(vm.Entries.Count == 1 && vm.Selected!.Source == SpeechSource.Speaker && vm.Selected.IsScreenshot && vm.Selected.ImagePreview is not null,
            "regional screenshot creates an opponent question with retained image preview");
        Check(fake.LastKind == "region" && fake.Region == new System.Drawing.Rectangle(10, 20, 300, 200) && fake.SelectionCalls == 0 &&
            fake.CapturedWithPassthrough && WindowInteraction.IsClickThrough, "shortcut captures exact saved region without selection while mouse passthrough stays active");
        WindowInteraction.SetClickThrough(false);
        var request = router.Requests.Last();
        Check(request.Config.Primary.Model == "vision-primary" && request.Config.Secondary.Model == "vision-fallback" && request.Config.PreferredModel == PreferredModelTarget.Primary,
            "screenshot questions use the separate screenshot model route");
        Check(request.Turns.Single().ImageBytes!.SequenceEqual(fake.Result.ImageBytes) && request.Turns.Single().ImageMediaType == "image/png",
            "screenshot request includes the original image for multimodal recognition");
        Check(request.Config.Primary.SystemPrompt == config.Primary.SystemPrompt + BattlePrompt.AnswerFormatting + ScreenshotQuestion.AnswerInstructions && request.Config.Primary.SystemPrompt != config.Battle.ConversationPrompt,
            "screenshot uses its own prompt instead of the conversation prompt");
        var first = vm.Selected;
        vm.AddSentence(SpeechSource.Speaker, "speech next", true);
        await Until(() => vm.Entries.All(e => !e.Busy));
        Check(vm.Entries[0].Text == "speech next" && vm.Entries[1] == first && vm.Selected == vm.Entries[0], "speech inserts above screenshot without detaching its answer");
        Check(first!.Answer == "Test answer" && router.Requests.Last().Config.Primary.Model == "conversation-primary", "interleaved speech retains screenshot answer under its original title");
        vm.Selected = first;
        router.Fail = true; vm.RetryCommand.Execute(null);
        await Until(() => !first.Busy);
        Check(first.Status.Contains("请求失败") && first.ImageBytes is { Length: > 0 }, "failed screenshot retains its image for retry");
        router.Fail = false; vm.RetryCommand.Execute(null);
        await Until(() => !first.Busy);
        Check(first.Answer == "Test answer" && router.Requests.Last().Turns.Single().ImageBytes is { Length: > 0 }, "screenshot retry resends image and updates the same question");
        var before = vm.Entries.Count;
        fake.Result = null; await vm.CaptureScreenshotAsync();
        Check(vm.Entries.Count == before && !vm.IsScreenshotBusy, "empty configured capture adds no empty question");
        fake.Result = new ScreenshotCaptureResult { ImageBytes = CreateScreenshotFixture() };
        config.RemoteCaptureMode = RemoteCaptureMode.PresetRegion;
        config.RemoteCaptureRegionWidth = 0; config.RemoteCaptureRegionHeight = 0;
        Check(!vm.QueueRemoteScreenshot().Accepted, "phone preset capture rejects missing saved region");
        var captures = fake.CaptureCalls; var requests = router.Requests.Count;
        await vm.CaptureScreenshotAsync();
        Check(fake.CaptureCalls == captures && router.Requests.Count == requests && vm.Entries.Count == before && !vm.IsScreenshotBusy && vm.Status.Contains("设置"),
            "shortcut with missing preset reports settings without picking a region, capturing desktop or sending a question");
        config.RemoteCaptureRegionX = 10; config.RemoteCaptureRegionY = 20; config.RemoteCaptureRegionWidth = 300; config.RemoteCaptureRegionHeight = 200;
        fake.Delay = 150;
        Check(vm.QueueRemoteScreenshot().Accepted && !vm.QueueRemoteScreenshot().Accepted, "phone capture accepts once and rejects overlapping capture");
        await Until(() => !vm.IsScreenshotBusy && vm.Entries.All(e => !e.Busy));
        Check(fake.LastKind == "region" && fake.Region == new System.Drawing.Rectangle(10, 20, 300, 200) && vm.Entries.Count == before + 1,
            "phone uses the saved region and appends a battle question");
        Check(vm.Entries[0] == vm.Selected && vm.Entries[0].IsScreenshot && vm.Entries[1].Text == "speech next" && vm.Entries[2] == first,
            "new phone screenshot takes the top position while older mixed questions retain order");
        config.RemoteCaptureRegionX = -120; config.RemoteCaptureRegionWidth = 240;
        before = vm.Entries.Count;
        vm.ScreenshotCommand.Execute(null);
        Check(vm.IsScreenshotBusy && !vm.ScreenshotCommand.CanExecute(null), "shortcut prevents overlapping captures while direct screenshot is running");
        await Until(() => !vm.IsScreenshotBusy && vm.Entries.All(e => !e.Busy));
        Check(fake.Region == new System.Drawing.Rectangle(-120, 20, 240, 200) && fake.SelectionCalls == 0 && vm.Entries.Count == before + 1 && vm.Selected!.Answer == "Test answer",
            "repeated shortcut reads updated region coordinates and automatically answers a new question");
        config.RemoteCaptureMode = RemoteCaptureMode.FullDesktop;
        await vm.CaptureScreenshotAsync();
        await Until(() => vm.Entries.All(e => !e.Busy));
        Check(fake.LastKind == "desktop" && fake.SelectionCalls == 0, "shortcut uses direct desktop capture when configured for entire desktop");
        Check(vm.QueueRemoteScreenshot().Accepted, "phone full-desktop capture is accepted while audio is stopped");
        await Until(() => !vm.IsScreenshotBusy && vm.Entries.All(e => !e.Busy));
        Check(fake.LastKind == "desktop" && vm.Selected!.IsScreenshot, "phone full-desktop result enters the battle question list");
        using (var server = new RemoteCaptureServer(new TestLogger(),
            () => Dispatcher.CurrentDispatcher == Application.Current.Dispatcher ? vm.QueueRemoteScreenshot() : Application.Current.Dispatcher.Invoke(vm.QueueRemoteScreenshot),
            (_, _) => new(false, "unused"), () => "test", () => false, _ => Task.FromResult(new RemoteCompactModeResult(true, "test", false)),
            () => [], _ => new(false, "unused"), _ => new(false, "unused")))
        {
            server.Start();
            var count = vm.Entries.Count;
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var reply = await http.PostAsync($"http://127.0.0.1:{server.Port}/capture", new System.Net.Http.StringContent(""));
            await Until(() => !vm.IsScreenshotBusy && vm.Entries.All(e => !e.Busy));
            Check(reply.IsSuccessStatusCode && vm.Entries.Count == count + 1 && vm.Selected!.IsScreenshot,
                "real phone HTTP capture endpoint produces a battle screenshot question");
        }
        before = vm.Entries.Count;
        var capturing = vm.CaptureScreenshotAsync(); vm.CancelRequests(); await capturing;
        Check(vm.Entries.Count == before && !vm.IsScreenshotBusy, "cancelling outstanding work aborts screenshot without adding a question");
        var window = new BattleWindow(vm, config); window.Show(); await Task.Delay(100);
        Check(((Image)window.FindName("ScreenshotOriginalImage")).Source is not null && ((System.Windows.Controls.Grid)window.FindName("ScreenshotQuestionPane")).IsVisible,
            "selected screenshot retains an expandable original image beside the extracted question");
        using (var cancellation = new CancellationTokenSource(350))
        {
            try { await new ScreenshotService(new TestLogger()).CaptureAsync(cancellation.Token); }
            catch (OperationCanceledException) { }
            Check(window.IsVisible, "cancelling the real region overlay closes it and restores battle window");
        }
        SaveWindowImage(window, "battle-screenshot.png");
        window.Close();
        config.Battle.Speech.ApiKey = "saved-ui-test-key";
        var settings = new SettingsWindow(config, AppConfig.CreateDefault()); settings.Show(); await Task.Delay(100);
        Check(!settings.ShowInTaskbar && (GetWindowLongPtr(new WindowInteropHelper(settings).Handle, -20).ToInt64() & 0x40080) == 0x80,
            "settings window uses native tool-window flags without a taskbar entry");
        var sections = (TabControl)settings.FindName("SettingsTabs");
        Check(ReferenceEquals(sections.SelectedItem, settings.FindName("UserGuideTab")), "settings open on the first user guide tab");
        sections.SelectedItem = settings.FindName("AppearanceTab"); await Task.Delay(80);
        Check(FindVisual<Slider>(settings).Any(s => s.Minimum == 14 && s.Maximum == 28), "settings expose the full readable answer font range");
        Check(((SolidColorBrush)settings.Background).Color == Colors.White && FindVisual<TextBox>(settings).All(t => t.Foreground is SolidColorBrush brush && brush.Color.R < 128),
            "settings use white surfaces and dark readable field text");
        sections.SelectedItem = settings.FindName("ModelsTab"); await Task.Delay(80);
        var tabs = (TabControl)settings.FindName("ModelTabs");
        foreach (var tab in tabs.Items.Cast<TabItem>())
        {
            tabs.SelectedItem = tab; await Task.Delay(80);
            Check(FindVisual<TextBox>(settings).Any(t => t.Text == (tabs.SelectedIndex == 0 ? "vision-primary" : "conversation-primary")), "settings renders independent " + tab.Header + " fields");
            SaveWindowImage(settings, "settings-models-" + tabs.SelectedIndex + ".png");
        }
        sections.SelectedItem = settings.FindName("SpeechTab"); await Task.Delay(80);
        var speechGroup = (System.Windows.Controls.GroupBox)settings.FindName("SpeechServiceSection");
        speechGroup.BringIntoView(); await Task.Delay(120);
        var speechKeyBox = (PasswordBox)settings.FindName("SpeechApiKeyBox");
        Check(speechKeyBox.Password == "saved-ui-test-key" &&
            !FindVisual<System.Windows.Controls.CheckBox>(speechGroup).Any(c => c.Content?.ToString()?.Contains("二遍") == true) &&
            !FindVisual<System.Windows.Controls.TextBlock>(speechGroup).Any(t => t.Text.Contains("热词") || t.Text.Contains("识别上下文")),
            "cloud settings show a masked saved API Key with no second-pass, hotword or context controls");
        speechKeyBox.Password = "edited-ui-test-key";
        Check(((SettingsViewModel)settings.DataContext).BuildUpdatedConfig().Battle.Speech.ApiKey == "edited-ui-test-key" &&
            config.Battle.Speech.ApiKey == "saved-ui-test-key", "editing speech API Key updates the settings snapshot without modifying the active configuration");
        Check(((TextBox)settings.FindName("SpeechArchivePathBox")).IsEnabled &&
            ((TextBox)settings.FindName("SpeechArchivePathBox")).Text == config.Battle.SpeechArchiveDirectory, "archive directory remains editable for both session recording choices");
        SaveWindowImage(settings, "settings-speech.png");
        await SettingsOrganizationTests.RunAsync(settings, config, Check);
        settings.Close();
        await SettingsOrganizationTests.TestDialogsAsync(config, Check);
    }

    private static void SaveWindowImage(Window window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }

    private static async Task TestConfiguredScreenshotModelAsync()
    {
        var logger = new TestLogger();
        var config = await new AppConfigService(new ApiKeyProtector(), logger).LoadAsync();
        var router = new ModelRouter(new OpenAiModelClient(logger), logger);
        await using var vm = new BattleViewModel(new FakeSpeech(), router, () => config, Dispatcher.CurrentDispatcher);
        vm.AddScreenshot(new ScreenshotCaptureResult { ImageBytes = CreateScreenshotFixture() });
        await Until(() => vm.Selected?.Busy == false, 180000);
        Check(vm.Selected!.HasExtractedQuestion && vm.Selected.Title.Length > 0, "configured screenshot model extracts a generated problem before solving");
        Check(vm.Selected!.Answer.Contains("12") || vm.Selected.Answer.Contains("十二"), "configured screenshot model reads a generated image and answers 7 + 5 correctly");
    }

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in FindVisual<T>(child)) yield return nested;
        }
    }

    private static async Task TestConfiguredModelAsync()
    {
        var logger = new TestLogger();
        var service = new AppConfigService(new ApiKeyProtector(), logger);
        var config = await service.LoadAsync();
        config.Battle.ConversationPrompt = "这是软件连通性测试。请仅回答：测试成功。";
        var router = new ModelRouter(new OpenAiModelClient(logger), logger);
        await using var vm = new BattleViewModel(new FakeSpeech(), router, () => config, Dispatcher.CurrentDispatcher);
        vm.AddSentence(SpeechSource.Speaker, "请确认这条测试请求已经收到。", true);
        await Until(() => vm.Selected?.Busy == false, 120000);
        Check(!string.IsNullOrWhiteSpace(vm.Selected?.Answer), "configured model returns an answer through actual battle-mode routing");
    }

    private static async Task Until(Func<bool> condition, int timeout = 6000)
    {
        var expiry = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!condition()) { if (DateTime.UtcNow >= expiry) throw new TimeoutException("Test condition timed out."); await Task.Delay(20); }
    }
    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("FAIL: " + message);
        _checks++; Console.WriteLine("PASS: " + message);
    }
    private sealed class FakeSpeech : IBattleSpeechService
    {
        public event Action<SpeechSegment>? Transcribed;
        public event Action<string>? Failed { add { } remove { } }
        public int Starts, Stops, StartDelay;
        public List<bool?> RecordingChoices = [];
        public void Emit(SpeechSegment segment) => Transcribed?.Invoke(segment);
        public async Task StartAsync(string microphoneId, string speakerId, CancellationToken token, bool? recordAudio = null) { Starts++; RecordingChoices.Add(recordAudio); await Task.Delay(StartDelay, token); }
        public Task StopAsync() { Stops++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeRouter : IModelRouter
    {
        public List<(AppConfig Config, IReadOnlyList<ConversationTurn> Turns)> Requests = [];
        public int Calls, Active, Peak, Delay = 60;
        public bool Fail;
        public async Task<ModelReply> AskAsync(AppConfig config, IReadOnlyList<ConversationTurn> turns, CancellationToken token)
        {
            Requests.Add((config, turns)); Calls++; Active++; Peak = Math.Max(Peak, Active);
            try
            {
                await Task.Delay(Delay, token); if (Fail) throw new IOException("Simulated failure");
                return new ModelReply { Text = config.Primary.SystemPrompt == ScreenshotQuestion.ExtractionInstructions ? "{\"hasQuestion\":true,\"title\":\"测试题目\",\"questionMarkdown\":\"请解答截图中的测试题目。\"}" : "Test answer" };
            }
            finally { Active--; }
        }
    }
    private sealed class FakeScreenshots : IScreenshotService
    {
        public ScreenshotCaptureResult? Result;
        public string LastKind = "";
        public System.Drawing.Rectangle Region;
        public bool CapturedWithPassthrough;
        public int SelectionCalls;
        public int CaptureCalls;
        public int Delay;
        public Task<ScreenshotCaptureResult?> CaptureAsync(CancellationToken token) { SelectionCalls++; return Capture("selection", token); }
        public Task<ScreenshotCaptureResult?> CaptureFullDesktopAsync(CancellationToken token) => Capture("desktop", token);
        public Task<ScreenshotCaptureResult?> CaptureRegionAsync(System.Drawing.Rectangle region, CancellationToken token) { Region = region; return Capture("region", token); }
        private async Task<ScreenshotCaptureResult?> Capture(string kind, CancellationToken token) { CaptureCalls++; CapturedWithPassthrough = WindowInteraction.IsClickThrough; LastKind = kind; await Task.Delay(Delay, token); return Result; }
    }
    private sealed class TestLogger : ILoggerService
    {
        public void Info(string message) { }
        public void Warn(string message) => Console.WriteLine("WARN: " + message);
        public void Error(string message, Exception? ex = null) => Console.WriteLine("ERROR: " + message);
    }
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
