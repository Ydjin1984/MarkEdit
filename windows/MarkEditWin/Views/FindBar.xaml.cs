using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkEditWin.Views;

/// <summary>
/// Search options mirroring the shape the editor core expects for <c>search.updateQuery</c>.
/// </summary>
public sealed record FindOptions(
    string Search,
    bool CaseSensitive,
    bool WholeWord,
    bool RegularExpression,
    string Replace)
{
    /// <summary>The editor core has no separate literal flag, whole word and plain search cover it.</summary>
    public bool Literal => false;

    public bool DiacriticInsensitive => false;
}

/// <summary>
/// The find and replace bar. The editor core ships an intentionally empty search panel on
/// native hosts, so the panel itself lives here and drives the web search module.
/// </summary>
public partial class FindBar : UserControl
{
    public FindBar()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => SearchTermChanged?.Invoke(this, Options);
        SearchBox.KeyDown += OnSearchBoxKeyDown;
        ReplaceBox.KeyDown += OnReplaceBoxKeyDown;

        CaseButton.Click += (_, _) => OptionsChanged?.Invoke(this, Options);
        WordButton.Click += (_, _) => OptionsChanged?.Invoke(this, Options);
        RegexButton.Click += (_, _) => OptionsChanged?.Invoke(this, Options);

        PreviousButton.Click += (_, _) => PreviousRequested?.Invoke(this, EventArgs.Empty);
        NextButton.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
        ReplaceButton.Click += (_, _) => ReplaceRequested?.Invoke(this, EventArgs.Empty);
        ReplaceAllButton.Click += (_, _) => ReplaceAllRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<FindOptions>? SearchTermChanged;

    public event EventHandler<FindOptions>? OptionsChanged;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    public event EventHandler? ReplaceRequested;

    public event EventHandler? ReplaceAllRequested;

    public event EventHandler? CloseRequested;

    public FindOptions Options => new(
        SearchBox.Text,
        CaseButton.IsChecked == true,
        WordButton.IsChecked == true,
        RegexButton.IsChecked == true,
        ReplaceBox.Text);

    public string SearchTerm
    {
        get => SearchBox.Text;
        set
        {
            SearchBox.Text = value;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    public bool IsReplaceVisible
    {
        get => ReplaceRow.Visibility == Visibility.Visible;
        set => ReplaceRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdateCounter(int numberOfItems, int currentIndex, bool emptyInput)
    {
        if (emptyInput)
        {
            CounterLabel.Text = string.Empty;
            return;
        }

        if (numberOfItems > 0 && currentIndex >= 0)
        {
            CounterLabel.Text = $"{currentIndex + 1} of {numberOfItems}";
            return;
        }

        CounterLabel.Text = $"{numberOfItems}";
    }

    public void ClearCounter() => CounterLabel.Text = string.Empty;

    public void FocusSearchField(bool selectAll)
    {
        SearchBox.Focus();
        if (selectAll)
        {
            SearchBox.SelectAll();
        }
    }

    public void FocusReplaceField()
    {
        ReplaceBox.Focus();
        ReplaceBox.SelectAll();
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Enter:
                NextRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void OnReplaceBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                ReplaceRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }
}
