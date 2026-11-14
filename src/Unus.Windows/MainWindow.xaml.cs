
using System.Windows;

namespace Unus.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        LocalDeviceName.Text = Environment.MachineName;
    }
}
