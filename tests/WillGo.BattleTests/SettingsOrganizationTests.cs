using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using TabControl = System.Windows.Controls.TabControl;

internal static class SettingsOrganizationTests
{
    public static async Task RunAsync(SettingsWindow window, AppConfig original, Action<bool, string> check)
    {
        var baseline = JsonSerializer.Serialize(original);
        var vm = (SettingsViewModel)window.DataContext;
        var sections = (TabControl)window.FindName("SettingsTabs");
        var models = (TabControl)window.FindName("ModelTabs");
        check(sections.Items.Cast<TabItem>().Select(t => t.Header.ToString()).SequenceEqual(new[] { "使用说明", "模型设置", "语音设置", "快捷提问", "界面设置" }),
            "settings expose the guide first followed by four Chinese configuration sections");
        sections.SelectedItem = window.FindName("UserGuideTab"); await Task.Delay(60);
        var shortcuts = (Grid)window.FindName("GuideShortcuts");
        check(new[] { "Ctrl + 1", "Ctrl + Shift + S", "Ctrl + Shift + 左 / 右", "Ctrl + Shift + 上 / 下", "Ctrl + 上 / 下", "Enter / Shift + Enter" }
            .All(key => Visuals<TextBlock>(shortcuts).Any(text => text.Text == key)), "guide includes every active global shortcut and composer keys");
        check(window.Title == "设置" && System.Windows.Shell.WindowChrome.GetWindowChrome(window).CornerRadius.TopLeft == 16,
            "settings use a Chinese title and the same native rounded frame as the workspace");
        var oldLabels = new[] { "Appearance", "Remote Capture", "Remote Prompt Presets", "BaseUrl", "ApiKey", "Protocol", "Model", "SystemPrompt", "Defaults", "Cancel", "Save", "Enabled", "Up", "Down", "Delete" };
        models.SelectedIndex = 0;
        foreach (TabItem section in sections.Items)
        {
            sections.SelectedItem = section; await Task.Delay(60);
            check(!Visuals<TextBlock>(window).Any(text => oldLabels.Contains(text.Text)) &&
                !Visuals<Button>(window).Any(button => oldLabels.Contains(button.Content?.ToString() ?? "")),
                "visible labels are localized in " + section.Header);
            SaveImage(window, "settings-section-" + sections.SelectedIndex + ".png");
        }
        sections.SelectedItem = window.FindName("ModelsTab"); models.SelectedIndex = 0; await Task.Delay(60);
        var preferred = (ComboBox)window.FindName("ScreenshotPreferredModel");
        preferred.SelectedValue = PreferredModelTarget.Secondary;
        check(vm.BuildUpdatedConfig().PreferredModel == PreferredModelTarget.Secondary && preferred.Items.Cast<SettingsOption<PreferredModelTarget>>().All(x => x.Label is "主模型" or "备用模型"),
            "localized preferred model choices retain their original enum values");
        var protocol = Visuals<ComboBox>(window).Single(box => ReferenceEquals(box.DataContext, vm.EditableConfig.Primary) && box.SelectedValue is ModelApiProtocol);
        protocol.SelectedValue = ModelApiProtocol.OpenAiResponses;
        protocol.IsDropDownOpen = true; await Task.Delay(60);
        check(protocol.ItemContainerGenerator.ContainerFromIndex(2) is ComboBoxItem && protocol.Items.Count == 4,
            "rounded protocol dropdown realizes selectable localized options");
        protocol.IsDropDownOpen = false;
        check(vm.BuildUpdatedConfig().Primary.Protocol == ModelApiProtocol.OpenAiResponses && vm.EditableConfig.ConversationModels.Primary.Protocol == original.ConversationModels.Primary.Protocol,
            "editing screenshot protocol leaves conversation routing independent");
        var regionMode = (ComboBox)window.FindName("RemoteCaptureModeBox");
        regionMode.SelectedValue = RemoteCaptureMode.PresetRegion;
        vm.SetRemoteCaptureRegion(new System.Drawing.Rectangle(21, 32, 640, 480));
        check(vm.BuildUpdatedConfig().RemoteCaptureMode == RemoteCaptureMode.PresetRegion && vm.RemoteCaptureRegionSummary.Contains("横坐标 21") && vm.RemoteCaptureRegionSummary.Contains("宽度 640"),
            "screenshot tab preserves phone capture mode and displays Chinese region coordinates");
        models.SelectedIndex = 1; await Task.Delay(60);
        ((ComboBox)window.FindName("ConversationPreferredModel")).SelectedValue = PreferredModelTarget.Primary;
        var conversationModel = Visuals<TextBox>(window).Single(box => ReferenceEquals(box.DataContext, vm.EditableConfig.ConversationModels.Primary) && box.Text == original.ConversationModels.Primary.Model);
        conversationModel.Text = "edited-conversation-model";
        check(vm.BuildUpdatedConfig().ConversationModels.Primary.Model == "edited-conversation-model" && vm.BuildUpdatedConfig().Primary.Model == original.Primary.Model,
            "conversation model edits survive separate model tab navigation");
        sections.SelectedItem = window.FindName("SpeechTab"); await Task.Delay(60);
        var context = (TextBox)window.FindName("ContextPromptBox");
        check(context.Text == original.Battle.ContextPrompt && ((System.Windows.Controls.CheckBox)window.FindName("SuppressSpeakerEchoBox")).IsChecked == true,
            "speech settings expose the default context prompt and enabled echo filter");
        context.BringIntoView(); await Task.Delay(80);
        SaveImage(window, "settings-context.png");
        context.Text = "Java 后端技术面试，结合最近两轮问答识别术语。";
        ((System.Windows.Controls.CheckBox)window.FindName("SuppressSpeakerEchoBox")).IsChecked = false;
        check(vm.BuildUpdatedConfig().Battle.ContextPrompt == context.Text && !vm.BuildUpdatedConfig().Battle.SuppressSpeakerEcho,
            "editing context and echo controls updates only the settings snapshot");
        ((TextBox)window.FindName("SpeechPauseBox")).Text = "1.4";
        ((TextBox)window.FindName("SpeechPauseBox")).GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        ((TextBox)window.FindName("ConversationPromptBox")).Text = "中文对话提示词 Kafka";
        ((TextBox)window.FindName("TriggerPhrasesBox")).Text = "我想一想\n让我想想";
        ((TextBox)window.FindName("SpeechArchivePathBox")).Text = Path.Combine(Path.GetTempPath(), "willgo-settings-ui");
        var microphone = (ComboBox)window.FindName("MicrophoneBox"); microphone.SelectedIndex = microphone.Items.Count - 1;
        var speaker = (ComboBox)window.FindName("SpeakerBox"); speaker.SelectedIndex = speaker.Items.Count - 1;
        var edited = vm.BuildUpdatedConfig();
        check(edited.Battle.SpeechPauseSeconds == 1.4 && edited.Battle.ConversationPrompt == "中文对话提示词 Kafka" && edited.Battle.TriggerPhrases.Contains('\n') &&
            edited.Battle.MicrophoneDeviceId == (string)microphone.SelectedValue && edited.Battle.SpeakerDeviceId == (string)speaker.SelectedValue &&
            edited.Battle.SpeechArchiveDirectory.EndsWith("willgo-settings-ui"), "speech section binds pause, prompt, triggers, devices and archive directory together");
        sections.SelectedItem = window.FindName("QuickQuestionsTab"); await Task.Delay(60);
        var count = vm.PromptPresets.Count;
        ((Button)window.FindName("AddPromptPresetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(60);
        var preset = vm.PromptPresets.Last(); preset.Title = "测试预设"; preset.Prompt = "解释 Kafka 的消费组";
        var up = Visuals<Button>(window).Single(button => ReferenceEquals(button.DataContext, preset) && button.ToolTip?.ToString() == "上移");
        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(vm.PromptPresets.Count == count + 1 && vm.PromptPresets.IndexOf(preset) == Math.Max(0, count - 1),
            "quick questions support adding, editing and moving a preset using icon controls");
        var serializedPreset = vm.BuildUpdatedConfig().PromptPresets.Single(item => item.Title == "测试预设");
        check(serializedPreset.Id == preset.Id && serializedPreset.Prompt.Contains("Kafka"), "preset editing preserves stable IDs and original technical names");
        var delete = Visuals<Button>(window).Single(button => ReferenceEquals(button.DataContext, preset) && button.ToolTip?.ToString() == "删除问题");
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(vm.PromptPresets.Count == count && !vm.PromptPresets.Contains(preset), "quick question delete removes the intended item");
        sections.SelectedItem = window.FindName("AppearanceTab"); await Task.Delay(60);
        Visuals<Slider>(window).Single(slider => slider.Maximum == 28).Value = 25;
        Visuals<Slider>(window).Single(slider => slider.Maximum == 1).Value = .65;
        check(vm.BuildUpdatedConfig().Battle.AnswerFontSize == 25 && vm.BuildUpdatedConfig().WindowOpacity == .65, "appearance section retains editable typography and opacity values");
        sections.SelectedItem = window.FindName("SpeechTab"); await Task.Delay(60);
        check(((TextBox)window.FindName("ConversationPromptBox")).Text == "中文对话提示词 Kafka" && ((PasswordBox)window.FindName("SpeechApiKeyBox")).Password == "edited-ui-test-key",
            "switching all sections retains speech text and masked credentials");
        window.Width = 800; window.Height = 560; await Task.Delay(80);
        foreach (TabItem section in sections.Items)
        {
            sections.SelectedItem = section; await Task.Delay(50);
            var save = (Button)window.FindName("SaveButton"); var point = save.TransformToAncestor(window).Transform(new System.Windows.Point());
            var scrolling = section.Content is ScrollViewer rootScroll ? new[] { rootScroll } : Visuals<ScrollViewer>((DependencyObject)section.Content).ToArray();
            check(save.ActualWidth >= 96 && point.X + save.ActualWidth <= window.ActualWidth && point.Y + save.ActualHeight <= window.ActualHeight &&
                scrolling.Any(scroll => scroll.ViewportWidth > 250 && scroll.ViewportHeight > 80),
                "minimum-size settings keep scrolling and save controls available in " + section.Header);
            SaveImage(window, "settings-minimum-" + sections.SelectedIndex + ".png");
        }
        sections.SelectedItem = window.FindName("UserGuideTab"); await Task.Delay(60);
        var guide = (ScrollViewer)window.FindName("UserGuideScroll");
        check(guide.ScrollableHeight > 0 && guide.ExtentWidth <= guide.ViewportWidth + 1 &&
            Visuals<TextBlock>(shortcuts).All(text => text.ActualWidth > 100 && text.ActualWidth < guide.ViewportWidth),
            "guide shortcut columns fit the minimum settings width without horizontal scrolling");
        guide.ScrollToEnd(); await Task.Delay(60);
        var note = (TextBlock)window.FindName("GuideSaveNote");
        var noteBounds = note.TransformToAncestor(guide).TransformBounds(new Rect(note.RenderSize));
        check(guide.VerticalOffset >= guide.ScrollableHeight - 1 && noteBounds.Top >= 0 && noteBounds.Bottom <= guide.ViewportHeight + 1,
            "guide can scroll to the complete final instructions at minimum window size");
        SaveImage(window, "settings-guide-bottom.png");
        check(JsonSerializer.Serialize(original) == baseline, "settings edits across all sections leave the running configuration unchanged until save");
        ((Button)window.FindName("DefaultsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(vm.EditableConfig.Battle.Speech.ApiKey == "" && ((PasswordBox)window.FindName("SpeechApiKeyBox")).Password == "" &&
            vm.EditableConfig.Battle.SpeechPauseSeconds == 1.1, "restore defaults refreshes masked credentials and all section bindings");
    }

    public static Task TestDialogsAsync(AppConfig original, Action<bool, string> check)
    {
        var baseline = JsonSerializer.Serialize(original);
        foreach (var save in new[] { false, true })
        {
            var dialog = new SettingsWindow(original, AppConfig.CreateDefault());
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ((TabControl)dialog.FindName("SettingsTabs")).SelectedItem = dialog.FindName("SpeechTab"); dialog.UpdateLayout();
                ((TextBox)dialog.FindName("TriggerPhrasesBox")).Text = "仅用于保存校验";
                ((Button)dialog.FindName(save ? "SaveButton" : "CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            var result = dialog.ShowDialog();
            check(save ? result == true && dialog.UpdatedConfig?.Battle.TriggerPhrases == "仅用于保存校验" : result == false && dialog.UpdatedConfig is null,
                save ? "save closes the dialog with the edited configuration" : "cancel closes the dialog without exposing an updated configuration");
        }
        check(JsonSerializer.Serialize(original) == baseline, "save and cancel dialogs never mutate their source configuration");
        return Task.CompletedTask;
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void SaveImage(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(file);
    }
}
