using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Halill;

public partial class MainWindow : Window
{
    private readonly LocalStore store = new();
    private readonly GoogleCalendarService google;
    private readonly AppSettings settings;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime selected = DateTime.Today;
    private List<CalendarEvent> events = [];
    private bool busy;
    private bool updatingPicker;
    private CancellationTokenSource? loginCancellation;
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private static Brush Color(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static string PreviewTitle(string title)
    {
        var characters = StringInfo.ParseCombiningCharacters(title);
        return characters.Length <= 6 ? title : title[..characters[6]];
    }

    public MainWindow()
    {
        InitializeComponent();
        try { settings = store.Read<AppSettings>("settings.json") ?? new(); }
        catch { settings = new(); }
        try { google = new(store); }
        catch
        {
            store.Delete("tokens.bin"); store.Delete("client.bin");
            google = new(store);
            StatusText.Text = "저장된 연결 정보를 읽지 못했습니다. 다시 연결해 주세요.";
        }
        Topmost = settings.AlwaysOnTop;
        UpdatePin();
        if (settings.Left is double left && settings.Top is double top && double.IsFinite(left) && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        }
        events = google.IsConnected ? [] : DemoData.ForMonth(month);
        Render();
        Loaded += async (_, _) => await RunAsync(async () =>
        {
            if (google.IsConnected) { await LoadCalendarsAsync(); await LoadEventsAsync(); }
        });
        timer.Tick += async (_, _) => { if (google.IsConnected) await RunAsync(LoadEventsAsync); else Render(); };
        timer.Start();
        Closing += (_, _) =>
        {
            timer.Stop(); lifetime.Cancel();
            settings.Left = Left; settings.Top = Top; settings.AlwaysOnTop = Topmost;
            try { store.Write("settings.json", settings); } catch { /* Do not prevent closing if the settings folder is unavailable. */ }
        };
        Closed += (_, _) => google.Dispose();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        SetControls();
        try { await action(); }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) StatusText.Text = "연결 시간이 초과되었거나 취소되었습니다. 다시 시도해 주세요."; }
        catch (HttpRequestException) { StatusText.Text = "인터넷 연결을 확인해 주세요. 마지막으로 불러온 일정은 유지됩니다."; }
        catch (Exception ex) { StatusText.Text = ex is InvalidOperationException ? ex.Message : "처리하지 못했습니다. 설정 파일과 인터넷 연결을 확인하고 다시 시도해 주세요."; }
        finally { busy = false; SetControls(); }
    }
    private void SetControls()
    {
        ConnectButton.IsEnabled = RefreshButton.IsEnabled = DisconnectButton.IsEnabled = CalendarPicker.IsEnabled = Navigation.IsEnabled = DaysGrid.IsEnabled = !busy;
        ConnectButton.Content = google.IsConnected ? "Google 계정 다시 연결" : "Google 캘린더 연결";
        CancelLoginButton.Visibility = loginCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = google.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        CalendarPicker.Visibility = google.IsConnected ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task LoadCalendarsAsync()
    {
        StatusText.Text = "캘린더 목록을 불러오는 중…";
        var calendars = await google.CalendarsAsync(lifetime.Token);
        if (calendars.Count == 0) calendars.Add(new("primary", "기본 캘린더"));
        updatingPicker = true;
        try
        {
            CalendarPicker.ItemsSource = calendars;
            CalendarPicker.SelectedItem = calendars.FirstOrDefault(x => x.Id == settings.CalendarId) ?? calendars[0];
            settings.CalendarId = ((CalendarChoice)CalendarPicker.SelectedItem).Id;
        }
        finally { updatingPicker = false; }
    }
    private async Task LoadEventsAsync()
    {
        if (!google.IsConnected)
        {
            events = DemoData.ForMonth(month); StatusText.Text = "샘플 일정 · Google 계정을 연결해 보세요"; Render(); return;
        }
        StatusText.Text = "일정을 불러오는 중…";
        var from = month.AddDays(-(int)month.DayOfWeek);
        events = await google.EventsAsync(settings.CalendarId, from, from.AddDays(42), lifetime.Token);
        StatusText.Text = $"Google 연결됨 · {DateTime.Now:HH:mm} 업데이트 · 5분마다 갱신";
        Render();
    }
    private void Render()
    {
        MonthTitle.Text = month.ToString("yyyy년 M월", Korean);
        MonthSubtitle.Text = google.IsConnected ? "GOOGLE CALENDAR  /  PC의 현지 시간" : "MY LITTLE CALENDAR  /  샘플 일정";
        DaysGrid.Children.Clear();
        var first = month.AddDays(-(int)month.DayOfWeek);
        var requiredWeeks = ((int)month.DayOfWeek + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
        for (int i = 0; i < 42; i++)
        {
            if (i >= requiredWeeks * 7)
            {
                // Preserve the six-row layout; unused weeks blend into the window background.
                DaysGrid.Children.Add(new Border { Background = Background, IsHitTestVisible = false });
                continue;
            }
            var date = first.AddDays(i);
            bool chosen = date == selected;
            var dayEvents = events.Where(x => x.OccursOn(date)).OrderByDescending(x => x.AllDay).ThenBy(x => x.Start).ToList();
            var panel = new StackPanel();
            var dateHeader = new Grid { Margin = new Thickness(3, 0, 2, 2) };
            dateHeader.Children.Add(new TextBlock { Text = date.Day.ToString(), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12,
                FontWeight = date == DateTime.Today ? FontWeights.Bold : FontWeights.Normal,
                Foreground = chosen ? Brushes.White : date.Month != month.Month ? Color("#B8C0B4") : date.DayOfWeek == DayOfWeek.Sunday ? Color("#BA705F") : Color("#304535") });
            if (dayEvents.Count > 3) dateHeader.Children.Add(new TextBlock {
                Text = $"+{dayEvents.Count - 3}개", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Foreground = chosen ? Color("#D2E5C8") : Color("#62735F") });
            panel.Children.Add(dateHeader);
            foreach (var entry in dayEvents.Take(3))
            {
                panel.Children.Add(new Border {
                    Background = entry.AllDay ? Color("#DCE8CF") : Color("#E0EAF2"),
                    BorderBrush = entry.AllDay ? Color("#82A06A") : Color("#7697B2"),
                    BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 0, 2, 0), Margin = new Thickness(0, 0, 0, 1), Height = 16,
                    ToolTip = $"{entry.TimeLabel}\n{entry.Title}" + (entry.Location.Length > 0 ? $"\n{entry.Location}" : ""),
                    Child = new TextBlock { Text = PreviewTitle(entry.Title), FontSize = 10, Foreground = Color("#304535"),
                        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }
                });
            }
            var button = new Button { Content = panel, Tag = date, Margin = new Thickness(1), Padding = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Background = chosen ? Color("#2E5544") : date == DateTime.Today ? Color("#E4EBDE") : date.Month == month.Month ? Color("#EFF2EB") : Color("#F3F5F0") };
            button.Click += SelectDate;
            DaysGrid.Children.Add(button);
        }
        SelectedDayTitle.Text = selected.ToString("M월 d일 dddd", Korean) + (selected == DateTime.Today ? " · 오늘" : "");
        var daily = events.Where(x => x.OccursOn(selected)).OrderByDescending(x => x.AllDay).ThenBy(x => x.Start).ToList();
        EventCount.Text = $"{daily.Count}개의 일정";
        AgendaPanel.Children.Clear();
        if (daily.Count == 0)
        {
            AgendaPanel.Children.Add(new TextBlock { Text = "예정된 일정이 없어요.\n여유로운 하루를 만들어 보세요.", Foreground = Color("#899383"), FontSize = 12, LineHeight = 23, Margin = new Thickness(3, 14, 0, 12) });
        }
        foreach (var entry in daily)
        {
            var body = new StackPanel();
            string time = entry.TimeLabel;
            if (!entry.AllDay && entry.Start.Date != entry.End.Date) time = $"{entry.Start:M/d HH:mm} – {entry.End:M/d HH:mm}";
            body.Children.Add(new TextBlock { Text = time, FontSize = 11, Foreground = Color("#7F8C76"), Margin = new Thickness(0, 0, 0, 3) });
            body.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (entry.Location.Length > 0) body.Children.Add(new TextBlock { Text = entry.Location, FontSize = 11, Foreground = Color("#7F8C76"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            AgendaPanel.Children.Add(new Border { Child = body, Height = 72, ToolTip = $"{time}\n{entry.Title}\n{entry.Location}", Background = Color("#F0F3EB"), BorderBrush = Color("#92A980"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(3) });
        }
    }
    private void SelectDate(object sender, RoutedEventArgs e) { selected = (DateTime)((Button)sender).Tag; Render(); }
    private async Task ChangeMonthAsync(DateTime target)
    {
        await RunAsync(async () =>
        {
            month = new(target.Year, target.Month, 1); selected = target;
            events = []; Render(); await LoadEventsAsync();
        });
    }
    private async void PreviousMonth(object sender, RoutedEventArgs e) => await ChangeMonthAsync(month.AddMonths(-1));
    private async void NextMonth(object sender, RoutedEventArgs e) => await ChangeMonthAsync(month.AddMonths(1));
    private async void GoToday(object sender, RoutedEventArgs e) => await ChangeMonthAsync(DateTime.Today);
    private async void RefreshClicked(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (google.IsConnected && CalendarPicker.Items.Count == 0) await LoadCalendarsAsync();
        await LoadEventsAsync();
    });
    private async void CalendarChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingPicker || CalendarPicker.SelectedItem is not CalendarChoice choice) return;
        await RunAsync(async () => { settings.CalendarId = choice.Id; events = []; Render(); store.Write("settings.json", settings); await LoadEventsAsync(); });
    }
    private async void ConnectGoogle(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            loginCancellation = cancellation;
            SetControls();
            try
            {
                StatusText.Text = "브라우저에서 Google 로그인을 완료해 주세요 (최대 3분)…";
                await google.ConnectAsync(cancellation.Token);
            }
            finally { loginCancellation = null; SetControls(); }
            Activate();
            events = []; Render();
            await LoadCalendarsAsync(); await LoadEventsAsync();
        });
    }
    private void CancelLogin(object sender, RoutedEventArgs e) => loginCancellation?.Cancel();
    private void DisconnectGoogle(object sender, RoutedEventArgs e)
    {
        try
        {
            google.Disconnect(); settings.CalendarId = "primary";
            updatingPicker = true; CalendarPicker.ItemsSource = null; updatingPicker = false;
            events = DemoData.ForMonth(month); StatusText.Text = "이 PC의 연결 정보 삭제됨 · 현재 샘플 일정 표시 중";
            SetControls(); Render();
        }
        catch { StatusText.Text = "연결 정보를 삭제하지 못했습니다. 앱을 다시 열고 시도해 주세요."; }
    }
    private void UpdatePin() { PinButton.Content = Topmost ? "고정됨" : "고정"; PinButton.Background = Color(Topmost ? "#D6E3CD" : "#EDF1EA"); }
    private void TogglePin(object sender, RoutedEventArgs e) { Topmost = !Topmost; UpdatePin(); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
