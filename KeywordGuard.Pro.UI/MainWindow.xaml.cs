using System.Windows;

namespace KeywordGuard.Pro.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel();
        Closing += (s, e) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.OnWindowClosing();
        };
    }
}