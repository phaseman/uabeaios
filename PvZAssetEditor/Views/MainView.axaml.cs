using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PvZAssetEditor.Core;
using PvZAssetEditor.Models;
using PvZAssetEditor.ViewModels;

namespace PvZAssetEditor.Views;

public partial class MainView : UserControl
{
    private IStorageFile? _openedFile;

    public MainView()
    {
        InitializeComponent();
    }

    private MainViewModel ViewModel
        => DataContext as MainViewModel
           ?? throw new InvalidOperationException("The editor view model is unavailable.");

    private async void OpenFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open a Unity assets or bundle file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Unity files") { Patterns = ["*"] }
                ]
            });

        IStorageFile? file = files.FirstOrDefault();
        if (file is null)
            return;

        ViewModel.IsBusy = true;
        ViewModel.StatusMessage = "Reading and validating the Unity bundle…";

        try
        {
            await using Stream stream = await file.OpenReadAsync();
            RecipeDeckDocument document = RecipeDeckDocument.Load(stream, file.Name);
            _openedFile = file;
            ViewModel.LoadDocument(document);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open this file: {ex.Message}";
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async void SaveFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_openedFile is null || ViewModel.Document is null)
            return;

        if (ViewModel.IsFullJsonEditorVisible)
            ViewModel.FullJsonText = RawJsonEditor.Text ?? string.Empty;

        ViewModel.IsBusy = true;
        ViewModel.StatusMessage = "Rebuilding the Unity bundle…";

        try
        {
            byte[] bytes = ViewModel.BuildDocument();
            string backupPath = await CreateBackupAsync(_openedFile);
            await ReplaceFileAsync(_openedFile, bytes);

            await using var reloadedStream = new MemoryStream(bytes, writable: false);
            RecipeDeckDocument reloaded = RecipeDeckDocument.Load(reloadedStream, _openedFile.Name);
            ViewModel.LoadDocument(reloaded);
            ViewModel.StatusMessage = $"Saved successfully. Backup: {backupPath}";
        }
        catch (InvalidOperationException ex)
        {
            ViewModel.StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Save failed; the original file was left in place when possible. {ex.Message}";
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private void AddCard_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ViewModel.SelectedDeck?.AddCard(false);

    private void ToggleEditorMode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel.IsFullJsonEditorVisible)
            ViewModel.FullJsonText = RawJsonEditor.Text ?? string.Empty;
        ViewModel.ToggleEditorMode();
    }

    private void FormatJson_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel.FullJsonText = RawJsonEditor.Text ?? string.Empty;
        ViewModel.FormatFullJson();
    }

    private void AddOverride_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ViewModel.SelectedDeck?.AddCard(true);

    private void RemoveCard_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CardEntryModel card })
            ViewModel.SelectedDeck?.RemoveCard(card, false);
    }

    private void RemoveOverride_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CardEntryModel card })
            ViewModel.SelectedDeck?.RemoveCard(card, true);
    }

    private static async Task<string> CreateBackupAsync(IStorageFile file)
    {
        string? localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            string besideOriginal = $"{localPath}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            File.Copy(localPath, besideOriginal, overwrite: false);
            return besideOriginal;
        }

        string backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "UnityAssetEditor",
            "Backups");
        Directory.CreateDirectory(backupDirectory);

        string safeName = string.Concat(file.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string backupPath = Path.Combine(
            backupDirectory,
            $"{safeName}.{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak");

        await using Stream input = await file.OpenReadAsync();
        await using var output = File.Create(backupPath);
        await input.CopyToAsync(output);
        await output.FlushAsync();
        return backupPath;
    }

    private static async Task ReplaceFileAsync(IStorageFile file, byte[] bytes)
    {
        string? localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            string temporaryPath = localPath + ".unityasseteditor.tmp";
            await File.WriteAllBytesAsync(temporaryPath, bytes);
            File.Move(temporaryPath, localPath, true);
            return;
        }

        await using Stream output = await file.OpenWriteAsync();
        output.SetLength(0);
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    }
}
