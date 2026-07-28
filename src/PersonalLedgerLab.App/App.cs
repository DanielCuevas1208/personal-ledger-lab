using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PersonalLedgerLab.App.ViewModels;

namespace PersonalLedgerLab.App;

/// <summary>
/// Application entry style. Builds the theme set in code so the project
/// does not depend on the Avalonia XAML source generator.
/// </summary>
public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://PersonalLedgerLab.App", UriKind.RelativeOrAbsolute))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml", UriKind.RelativeOrAbsolute)
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(new MainViewModel());
        }

        base.OnFrameworkInitializationCompleted();
    }
}