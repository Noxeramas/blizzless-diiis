using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace D3Offline.CombatMeter;

public partial class MeterWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    private readonly MeterHistory _history = new();
    private readonly string[] _args;
    private HwndSource? _source;
    private bool _rendering;
    private string _connection = "Connecting to local server…";
    private List<CombatHit> _selection = new();
    private long? _selectedSequence;
    private const int HotkeyId = 0xD308;
    private const int HotkeyMessage = 0x0312;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    public MeterWindow(string[] args)
    {
        _args = args;
        InitializeComponent();
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 60;
        MaxHeight = SystemParameters.WorkArea.Height - 30;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source.AddHook(WindowMessage);
            if (!RegisterHotKey(_source.Handle, HotkeyId, 0x4000, 0x77))
            {
                MessageBox.Show("F8 is already registered by another application. Close that application and restart the combat meter.", "Combat meter hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }
            if (!_args.Contains("--smoke-test") || !_args.Contains("--fixture")) _ = Receive();
            if (_args.Contains("--smoke-test")) _ = SmokeTest();
        };
        Closed += (_, _) =>
        {
            _stop.Cancel();
            if (_source != null) { UnregisterHotKey(_source.Handle, HotkeyId); _source.RemoveHook(WindowMessage); }
        };
    }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam.ToInt32() == HotkeyId)
        {
            if (IsVisible) Hide(); else Show();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async Task Receive()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", MeterProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(1000, _stop.Token);
                _history.Reset(); // An unobserved disconnect gap must not silently enter encounter totals.
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                while (!_stop.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await writer.WriteAsync("?".AsMemory(), timeout.Token);
                    var json = await reader.ReadLineAsync(timeout.Token);
                    if (json == null) throw new EndOfStreamException("Server disconnected");
                    var frame = JsonSerializer.Deserialize<MeterFrame>(json) ?? throw new InvalidDataException("Empty combat frame");
                    _history.Accept(frame);
                    _connection = "Connected · server damage trace";
                    Render();
                    await Task.Delay(250, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { _connection = "Server stopped responding · reconnecting"; }
            catch (TimeoutException) { _connection = "Server unavailable · reconnecting"; }
            catch (IOException ex) { _connection = "Disconnected · " + ex.Message; }
            catch (UnauthorizedAccessException) { _connection = "Connection denied · server and meter need the same Windows user and elevation; normally launch both without Run as administrator"; }
            catch (JsonException) { _connection = "Invalid combat frame · reconnecting"; }
            Render();
            try { await Task.Delay(1000, _stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private string PlayerId => PlayerPicker?.SelectedValue as string ?? "";
    private string Direction => DirectionPicker?.SelectedIndex == 1 ? "Incoming" : "Outgoing";
    private static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Compact(double value) => value >= 1e9 ? $"{value / 1e9:0.00} B" : value >= 1e6 ? $"{value / 1e6:0.00} M" : value >= 1e3 ? $"{value / 1e3:0.0} K" : $"{value:0}";
    private sealed record Row(string Name, string Value = "", string Detail = "");

    private void Render()
    {
        if (!IsInitialized || _rendering) return;
        _rendering = true;
        try
        {
            ConnectionText.Text = _connection + (_history.LostHits > 0 ? $" · {_history.LostHits} lost hits: totals incomplete" : "");
            var id = PlayerId;
            var players = _history.Players;
            if (!players.Select(p => p.Id).SequenceEqual(PlayerPicker.Items.Cast<CombatPlayer>().Select(p => p.Id)))
            {
                PlayerPicker.ItemsSource = players;
                PlayerPicker.SelectedValue = players.Any(p => p.Id == id) ? id : players.FirstOrDefault()?.Id;
            }
            var player = players.FirstOrDefault(p => p.Id == PlayerId);
            var stats = _history.Stats(PlayerId, Direction);
            DpsText.Text = Compact(stats.Rolling);
            AverageText.Text = $"{Compact(stats.Average)} encounter average";
            TotalText.Text = Compact(stats.Total);
            CountText.Text = $"{stats.Count:N0} hits · includes idle time";
            BuffTitle.Text = "Buffs and effects" + (player?.Paused == true ? " · game paused" : "");
            BuffRows.ItemsSource = player?.Buffs.Select(b => new Row(b.Name,
                b.State + (b.IsStacking ? $" · {b.Stacks} stacks" : "") + (b.SecondsLeft.HasValue ? $" · {b.SecondsLeft:0.0}s" : ""), b.Detail)).ToList();
            ShieldText.Text = player == null ? "Enter a game to see buffs and damage." : $"Deflection shield capacity · {Compact(player.Shield)} · other shields are listed per effect";
            _selection = _history.Hits.Where(h => h.PlayerId == PlayerId && h.Direction == Direction).ToList();
            HitSlider.Maximum = Math.Max(0, _selection.Count - 1);
            CombatHit? hit = null;
            if (FollowLatest.IsChecked == true) hit = _selection.LastOrDefault();
            else if (_selectedSequence.HasValue) hit = _selection.FirstOrDefault(h => h.Sequence == _selectedSequence);
            if (hit == null && FollowLatest.IsChecked != true) HitMeta.Text = "Selected hit expired from the last 1,000 retained hits. Resume follow to inspect new hits.";
            if (hit != null) { HitSlider.Value = _selection.IndexOf(hit); RenderHit(hit); }
            else
            {
                HitTitle.Text = "Waiting for combat…";
                FactorTotal.Text = "";
                FactorRows.ItemsSource = null; StepRows.ItemsSource = null; BucketRows.ItemsSource = null;
                ReferenceText.Text = "";
            }
            DrawChart();
        }
        finally { _rendering = false; }
    }

    private void RenderHit(CombatHit hit)
    {
        HitTitle.Text = $"{hit.Skill} · {Compact(hit.HealthRemoved)} dealt";
        var factors = hit.Factors.Where(f => f.Kind == "Multiplier").ToList();
        var product = factors.Aggregate(1d, (total, f) => total * f.Multiplier);
        FactorTotal.Text = $"Recorded bonus factors · {product:0.###}× (after weapon roll)";
        FactorRows.ItemsSource = factors.Select(f => new Row(f.Name, $"{f.Multiplier:0.###}×",
            f.Multiplier == 1 ? "No bonus applied to this hit" : f.Source)).ToList();
        HitMeta.Text = $"Hit {hit.Sequence} · {hit.Element} · {(hit.Critical ? "Critical" : "Normal")} · {(hit.ItemProc ? "Item proc" : "Direct / skill")} · {hit.Outcome}\nTarget {hit.Target}";
        var steps = new List<Row> { new("Rolled damage / skill coefficient", Number(hit.BaseDamage)) };
        steps.AddRange(hit.Factors.Select(f => new Row(f.Name + $" ({f.Multiplier:0.###}×)", Number(f.After))));
        steps.Add(new("Final damage before overkill", Number(hit.FinalDamage)));
        steps.Add(new("Overkill excluded", Number(hit.Overkill)));
        steps.Add(new("Actual HP removed", Number(hit.HealthRemoved)));
        StepRows.ItemsSource = steps;
        var covered = factors.Where(f => f.ReferenceBucket != MeterProtocol.Unverified).ToList();
        var actual = covered.Aggregate(1d, (total, f) => total * f.Multiplier);
        var reference = MeterProtocol.ReferenceProduct(covered);
        var difference = reference == 0 ? 0 : actual / reference - 1;
        ReferenceText.Text = $"Classified factors: server {actual:0.###}× · reference {reference:0.###}×" +
            (covered.Count == 0 ? "\nNo classified factors in this hit" : Math.Abs(difference) > .0001 ? $"\nBucket mismatch: {difference:+0.##%;-0.##%} against published grouping" : "\nCaptured factors agree with published grouping") +
            $"\n{hit.Factors.Count(f => f.ReferenceBucket == MeterProtocol.Unverified && (f.Kind == "Multiplier" || f.Kind == "Adjustment"))} unclassified adjustments · full live parity unverified";
        BucketRows.ItemsSource = covered.GroupBy(f => f.ReferenceBucket).Select(g => new Row(g.Key, "",
            string.Join("; ", g.Select(f => f.Name + (g.Key == MeterProtocol.Independent ? $" ×{f.Multiplier:0.###}" : $" {f.Multiplier - 1:+0.##%;-0.##%;0%}"))))).ToList();
    }

    private void DrawChart()
    {
        if (ChartCanvas == null || ChartCanvas.ActualWidth < 20) return;
        ChartCanvas.Children.Clear();
        var values = _history.Chart(PlayerId, Direction);
        var ceiling = Math.Max(1, values.Max());
        var width = ChartCanvas.ActualWidth;
        var points = new PointCollection(values.Select((v, i) => new Point(i * (width - 1) / 59, 78 - v / ceiling * 57)));
        ChartCanvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = 79, Y2 = 79, Stroke = new SolidColorBrush(Color.FromRgb(65, 70, 80)) });
        ChartCanvas.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(Color.FromRgb(222, 182, 111)), StrokeThickness = 2 });
        var label = new TextBlock { Text = $"{Compact(ceiling)} DPS · 0.5 s bins", Foreground = Brushes.LightGray, FontSize = 11 };
        ChartCanvas.Children.Add(label);
        var start = new TextBlock { Text = "−30 s", Foreground = Brushes.LightGray, FontSize = 11 };
        var end = new TextBlock { Text = "now", Foreground = Brushes.LightGray, FontSize = 11 };
        Canvas.SetTop(start, 84); ChartCanvas.Children.Add(start);
        Canvas.SetTop(end, 84); Canvas.SetRight(end, 0); ChartCanvas.Children.Add(end);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void HideMeter(object sender, RoutedEventArgs e) => Hide();
    private void CloseMeter(object sender, RoutedEventArgs e) => Close();
    private void ResetEncounter(object sender, RoutedEventArgs e) { _history.Reset(); _selectedSequence = null; Render(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsInitialized) { _selectedSequence = null; Render(); } }
    private void ChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();
    private void ExpandDetails(object sender, RoutedEventArgs e) { Height = Math.Min(900, MaxHeight); }
    private void FollowChanged(object sender, RoutedEventArgs e)
    {
        if (_rendering || !IsInitialized) return;
        if (FollowLatest.IsChecked != true) _selectedSequence = _selection.LastOrDefault()?.Sequence;
        Render();
    }
    private void HitChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_rendering || _selection.Count == 0) return;
        var selected = _selection[Math.Clamp((int)e.NewValue, 0, _selection.Count - 1)].Sequence;
        _rendering = true;
        FollowLatest.IsChecked = false;
        _rendering = false;
        _selectedSequence = selected;
        Render();
    }

    private async Task SmokeTest()
    {
        // Exercises the real window and registered hotkey handler; no mock combat data is inserted.
        var fixture = _args.SkipWhile(a => a != "--fixture").Skip(1).FirstOrDefault();
        if (fixture != null)
        {
            var frame = JsonSerializer.Deserialize<MeterFrame>(File.ReadAllText(fixture))!;
            _history.Accept(frame);
            _connection = "UI test · recorded regression fixture";
            Render();
            if (frame.Hits.Count > 0 && !HitTitle.Text.Contains(frame.Hits.Last().Skill)) throw new InvalidOperationException("Fixture hit did not render");
        }
        await Task.Delay(1500);
        var handled = false;
        WindowMessage(_source!.Handle, HotkeyMessage, (IntPtr)HotkeyId, IntPtr.Zero, ref handled);
        if (IsVisible || !handled) throw new InvalidOperationException("Hotkey did not hide the overlay");
        WindowMessage(_source.Handle, HotkeyMessage, (IntPtr)HotkeyId, IntPtr.Zero, ref handled);
        if (!IsVisible) throw new InvalidOperationException("Hotkey did not restore the overlay");
        Details.IsExpanded = true;
        UpdateLayout();
        var path = _args.SkipWhile(a => a != "--smoke-test").Skip(1).FirstOrDefault();
        if (path != null)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            File.WriteAllText(path + ".txt", "PASS F8 registered; hide/show handler; expanded window rendered. " + _connection);
        }
        Close();
    }
}
