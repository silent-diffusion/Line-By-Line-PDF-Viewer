using Velopack;

namespace LineByLine;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: during install, update and uninstall, Velopack launches the app with
        // special arguments, does its work here and exits before any window appears.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
