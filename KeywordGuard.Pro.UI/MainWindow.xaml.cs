using System.Windows;
using System.Windows.Threading;

namespace KeywordGuard.Pro.UI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _hideClickResetTimer;
    private int _hideClickCount;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel();

        _hideClickResetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _hideClickResetTimer.Tick += (_, _) =>
        {
            _hideClickCount = 0;
            _hideClickResetTimer.Stop();
        };

        Closing += (s, e) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.OnWindowClosing();
        };
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _hideClickCount++;
        _hideClickResetTimer.Stop();
        _hideClickResetTimer.Start();

        if (_hideClickCount < 3) return;

        _hideClickCount = 0;
        _hideClickResetTimer.Stop();
        OpenHiddenAccess();
    }

    private void OpenHiddenAccess()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var popup = new HiddenAccessWindow(vm)
        {
            Owner = this
        };
        popup.ShowDialog();
    }
}