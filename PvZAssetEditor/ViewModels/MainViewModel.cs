using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PvZAssetEditor.Core;
using PvZAssetEditor.Models;

namespace PvZAssetEditor.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private RecipeDeckDocument? _document;

    [ObservableProperty]
    private string _fileName = "No file open";

    [ObservableProperty]
    private string _fileSummary = "Choose a Unity bundle to begin.";

    [ObservableProperty]
    private string _statusMessage = "Edits stay on this device. A backup is created before saving.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DeckModel? _selectedDeck;

    [ObservableProperty]
    private bool _hasDocument;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<DeckModel> VisibleDecks { get; } = [];

    public RecipeDeckDocument? Document => _document;

    public void LoadDocument(RecipeDeckDocument document)
    {
        _document?.Dispose();
        _document = document;
        FileName = document.SourceName;
        FileSummary = $"Unity {document.UnityVersion} • {document.Decks.Count} decks • {document.AssetCount} assets";
        HasDocument = true;
        SearchText = string.Empty;
        ApplyFilter();
        SelectedDeck = VisibleDecks.FirstOrDefault();
        StatusMessage = "Ready. Select a deck, make changes, then tap Save.";
    }

    public byte[] BuildDocument()
        => _document?.Build() ?? throw new InvalidOperationException("No file is open.");

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        DeckModel? previous = SelectedDeck;
        VisibleDecks.Clear();

        if (_document is null)
            return;

        IEnumerable<DeckModel> filtered = _document.Decks;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(deck =>
                deck.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        foreach (DeckModel deck in filtered)
            VisibleDecks.Add(deck);

        SelectedDeck = previous is not null && VisibleDecks.Contains(previous)
            ? previous
            : VisibleDecks.FirstOrDefault();
    }
}
