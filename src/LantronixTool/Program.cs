using Avalonia;

namespace LantronixTool;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine(App.Version);
            return 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // also used by the XAML previewer
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
