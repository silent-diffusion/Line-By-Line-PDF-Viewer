using System.Windows;
using LineByLine.Services;

namespace LineByLine;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new SettingsStore();
        store.Load();

        var window = new MainWindow(store);
        MainWindow = window;
        window.Show();

        // "Open with" / drag onto the exe passes the file path as the first argument.
        if (e.Args.Length > 0)
            window.OpenFile(e.Args[0]);
    }
}
