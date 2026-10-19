using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PrioritySidebar;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CardViewModel> _cards = [];
    private readonly AppBar _appBar;
    private readonly DispatcherTimer _pollTimer = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private AppSettings _settings;
    private TrelloClient? _client;
    private string? _doneLabelId;
    private bool _online = true;
    private int _pendingWrites;
    private DateTime? _lastSync;
    private string? _problem;
    private Action? _undo;

    private CardViewModel? _pressedCard;
    private Point _pressPoint;
    private bool _dragging;
    private int _dropIndex = -1;

    // Only one card is open at a time, so the list stays easy to scan.
    private CardViewModel? _openCard;

    public MainWindow()
    {
        InitializeComponent();
        CardList.ItemsSource = _cards;
        _settings = AppSettings.Load();

        _appBar = new AppBar(this) { MonitorDevice = _settings.MonitorDevice, WidthDip = _settings.Width };
        _appBar.FullScreenChanged += fullScreen => Topmost = !fullScreen;

        SourceInitialized += (_, _) => _appBar.Attach();
        ContentRendered += OnFirstShown;
        Loaded += OnLoaded;
        Closed += (_, _) => _appBar.Remove();
        // Give the strip back even if something crashes outside the UI thread.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _appBar.Remove();

        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _toastTimer.Tick += (_, _) => HideToast();
        AddBox.TextChanged += (_, _) =>
            AddPlaceholder.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _cards.CollectionChanged += (_, _) => Renumber();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowCards(Storage.LoadCache(), highlightRisers: false);
        await ApplySettingsAsync();
    }

    private void OnFirstShown(object? sender, EventArgs e)
    {
        // WPF applies its own first placement after SourceInitialized; move into the strip once it has.
        _appBar.Redock();
        // Opened here rather than in Loaded, which runs before the sidebar is visible.
        if (!_settings.IsComplete) OpenSettings();
    }

    // ---------- Settings ----------

    private async Task ApplySettingsAsync()
    {
        _client?.Dispose();
        _client = null;
        _doneLabelId = null;
        _problem = null;

        _appBar.MonitorDevice = _settings.MonitorDevice;
        _appBar.WidthDip = _settings.Width;
        _appBar.Redock();

        ListTitle.Text = string.IsNullOrEmpty(_settings.ListName) ? "Priorities" : _settings.ListName;
        SetupPanel.Visibility = _settings.IsComplete ? Visibility.Collapsed : Visibility.Visible;

        if (!_settings.IsComplete)
        {
            _pollTimer.Stop();
            _cards.Clear();
            UpdateStatus();
            return;
        }

        _client = new TrelloClient(_settings.ApiKey, _settings.Token);
        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, _settings.PollSeconds));
        _pollTimer.Start();
        await RefreshAsync(force: true);
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            var listChanged = window.Result.ListId != _settings.ListId;
            _settings = window.Result;
            _settings.Save();
            if (listChanged)
            {
                _cards.Clear();
                Storage.SaveCache([]);
            }
            _ = ApplySettingsAsync();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Loading from Trello ----------

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(force: true);

    private async Task RefreshAsync(bool force = false)
    {
        if (_client is null || _settings.ListId is null) return;
        // Never pull underneath a drag or an unsaved change; the next tick catches up.
        if (_dragging || _pendingWrites > 0) return;
        if (force) StatusText.Text = "Checking Trello...";

        try
        {
            var cards = await _client.GetCards(_settings.ListId);
            if (_dragging || _pendingWrites > 0) return;
            _online = true;
            _problem = null;
            _lastSync = DateTime.Now;
            ShowCards(cards, highlightRisers: true);
            Storage.SaveCache(cards);
        }
        catch (Exception ex)
        {
            NoteFailure(ex);
        }
        UpdateStatus();
    }

    /// <summary>Shows Trello's list, reusing existing cards so nothing flickers when little changed.</summary>
    private void ShowCards(List<TrelloCard> fresh, bool highlightRisers)
    {
        var oldRanks = _cards.ToDictionary(c => c.Id, c => c.Rank);
        var sameOrder = fresh.Select(c => c.Id).SequenceEqual(_cards.Select(c => c.Id));
        var byId = _cards.ToDictionary(c => c.Id);
        var openList = _openCard is { ShowChecklist: true } ? _openCard : null;
        var countsBefore = openList is null ? default : (openList.CheckDone, openList.CheckTotal);

        var updated = fresh.Select(card =>
            byId.TryGetValue(card.Id, out var existing) ? existing.Update(card) : CardViewModel.From(card)).ToList();

        if (!sameOrder)
        {
            _cards.Clear();
            foreach (var card in updated) _cards.Add(card);
        }
        if (_openCard is not null && !_cards.Contains(_openCard)) _openCard = null;
        UpdateStatus();

        // The open checklist was changed somewhere else, for example on a phone.
        if (openList is { ShowChecklist: true } && _cards.Contains(openList) &&
            (openList.CheckDone, openList.CheckTotal) != countsBefore)
            _ = LoadChecklistAsync(openList);

        if (highlightRisers && !sameOrder && oldRanks.Count > 0)
        {
            foreach (var card in ChangedElsewhere(oldRanks))
                Flash(card);
        }
    }

    /// <summary>
    /// Cards that were added or moved in Trello since the last load. When one card moves, its
    /// neighbours shift too; keeping the longest run that is still in the old order leaves only
    /// the cards that really moved.
    /// </summary>
    private List<CardViewModel> ChangedElsewhere(Dictionary<string, int> oldRanks)
    {
        var known = _cards.Where(c => oldRanks.ContainsKey(c.Id)).ToList();
        var length = new int[known.Count];
        var previous = new int[known.Count];
        var best = -1;
        for (var i = 0; i < known.Count; i++)
        {
            length[i] = 1;
            previous[i] = -1;
            for (var j = 0; j < i; j++)
            {
                if (oldRanks[known[j].Id] < oldRanks[known[i].Id] && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }
            if (best < 0 || length[i] > length[best]) best = i;
        }

        var unmoved = new HashSet<string>();
        for (var i = best; i >= 0; i = previous[i]) unmoved.Add(known[i].Id);
        return _cards.Where(c => !unmoved.Contains(c.Id)).ToList();
    }

    private void NoteFailure(Exception ex)
    {
        App.Log(ex);
        switch (ex)
        {
            case TrelloException { Status: HttpStatusCode.Unauthorized }:
                _problem = "Trello didn't accept the key or token. Check settings.";
                break;
            case TrelloException { Status: HttpStatusCode.NotFound }:
                _problem = "This list can't be found in Trello. Check settings.";
                break;
            case HttpRequestException or TaskCanceledException:
                _online = false;
                break;
            default:
                _problem = "Something went wrong talking to Trello.";
                break;
        }
    }

    private void UpdateStatus()
    {
        EmptyText.Visibility = _settings.IsComplete && _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        string state;
        if (!_settings.IsComplete) state = "Not connected";
        else if (_pendingWrites > 0) state = "Saving to Trello...";
        else if (_problem is not null) state = _problem;
        else if (!_online) state = "Offline, showing the last list";
        else if (_lastSync is { } at) state = $"Synced at {at:HH:mm}";
        else state = "Loading...";

        var count = _cards.Count == 1 ? "1 card" : $"{_cards.Count} cards";
        StatusText.Text = _settings.IsComplete ? $"{count} · {state}" : state;
        StatusText.ToolTip = StatusText.Text;
    }

    // ---------- Saving to Trello ----------

    private bool CanEdit()
    {
        if (_client is null)
        {
            ShowToast("Connect to Trello in settings first.");
            return false;
        }
        if (!_online)
        {
            ShowToast("You're offline. Changes are paused until Trello can be reached again.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Runs one change against Trello. Changes run one at a time so each sees the result of the
    /// previous one. If a change fails, the list is reloaded from Trello so it shows the truth.
    /// </summary>
    private async Task<bool> SaveAsync(Func<TrelloClient, Task> change, CardViewModel? highlight = null)
    {
        if (_client is not { } client) return false;
        _pendingWrites++;
        UpdateStatus();
        await _writeLock.WaitAsync();
        try
        {
            await change(client);
            _lastSync = DateTime.Now;
            if (highlight is not null) Flash(highlight);
            return true;
        }
        catch (Exception ex)
        {
            NoteFailure(ex);
            _reloadNeeded = true;
            ShowToast(_online ? "Couldn't save that to Trello, so the list was reloaded." : "Couldn't reach Trello. Your change was not saved.");
            return false;
        }
        finally
        {
            _writeLock.Release();
            _pendingWrites--;
            UpdateStatus();
            if (_pendingWrites == 0 && _problem is null) _ = ReloadIfFailedAsync();
        }
    }

    private bool _reloadNeeded;

    private async Task ReloadIfFailedAsync()
    {
        if (!_reloadNeeded) return;
        _reloadNeeded = false;
        await RefreshAsync(force: true);
    }

    /// <summary>Trello position for the card where it now sits in the local list.</summary>
    private string PosForCurrentPlace(CardViewModel card)
    {
        var i = _cards.IndexOf(card);
        double? above = i > 0 ? _cards[i - 1].Pos : null;
        double? below = i < _cards.Count - 1 ? _cards[i + 1].Pos : null;
        return Ranking.PosBetween(above, below);
    }

    private async Task MoveAsync(CardViewModel card, int to)
    {
        var from = _cards.IndexOf(card);
        if (from < 0 || to < 0 || to >= _cards.Count || to == from) return;
        if (!CanEdit()) return;

        _cards.Move(from, to);
        await SaveAsync(async client =>
        {
            var saved = await client.MoveCard(card.Id, PosForCurrentPlace(card));
            card.Pos = saved.Pos;
        }, card);
    }

    private async Task DoneAsync(CardViewModel card)
    {
        if (!CanEdit()) return;
        var index = _cards.IndexOf(card);
        _cards.Remove(card);

        var ok = await SaveAsync(async client =>
        {
            _doneLabelId ??= (await client.GetOrCreateDoneLabel(_settings.BoardId!)).Id;
            await client.AddLabel(card.Id, _doneLabelId);
            await client.SetArchived(card.Id, true);
        });

        if (ok) ShowToast($"Done: \"{Shorten(card.Name)}\"", undo: () => _ = UndoDoneAsync(card, index));
    }

    private async Task UndoDoneAsync(CardViewModel card, int index)
    {
        if (!CanEdit()) return;
        _cards.Insert(Math.Min(index, _cards.Count), card);
        await SaveAsync(async client =>
        {
            var saved = await client.SetArchived(card.Id, false, PosForCurrentPlace(card));
            card.Pos = saved.Pos;
            if (_doneLabelId is not null) await client.RemoveLabel(card.Id, _doneLabelId);
        }, card);
    }

    private async Task AddAsync()
    {
        var name = AddBox.Text.Trim();
        if (name.Length == 0 || !CanEdit()) return;
        AddBox.Clear();

        var card = new CardViewModel { Id = "new-" + Guid.NewGuid(), Name = name };
        _cards.Add(card);
        Scroller.ScrollToEnd();

        await SaveAsync(async client =>
        {
            var created = await client.AddCard(_settings.ListId!, name);
            card.Update(created);
            card.Id = created.Id;
        }, card);
    }

    // ---------- Card buttons ----------

    private static CardViewModel CardOf(object sender) => (CardViewModel)((FrameworkElement)sender).DataContext;

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var card = CardOf(sender);
        await MoveAsync(card, _cards.IndexOf(card) - 1);
    }

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var card = CardOf(sender);
        await MoveAsync(card, _cards.IndexOf(card) + 1);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender).Url is { } url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void Done_Click(object sender, RoutedEventArgs e) => await DoneAsync(CardOf(sender));

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddAsync();

    // ---------- Description and checklists ----------

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A drag, or a press on one of the card's buttons, clears _pressedCard, so only a plain click gets here.
        var card = CardOf(sender);
        if (_pressedCard != card) return;
        _pressedCard = null;
        // Clicks in the open description or checklist leave the card open.
        if (IsInside(e.OriginalSource as DependencyObject, el => el is FrameworkElement { Name: "Details" })) return;

        if (card.IsExpanded) Collapse(card);
        else if (card.HasDescription || card.HasChecklist) Expand(card, card.HasDescription, card.HasChecklist);
    }

    private void DescChip_Click(object sender, RoutedEventArgs e) => TogglePart(CardOf(sender), description: true);

    private void ListChip_Click(object sender, RoutedEventArgs e) => TogglePart(CardOf(sender), description: false);

    /// <summary>The icon buttons show or hide just the description or just the checklist.</summary>
    private void TogglePart(CardViewModel card, bool description)
    {
        var showDescription = description ? !card.ShowDescription : card.ShowDescription;
        var showChecklist = description ? card.ShowChecklist : !card.ShowChecklist;
        if (showDescription || showChecklist) Expand(card, showDescription, showChecklist);
        else Collapse(card);
    }

    private void Expand(CardViewModel card, bool description, bool checklist)
    {
        if (_openCard is { } other && other != card) Collapse(other);
        _openCard = card;
        var loadChecklist = checklist && !card.ShowChecklist;
        card.ShowDescription = description;
        card.ShowChecklist = checklist;
        if (loadChecklist) _ = LoadChecklistAsync(card);
    }

    private void Collapse(CardViewModel card)
    {
        card.ShowDescription = false;
        card.ShowChecklist = false;
        if (_openCard == card) _openCard = null;
    }

    /// <summary>Loads the card's checklists fresh each time it opens; the list only carries the counts.</summary>
    private async Task LoadChecklistAsync(CardViewModel card)
    {
        if (_client is not { } client)
        {
            if (card.ChecklistRows.Count == 0) card.ChecklistStatus = "Connect to Trello to see the checklist.";
            return;
        }
        if (card.ChecklistRows.Count == 0) card.ChecklistStatus = "Loading the checklist...";
        try
        {
            card.ShowChecklists(await client.GetChecklists(card.Id));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (card.ChecklistRows.Count == 0)
                card.ChecklistStatus = ex is HttpRequestException or TaskCanceledException
                    ? "The checklist can't load while you're offline."
                    : "Couldn't load the checklist from Trello.";
        }
    }

    private async void CheckItem_Click(object sender, RoutedEventArgs e)
    {
        // The box has already flipped IsDone through its binding.
        var item = (CheckItemViewModel)((FrameworkElement)sender).DataContext;
        var card = item.Card;
        if (!CanEdit())
        {
            item.IsDone = !item.IsDone;
            return;
        }

        card.RecountChecklist();
        var done = item.IsDone;
        if (!await SaveAsync(client => client.SetCheckItem(card.Id, item.Id, done)))
            await LoadChecklistAsync(card);
    }

    /// <summary>Fades a description that is taller than its space and shows the "More in Trello" link.</summary>
    private void DescClip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border { Child: TextBlock text, DataContext: CardViewModel card } clip || clip.ActualWidth <= 0) return;
        if (string.IsNullOrEmpty(text.Text))
        {
            card.DescriptionClipped = false;
            return;
        }
        var full = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground,
            VisualTreeHelper.GetDpi(text).PixelsPerDip)
        {
            MaxTextWidth = clip.ActualWidth,
            LineHeight = text.LineHeight,
        };
        card.DescriptionClipped = full.Height > clip.MaxHeight + 0.5;
    }

    private async void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await AddAsync();
    }

    // ---------- Drag to reorder ----------

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Presses on the card's own buttons and checkboxes are clicks, not the start of a drag.
        _pressedCard = IsInside(e.OriginalSource as DependencyObject, el => el is ButtonBase) ? null : CardOf(sender);
        _pressPoint = e.GetPosition(this);
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedCard is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        var card = _pressedCard;
        _pressedCard = null;
        if (!CanEdit()) return;

        // A closed card keeps the drop line easy to follow.
        if (_openCard is not null) Collapse(_openCard);
        _dragging = true;
        card.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(CardViewModel), card), DragDropEffects.Move);
        }
        finally
        {
            card.IsDragging = false;
            _dragging = false;
            HideDropLine();
        }
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(CardViewModel)) is not CardViewModel card)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        AutoScroll(e.GetPosition(Scroller));

        var (index, y) = InsertionPoint(e.GetPosition(ListHost));
        _dropIndex = index;
        var from = _cards.IndexOf(card);
        if (index == from || index == from + 1)
        {
            DropLine.Visibility = Visibility.Collapsed;
            return;
        }
        DropLine.Margin = new Thickness(12, y - 1.5, 10, 0);
        DropLine.Visibility = Visibility.Visible;
    }

    private void List_DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(ListHost);
        if (p.X < 0 || p.Y < 0 || p.X >= ListHost.ActualWidth || p.Y >= ListHost.ActualHeight) HideDropLine();
    }

    private async void List_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = _dropIndex;
        HideDropLine();
        if (e.Data.GetData(typeof(CardViewModel)) is not CardViewModel card || index < 0) return;

        var from = _cards.IndexOf(card);
        var to = index > from ? index - 1 : index;
        await MoveAsync(card, to);
    }

    /// <summary>Which gap the pointer is nearest to, and where that gap is on screen.</summary>
    private (int Index, double Y) InsertionPoint(Point pointer)
    {
        double lastBottom = 0;
        for (var i = 0; i < _cards.Count; i++)
        {
            if (CardList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item) continue;
            var top = item.TranslatePoint(new Point(0, 0), ListHost).Y;
            if (pointer.Y < top + item.ActualHeight / 2) return (i, top);
            lastBottom = top + item.ActualHeight;
        }
        return (_cards.Count, lastBottom);
    }

    private void AutoScroll(Point pointer)
    {
        const double edge = 40, step = 14;
        if (pointer.Y < edge) Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - step);
        else if (pointer.Y > Scroller.ActualHeight - edge) Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + step);
    }

    private void HideDropLine()
    {
        DropLine.Visibility = Visibility.Collapsed;
        _dropIndex = -1;
    }

    private static bool IsInside(DependencyObject? element, Func<DependencyObject, bool> match)
    {
        while (element is not null)
        {
            if (match(element)) return true;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    // ---------- Small helpers ----------

    private void Renumber()
    {
        for (var i = 0; i < _cards.Count; i++)
        {
            _cards[i].Rank = i + 1;
            _cards[i].IsLast = i == _cards.Count - 1;
        }
        if (IsLoaded) UpdateStatus();
    }

    private void Flash(CardViewModel card)
    {
        card.IsHighlighted = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        timer.Tick += (_, _) =>
        {
            card.IsHighlighted = false;
            timer.Stop();
        };
        timer.Start();
    }

    private void ShowToast(string text, Action? undo = null)
    {
        _undo = undo;
        ToastText.Text = text;
        UndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
        _undo = null;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var undo = _undo;
        HideToast();
        undo?.Invoke();
    }

    private static string Shorten(string name) => name.Length > 32 ? name[..30] + "..." : name;
}
