using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using WindowScreenTime.Services;

namespace WindowScreenTime.Views;

public partial class AboutWindow : Window
{
    private readonly string _dataDirectory;

    public AboutWindow(string dataDirectory)
    {
        InitializeComponent();

        _dataDirectory = dataDirectory;
        AppIconImage.Source = LoadLargestIconFrame();
        VersionText.Text = $"Sürüm {UpdateService.GetCurrentVersionString()}";
        RepoLink.NavigateUri = new Uri($"https://github.com/{UpdateService.RepoOwner}/{UpdateService.RepoName}");
        RepoLinkText.Text = $"github.com/{UpdateService.RepoOwner}/{UpdateService.RepoName}";
    }

    /// <summary>
    /// .ico dosyasındaki en büyük çözünürlüklü kareyi seçer (WPF varsayılan olarak küçük kareyi
    /// büyütüp bulanık gösterir).
    /// </summary>
    private static ImageSource? LoadLargestIconFrame()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/WindowScreenTime;component/Assets/app.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info == null) return null;
            using var stream = info.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RepoLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Yoksay
        }
        e.Handled = true;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Kontrol Ediliyor...";

        try
        {
            await UpdateService.CheckForUpdatesAsync(isManualCheck: true);
        }
        finally
        {
            CheckUpdateButton.Content = "Güncellemeleri Kontrol Et";
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            Process.Start(new ProcessStartInfo(_dataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Veri klasörü açılamadı:\n{_dataDirectory}\n\n{ex.Message}",
                "WindowScreenTime",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
