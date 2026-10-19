using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace PrioritySidebar;

public sealed record LabelChip(string Name, Brush Dot);

public sealed class CardViewModel : INotifyPropertyChanged
{
    private string _name = "";
    private int _rank;
    private bool _isLast;
    private IReadOnlyList<LabelChip> _labels = [];
    private bool _isDragging;
    private bool _isHighlighted;
    private string _description = "";
    private int _checkTotal;
    private int _checkDone;
    private bool _showDescription;
    private bool _showChecklist;
    private bool _descriptionClipped;
    private string? _checklistStatus;

    public required string Id { get; set; }
    public double Pos { get; set; }
    public string? Url { get; set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public IReadOnlyList<LabelChip> Labels
    {
        get => _labels;
        set { Set(ref _labels, value); OnChanged(nameof(HasLabels)); OnChanged(nameof(ShowMeta)); }
    }
    public bool HasLabels => _labels.Count > 0;
    public bool IsDragging { get => _isDragging; set => Set(ref _isDragging, value); }
    public bool IsHighlighted { get => _isHighlighted; set => Set(ref _isHighlighted, value); }

    public int Rank
    {
        get => _rank;
        set { Set(ref _rank, value); OnChanged(nameof(CanMoveUp)); }
    }

    public bool IsLast
    {
        get => _isLast;
        set { Set(ref _isLast, value); OnChanged(nameof(CanMoveDown)); }
    }

    public bool CanMoveUp => _rank > 1;
    public bool CanMoveDown => !_isLast;

    // ---------- Description and checklists ----------

    /// <summary>The card's Trello description as plain text.</summary>
    public string Description
    {
        get => _description;
        set { Set(ref _description, value); OnChanged(nameof(HasDescription)); OnChanged(nameof(ShowMeta)); }
    }
    public bool HasDescription => _description.Length > 0;

    public int CheckTotal { get => _checkTotal; set { Set(ref _checkTotal, value); OnCheckCountChanged(); } }
    public int CheckDone { get => _checkDone; set { Set(ref _checkDone, value); OnCheckCountChanged(); } }
    public bool HasChecklist => _checkTotal > 0;
    public bool CheckComplete => _checkTotal > 0 && _checkDone >= _checkTotal;
    public string CheckCountText => $"{_checkDone}/{_checkTotal}";
    public string ChecklistLabel => $"Checklist, {_checkDone} of {_checkTotal} done";

    /// <summary>Whether the row under the name (labels, description and checklist buttons) has anything to show.</summary>
    public bool ShowMeta => HasLabels || HasDescription || HasChecklist;

    public bool ShowDescription
    {
        get => _showDescription;
        set { Set(ref _showDescription, value); OnChanged(nameof(IsExpanded)); }
    }

    public bool ShowChecklist
    {
        get => _showChecklist;
        set { Set(ref _showChecklist, value); OnChanged(nameof(IsExpanded)); }
    }

    public bool IsExpanded => _showDescription || _showChecklist;

    /// <summary>The description is longer than the space it gets, so it fades out with a link to Trello.</summary>
    public bool DescriptionClipped { get => _descriptionClipped; set => Set(ref _descriptionClipped, value); }

    /// <summary>"Loading..." or a problem, shown in place of the checklist.</summary>
    public string? ChecklistStatus
    {
        get => _checklistStatus;
        set { Set(ref _checklistStatus, value); OnChanged(nameof(HasChecklistStatus)); }
    }
    public bool HasChecklistStatus => _checklistStatus is not null;

    /// <summary>Checklist headings (only when there is more than one checklist) and items, in Trello's order.</summary>
    public ObservableCollection<object> ChecklistRows { get; } = [];

    public void ShowChecklists(List<TrelloChecklist> checklists)
    {
        ChecklistRows.Clear();
        var lists = checklists.Where(l => l.CheckItems is { Count: > 0 }).OrderBy(l => l.Pos).ToList();
        foreach (var list in lists)
        {
            var heading = lists.Count > 1 ? new ChecklistHeading(list.Name) : null;
            if (heading is not null) ChecklistRows.Add(heading);
            foreach (var item in list.CheckItems!.OrderBy(i => i.Pos))
            {
                var row = new CheckItemViewModel(this, heading, item.Id, item.Name, item.State == "complete");
                heading?.Items.Add(row);
                ChecklistRows.Add(row);
            }
            heading?.Refresh();
        }
        ChecklistStatus = null;
        RecountChecklist();
    }

    /// <summary>Updates the count and progress from the loaded items after one is ticked.</summary>
    public void RecountChecklist()
    {
        var items = ChecklistRows.OfType<CheckItemViewModel>().ToList();
        CheckTotal = items.Count;
        CheckDone = items.Count(i => i.IsDone);
    }

    private void OnCheckCountChanged()
    {
        OnChanged(nameof(HasChecklist));
        OnChanged(nameof(CheckComplete));
        OnChanged(nameof(CheckCountText));
        OnChanged(nameof(ChecklistLabel));
        OnChanged(nameof(ShowMeta));
    }

    /// <summary>What the card looks like, to tell whether a fresh copy from Trello changed anything.</summary>
    public string Signature => Name + "|" + string.Join(",", _labels.Select(l => l.Name));

    public static CardViewModel From(TrelloCard card) => new CardViewModel { Id = card.Id }.Update(card);

    public CardViewModel Update(TrelloCard card)
    {
        Name = card.Name;
        Pos = card.Pos;
        Url = card.ShortUrl;
        Labels = (card.Labels ?? [])
            .Where(l => !string.Equals(l.Name?.Trim(), "Done", StringComparison.OrdinalIgnoreCase))
            .Select(l => new LabelChip(l.Name ?? "", LabelColors.For(l.Color)))
            .ToList();
        Description = PlainText.FromMarkdown(card.Desc);
        CheckTotal = card.Badges?.CheckItems ?? 0;
        CheckDone = card.Badges?.CheckItemsChecked ?? 0;
        if (!HasDescription) ShowDescription = false;
        if (!HasChecklist) ShowChecklist = false;
        return this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(name);
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A checklist's name with its own count, shown when a card has more than one checklist.</summary>
public sealed class ChecklistHeading(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public List<CheckItemViewModel> Items { get; } = [];
    public string CountText => $"{Items.Count(i => i.IsDone)}/{Items.Count}";

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CheckItemViewModel(CardViewModel card, ChecklistHeading? heading, string id, string name, bool isDone)
    : INotifyPropertyChanged
{
    private bool _isDone = isDone;

    public CardViewModel Card { get; } = card;
    public string Id { get; } = id;
    public string Name { get; } = name;

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDone)));
            heading?.Refresh();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Turns a Trello description (Markdown) into plain text for the sidebar.</summary>
public static partial class PlainText
{
    public static string FromMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = markdown.Replace("\r\n", "\n");
        text = Rule().Replace(text, "");
        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Heading().Replace(text, "");
        text = Quote().Replace(text, "");
        text = Bullet().Replace(text, "$1• ");
        text = Strong().Replace(text, "$2");
        text = Emphasis().Replace(text, "$2");
        text = Code().Replace(text, "$1");
        text = Escape().Replace(text, "$1");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^>\s?", RegexOptions.Multiline)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^(\s*)[-*+]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex Strong();

    [GeneratedRegex(@"(?<![\w*_])([*_])(?!\s)(.+?)(?<!\s)\1(?![\w*_])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!>])")]
    private static partial Regex Escape();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}

/// <summary>Trello label colours, toned for the dark sidebar.</summary>
public static class LabelColors
{
    private static readonly Dictionary<string, Brush> Brushes = new()
    {
        ["green"] = Make("#6CC07A"),
        ["yellow"] = Make("#E5C454"),
        ["orange"] = Make("#E8964A"),
        ["red"] = Make("#E5675E"),
        ["purple"] = Make("#A98BE0"),
        ["blue"] = Make("#5AA9E6"),
        ["sky"] = Make("#5CC6E0"),
        ["lime"] = Make("#A3D45C"),
        ["pink"] = Make("#E57AA8"),
        ["black"] = Make("#8C9096"),
    };

    private static readonly Brush Fallback = Make("#8C9096");

    // Trello also uses shades such as "green_light" and "green_dark"; they map to the base colour.
    public static Brush For(string? color) =>
        color is not null && Brushes.TryGetValue(color.Split('_')[0], out var brush) ? brush : Fallback;

    private static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
