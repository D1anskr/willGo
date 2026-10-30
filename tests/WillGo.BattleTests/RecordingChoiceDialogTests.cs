using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using FloatingDeskAssistant.Configuration;
using FloatingDeskAssistant.UI.Windows;
using FloatingDeskAssistant.ViewModels;

internal static class RecordingChoiceDialogTests
{
    public static async Task RunAsync(BattleViewModel viewModel, Action<bool, string> check)
    {
        var window = new BattleWindow(viewModel, AppConfig.CreateDefault());
        window.Show();
        var thread = GetCurrentThreadId();
        foreach (var (button, expected) in new (int, bool?)[] { (6, true), (7, false), (2, null) })
        {
            var driver = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    var dialog = IntPtr.Zero;
                    EnumThreadWindows(thread, (handle, _) =>
                    {
                        var title = new StringBuilder(128);
                        GetWindowText(handle, title, title.Capacity);
                        if (title.ToString() == "willGo · 本次录制") dialog = handle;
                        return true;
                    }, IntPtr.Zero);
                    if (dialog != IntPtr.Zero)
                    {
                        await Task.Delay(150);
                        if (!PostMessage(dialog, 0x0111, new IntPtr(button), IntPtr.Zero))
                            PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                    await Task.Delay(20);
                }
                throw new TimeoutException("Recording choice dialog did not appear on its owning UI thread.");
            });
            var actual = window.ConfirmAudioRecording();
            await driver;
            check(actual == expected, "actual native recording dialog maps button " + button + " to the expected per-session choice");
        }
        window.Close();
    }

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
