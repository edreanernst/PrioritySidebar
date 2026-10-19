using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace PrioritySidebar;

public partial class SettingsWindow : Window
{
    private sealed record Choice(string Value, string Name);

    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x1E, 0x7B, 0x34));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xB0, 0x2A, 0x1F));

    private readonly AppSettings _current;
    private TrelloClient? _client;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _current = current;
        Result = current;

        KeyBox.Text = current.ApiKey;
        TokenBox.Password = current.Token;

        var displays = Monitors.All().Select((m, i) => new Choice(m.DeviceName, Monitors.Describe(m, i))).ToList();
        DisplayCombo.ItemsSource = displays;
        DisplayCombo.SelectedItem = displays.FirstOrDefault(d => d.Value == current.MonitorDevice)
            ?? displays.FirstOrDefault(d => d.Value == Monitors.Find(null).DeviceName);

        WidthSlider.Value = current.Width;

        var intervals = new List<Choice> { new("30", "30 seconds"), new("60", "1 minute"), new("300", "5 minutes") };
        PollCombo.ItemsSource = intervals;
        PollCombo.SelectedItem = intervals.FirstOrDefault(c => c.Value == current.PollSeconds.ToString()) ?? intervals[0];

        StartupCheck.IsChecked = current.IsNew || WindowsStartup.IsEnabled;

        Loaded += async (_, _) =>
        {
            if (KeyBox.Text.Length > 0 && TokenBox.Password.Length > 0) await ConnectAsync();
        };
        Closed += (_, _) => _client?.Dispose();
    }

    public AppSettings Result { get; private set; }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        var key = KeyBox.Text.Trim();
        var token = TokenBox.Password.Trim();
        if (key.Length == 0 || token.Length == 0)
        {
            SetStatus("Paste your API key and token first.", Bad);
            return;
        }

        SetStatus("Connecting...", Brushes.Black);
        _client?.Dispose();
        _client = new TrelloClient(key, token);
        try
        {
            var me = await _client.GetMe();
            SetStatus($"Connected as {me.FullName ?? me.Username}", Good);

            var boards = await _client.GetBoards();
            BoardCombo.ItemsSource = boards;
            BoardCombo.SelectedItem = boards.FirstOrDefault(b => b.Id == _current.BoardId);
        }
        catch (TrelloException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            SetStatus("Trello didn't accept that key and token.", Bad);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetStatus("Couldn't reach Trello. Check your internet connection.", Bad);
        }
    }

    private async void BoardCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ListCombo.ItemsSource = null;
        if (_client is null || BoardCombo.SelectedItem is not TrelloNamed board) return;
        try
        {
            var lists = await _client.GetLists(board.Id);
            if (!ReferenceEquals(BoardCombo.SelectedItem, board)) return;
            ListCombo.ItemsSource = lists;
            ListCombo.SelectedItem = lists.FirstOrDefault(l => l.Id == _current.ListId);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetStatus("Couldn't load the lists on that board.", Bad);
        }
    }

    private void GetToken_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Text.Trim();
        if (key.Length == 0)
        {
            SetStatus("Paste your API key first, then get a token.", Bad);
            return;
        }
        Open("https://trello.com/1/authorize?expiration=never&scope=read,write&response_type=token" +
             $"&name=Priority%20Sidebar&key={Uri.EscapeDataString(key)}");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var board = BoardCombo.SelectedItem as TrelloNamed;
        var list = ListCombo.SelectedItem as TrelloNamed;
        if (board is not null && list is null)
        {
            SetStatus("Pick which list to show.", Bad);
            return;
        }

        Result = new AppSettings
        {
            ApiKey = KeyBox.Text.Trim(),
            Token = TokenBox.Password.Trim(),
            // Without a connection the board and list boxes stay empty; keep what was saved before.
            BoardId = board?.Id ?? _current.BoardId,
            ListId = list?.Id ?? _current.ListId,
            ListName = list?.Name ?? _current.ListName,
            MonitorDevice = (DisplayCombo.SelectedItem as Choice)?.Value,
            Width = WidthSlider.Value,
            PollSeconds = int.Parse(((Choice)PollCombo.SelectedItem).Value),
        };

        try
        {
            WindowsStartup.Set(StartupCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        DialogResult = true;
    }

    private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        WidthText.Text = $"{e.NewValue:0} px";

    private void CloseSidebar_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void SetStatus(string text, Brush color)
    {
        ConnectStatus.Text = text;
        ConnectStatus.Foreground = color;
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
