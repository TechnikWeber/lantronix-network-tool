#pragma warning disable CS0618 // Bitmap.Save(path): the replacement needs encoder options we do not care about
// Usage: dotnet run --project tools/Screenshot -- out.png [--search] [--assign] [--dark]
// --search runs a real search first (start demo/fake_devices.py for the README picture).
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using LantronixTool;

var output = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "screenshot.png";
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

if (args.Contains("--dark")) Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
else Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

var window = new MainWindow { Width = 760, Height = 470 };
window.Show();
var done = new CancellationTokenSource();
Dispatcher.UIThread.Post(async () =>
{
    try
    {
        if (args.Contains("--search")) await window.SearchAsync();
        if (args.Contains("--assign"))
        {
            var dialog = new AssignWindow(null);
            _ = dialog.ShowDialog(window);
            await Task.Delay(300);
            dialog.CaptureRenderedFrame()?.Save(output);
            done.Cancel();
            return;
        }
        await Task.Delay(300);
        window.CaptureRenderedFrame()?.Save(output);
    }
    catch (Exception e)
    {
        Console.Error.WriteLine(e);
        Environment.ExitCode = 1;
    }
    done.Cancel();
});
Dispatcher.UIThread.MainLoop(done.Token);
if (!File.Exists(output)) { Console.Error.WriteLine("no frame rendered"); return 1; }
Console.WriteLine(output);
return Environment.ExitCode;
