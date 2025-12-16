using System.Windows;
using MappFyren.App.ViewModels;

namespace MappFyren.App.Views;

public partial class MainWindow : Window
{
  public MainWindow(MainViewModel vm)
  {
    InitializeComponent();
    DataContext = vm;
  }
}
