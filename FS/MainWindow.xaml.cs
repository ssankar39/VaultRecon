using BetterFileSys.ViewModels;
using System;
using System.IO;
using System.Windows;

namespace BetterFileSys
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SearchViewModel _viewModel;
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "BetterFileSys_Debug.log");

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
                Console.WriteLine(message);
            }
            catch { }
        }

        public MainWindow()
        {
            Log("[WINDOW] MainWindow constructor called");
            InitializeComponent();
            _viewModel = new SearchViewModel();
            this.DataContext = _viewModel;
            Log("[WINDOW] DataContext set to SearchViewModel");
        }

        protected override void OnClosed(System.EventArgs e)
        {
            Log("[WINDOW] Window closing");
            _viewModel?.Cleanup();
            base.OnClosed(e);
        }
    }
}
