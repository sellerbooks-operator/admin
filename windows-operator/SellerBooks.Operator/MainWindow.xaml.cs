using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SellerBooks.Operator;

public partial class MainWindow : Window
{
    private const string AppUrl = "https://sellerbooks-operator.github.io/admin/";
    private readonly string _userDataFolder;
    private readonly DispatcherTimer _networkTimer;
    private bool _offline;
    private TcpListener? _oauthListener;
    private CancellationTokenSource? _oauthCts;
    private int _oauthPort;

    private const string OAuthCallbackPath = "/oauth-callback";

    public MainWindow()
    {
        InitializeComponent();
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SellerBooks", "Operator", "WebView2");

        _networkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkTimer.Tick += NetworkTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_userDataFolder);
            string? version = null;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }

            if (string.IsNullOrWhiteSpace(version))
            {
                ShowStartupError("Microsoft Edge WebView2 Runtime belum tersedia di komputer ini.\n\nSilakan instal WebView2 Runtime, lalu buka SellerBooks Operator kembali.");
                return;
            }

            _oauthPort = StartOAuthListener();
            var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder, null);
            await Browser.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(Browser.CoreWebView2);
            Browser.CoreWebView2.Navigate(AppUrl + "?desktop=1&oauth_port=" + _oauthPort);
        }
        catch (Exception ex)
        {
            ShowStartupError("SellerBooks Operator tidak dapat dijalankan.\n\nDetail: " + ex.Message);
        }
    }

    private void ShowStartupError(string message)
    {
        Browser.Visibility = Visibility.Collapsed;
        OfflinePanel.Visibility = Visibility.Visible;
        MessageBox.Show(this, message, "SellerBooks Operator", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ConfigureBrowser(CoreWebView2 web)
    {
        web.Settings.AreDefaultContextMenusEnabled = true;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.AreBrowserAcceleratorKeysEnabled = true;

        // Tandai WebView sebagai aplikasi Windows SellerBooks Operator.
        // index.html memakai marker ini agar OAuth Google tetap berada
        // di WebView yang sama, seperti mekanisme SellerBooks Admin.
        web.Settings.UserAgent = web.Settings.UserAgent + " SellerBooksOperator/1.0";

        web.AddWebResourceRequestedFilter("https://sellerbooks-operator.github.io/*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += Web_WebResourceRequested;
        web.NavigationCompleted += Web_NavigationCompleted;
        web.NewWindowRequested += Web_NewWindowRequested;
        web.DownloadStarting += Web_DownloadStarting;
        web.ProcessFailed += Web_ProcessFailed;
    }

    private void Web_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        => e.Request.Headers.SetHeader("Cache-Control", "no-cache");

    private void Web_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) ShowBrowser(); else ShowOffline();
    }

    private void Web_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri)) Browser.CoreWebView2.Navigate(e.Uri);
    }

    private void Web_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        => Dispatcher.Invoke(() => ShowStartupError("WebView2 mengalami kegagalan proses. Silakan tutup SellerBooks Operator dan buka kembali."));

    private void Web_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var suggestedName = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = "SellerBooks";
            var dialog = new SaveFileDialog
            {
                Title = "Simpan File SellerBooks",
                FileName = suggestedName,
                Filter = BuildFilter(suggestedName),
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                e.ResultFilePath = dialog.FileName;
                e.Handled = true;
            }
            else { e.Cancel = true; e.Handled = true; }
        });
    }

    private static string BuildFilter(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            ".xls" => "Excel Workbook (*.xls)|*.xls|All files (*.*)|*.*",
            ".csv" => "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            ".json" => "JSON Backup (*.json)|*.json|All files (*.*)|*.*",
            ".pdf" => "PDF (*.pdf)|*.pdf|All files (*.*)|*.*",
            _ => "All files (*.*)|*.*"
        };
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => TryNavigate();

    private void TryNavigate()
    {
        if (Browser.CoreWebView2 is null) return;
        ShowLoading();
        try { Browser.CoreWebView2.Navigate(AppUrl + "?desktop=1&oauth_port=" + _oauthPort); } catch { ShowOffline(); }
    }

    private void ShowLoading()
    {
        OfflinePanel.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private void ShowBrowser()
    {
        _offline = false;
        _networkTimer.Stop();
        OfflinePanel.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private void ShowOffline()
    {
        _offline = true;
        Browser.Visibility = Visibility.Collapsed;
        OfflinePanel.Visibility = Visibility.Visible;
        _networkTimer.Start();
    }

    private void NetworkTimer_Tick(object? sender, EventArgs e)
    {
        if (!_offline || !NetworkInterface.GetIsNetworkAvailable()) return;
        TryNavigate();
    }

    private int StartOAuthListener()
    {
        _oauthCts = new CancellationTokenSource();
        _oauthListener = new TcpListener(IPAddress.Loopback, 0);
        _oauthListener.Start();
        var port = ((IPEndPoint)_oauthListener.LocalEndpoint).Port;
        _ = Task.Run(() => OAuthListenerLoopAsync(_oauthCts.Token));
        return port;
    }

    private async Task OAuthListenerLoopAsync(CancellationToken token)
    {
        if (_oauthListener is null) return;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _oauthListener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleOAuthClientAsync(client), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(this,
                    "OAuth callback listener gagal.\\n\\n" + ex.Message,
                    "SellerBooks Operator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
        }
    }

    private async Task HandleOAuthClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, 0, buffer.Length);
            var request = Encoding.UTF8.GetString(buffer, 0, read);

            var firstLine = request.Split(new[] { "\\r\\n" }, StringSplitOptions.None).FirstOrDefault() ?? "";
            var parts = firstLine.Split(' ');
            var target = parts.Length >= 2 ? parts[1] : "/";
            var uri = new Uri("http://127.0.0.1" + target);

            var query = ParseQuery(uri.Query);
            var code = query.TryGetValue("code", out var c) ? c : null;
            var error = query.TryGetValue("error", out var err) ? err : null;
            var description = query.TryGetValue("error_description", out var desc) ? desc : null;

            const string html = "<!doctype html><html><head><meta charset='utf-8'><title>SellerBooks Operator</title></head><body style='font-family:Arial;text-align:center;padding:60px'><h2>Login SellerBooks selesai.</h2><p>Silakan kembali ke jendela SellerBooks Operator.</p><script>window.close();</script></body></html>";
            var body = Encoding.UTF8.GetBytes(html);
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\\r\\n" +
                "Content-Type: text/html; charset=utf-8\\r\\n" +
                "Content-Length: " + body.Length + "\\r\\n" +
                "Connection: close\\r\\n\\r\\n");
            await stream.WriteAsync(response, 0, response.Length);
            await stream.WriteAsync(body, 0, body.Length);

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(this,
                        "Login Google gagal.\\n\\n" + (description ?? error),
                        "SellerBooks Operator",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(code))
                {
                    Browser.CoreWebView2?.Navigate(
                        AppUrl + "?desktop=1&oauth_port=" + _oauthPort +
                        "&code=" + Uri.EscapeDataString(code));
                }
            });
        }
    }

    private static Dictionary<string,string> ParseQuery(string query)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var raw = query.StartsWith("?") ? query[1..] : query;
        foreach (var item in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
            var value = pair.Length > 1
                ? Uri.UnescapeDataString(pair[1].Replace("+", " "))
                : "";
            result[key] = value;
        }
        return result;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _networkTimer.Stop();
        try { _oauthCts?.Cancel(); } catch { }
        try { _oauthListener?.Stop(); } catch { }
    }
}