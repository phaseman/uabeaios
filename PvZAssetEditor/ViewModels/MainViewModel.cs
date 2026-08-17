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

    [ObservableProperty]
    private string _fullJsonText = string.Empty;

    [ObservableProperty]
    private bool _isDeckEditorVisible;

    [ObservableProperty]
    private bool _isFullJsonEditorVisible;

    [ObservableProperty]
    private bool _canSwitchEditorMode;

    [ObservableProperty]
    private string _editorModeButtonText = "Full JSON";

    public ObservableCollection<DeckModel> VisibleDecks { get; } = [];

    public RecipeDeckDocument? Document => _document;

    public void LoadDocument(RecipeDeckDocument document)
    {
        _document?.Dispose();
        _document = document;
        FileName = document.SourceName;
        FileSummary = document.Decks.Count > 0
            ? $"Unity {document.UnityVersion} • {document.Decks.Count} decks • {document.AssetCount} assets"
            : $"Unity {document.UnityVersion} • Full JSON • {document.AssetCount} assets";
        HasDocument = true;
        CanSwitchEditorMode = document.Decks.Count > 0;
        IsDeckEditorVisible = CanSwitchEditorMode;
        IsFullJsonEditorVisible = !CanSwitchEditorMode;
        EditorModeButtonText = IsFullJsonEditorVisible ? "Strategy decks" : "Full JSON";
        FullJsonText = IsFullJsonEditorVisible ? document.ExportFullJson() : string.Empty;
        SearchText = string.Empty;
        ApplyFilter();
        SelectedDeck = VisibleDecks.FirstOrDefault();
        StatusMessage = IsFullJsonEditorVisible
            ? "Full JSON mode. Edit values inside $data, then tap Save. Metadata beginning with $ is protected."
            : "Ready. Select a deck, make changes, then tap Save.";
    }

    public byte[] BuildDocument()
    {
        if (_document is null)
            throw new InvalidOperationException("No file is open.");

        return IsFullJsonEditorVisible
            ? _document.BuildFromFullJson(FullJsonText)
            : _document.Build();
    }

    public void ToggleEditorMode()
    {
        if (_document is null || !CanSwitchEditorMode)
            return;

        IsFullJsonEditorVisible = !IsFullJsonEditorVisible;
        IsDeckEditorVisible = !IsFullJsonEditorVisible;
        EditorModeButtonText = IsFullJsonEditorVisible ? "Strategy decks" : "Full JSON";

        if (IsFullJsonEditorVisible && string.IsNullOrEmpty(FullJsonText))
            FullJsonText = _document.ExportFullJson();

        StatusMessage = IsFullJsonEditorVisible
            ? "Full JSON mode. Edit values inside $data, then tap Save. Metadata beginning with $ is protected."
            : "Strategy deck mode. Select a deck, make changes, then tap Save.";
    }

    public void FormatFullJson()
    {
        try
        {
            FullJsonText = RecipeDeckDocument.FormatFullJson(FullJsonText);
            StatusMessage = "JSON is valid and has been formatted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"JSON is not valid: {ex.Message}";
        }
    }

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
