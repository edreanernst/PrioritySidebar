using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public required string Id { get; set; }
    public double Pos { get; set; }
    public string? Url { get; set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public IReadOnlyList<LabelChip> Labels { get => _labels; set { Set(ref _labels, value); OnChanged(nameof(HasLabels)); } }
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
