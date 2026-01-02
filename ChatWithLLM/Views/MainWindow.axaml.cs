using Avalonia.Controls;
using ChatWithLLM.ViewModels;

namespace ChatWithLLM.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetWindow(this);
            }
        };
    }
}