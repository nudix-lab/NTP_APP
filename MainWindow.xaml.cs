using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NtpTimer;

/// <summary>
/// 검정 배경 NTP 동기화 ms 타이머.
/// 시작 시 NTP 1회 질의로 "참 UTC ↔ Stopwatch" 앵커를 구하고,
/// 이후 표시는 Stopwatch(고해상도)로만 갱신한다.
/// </summary>
public partial class MainWindow : Window
{
    // Google + 보조 NTP 서버. 첫 성공을 사용.
    private static readonly string[] Servers =
    {
        "time.google.com",
        "time.cloudflare.com",
        "pool.ntp.org",
        "time.windows.com",
    };

    private readonly DispatcherTimer _timer;
    private readonly double _ticksPerSecond = Stopwatch.Frequency;

    private bool _synced;
    private DateTime _anchorUtc;     // 앵커 순간의 참 UTC
    private long _anchorTicks;       // 그 순간의 Stopwatch 타임스탬프
    private bool _showUtc;

    public MainWindow()
    {
        InitializeComponent();

        // 약 60fps 로 표시 갱신(ms 자릿수가 부드럽게 흐름).
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += (_, _) => UpdateDisplay();
        _timer.Start();

        Loaded += async (_, _) => await SyncAsync();
    }

    /// <summary>현재 참 UTC 추정값 = 앵커UTC + 경과 Stopwatch 시간.</summary>
    private DateTime NowUtc()
    {
        if (!_synced) return DateTime.UtcNow; // 동기화 전엔 로컬시계로 대체
        long elapsed = Stopwatch.GetTimestamp() - _anchorTicks;
        return _anchorUtc.AddSeconds(elapsed / _ticksPerSecond);
    }

    private void UpdateDisplay()
    {
        DateTime now = _showUtc ? NowUtc() : NowUtc().ToLocalTime();

        DateText.Text = now.ToString("yyyy.MM.dd (ddd)");
        TimeText.Text = now.ToString("HH:mm:ss");
        MsText.Text = "." + now.ToString("fff");
    }

    private async Task SyncAsync()
    {
        StatusText.Text = "NTP 동기화 중...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        ResyncButton.IsEnabled = false;

        try
        {
            var r = await NtpClient.QueryAsync(Servers);
            _anchorUtc = r.AnchorUtc;
            _anchorTicks = r.AnchorTicks;
            _synced = true;

            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            StatusText.Text =
                $"동기 완료 · {r.Server} · 오프셋 {r.Offset.TotalMilliseconds:+0.0;-0.0}ms · " +
                $"왕복 {r.RoundTrip.TotalMilliseconds:0.0}ms · {DateTime.Now:HH:mm:ss} 기준";
        }
        catch (Exception ex)
        {
            _synced = false;
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            StatusText.Text = $"NTP 실패(로컬시계 표시 중): {ex.Message}";
        }
        finally
        {
            ResyncButton.IsEnabled = true;
        }
    }

    private async void Resync_Click(object sender, RoutedEventArgs e) => await SyncAsync();

    private void Utc_Changed(object sender, RoutedEventArgs e)
    {
        _showUtc = UtcCheck.IsChecked == true;
        UpdateDisplay();
    }
}
