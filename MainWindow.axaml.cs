using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WojoPersistentEditor.Models;
using WojoPersistentEditor.Services;
using WojoPersistentEditor.Views;

namespace WojoPersistentEditor
{
    public partial class MainWindow : Window
    {
        private readonly InfoView infoView;
        private readonly CreditsView creditsView;
        private readonly EditorView editorView;
        private readonly PersistentLoaderView persistentLoaderView;

        private string? selectedPersistentFilePath;
        private string? savedPersistentFilePath;
        private bool isPreviewRequested;
        private bool isBusy;

        public MainWindow()
        {
            InitializeComponent();

            infoView = new InfoView();
            creditsView = new CreditsView();
            editorView = new EditorView();
            persistentLoaderView = new PersistentLoaderView();

            persistentLoaderView.LoadFileRequested +=
                OpenPersistentFile;

            selectedPersistentFilePath = null;
            savedPersistentFilePath = null;
            isPreviewRequested = false;
            isBusy = false;

            ShowView(infoView);
            UpdateActionButtons();
        }

        private void ShowView(Control view)
        {
            ContentHost.Content = view;
        }

        private void InfoButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowView(infoView);
        }

        private void PreviewButton_Click(object? sender, RoutedEventArgs e)
        {
            isPreviewRequested = true;
            editorView.SetPreviewMode(true);
            ShowPersistentWorkspace();
        }

        private void EditingButton_Click(object? sender, RoutedEventArgs e)
        {
            isPreviewRequested = false;
            editorView.SetPreviewMode(false);
            ShowPersistentWorkspace();
        }

        private async void BugsButton_Click(object? sender, RoutedEventArgs e)
        {
            Uri bugsUri = new Uri(
                "https://forms.gle/AGsKgWK8eRh6KSRD7"
            );

            await Launcher.LaunchUriAsync(bugsUri);
        }

        private void CreditsButton_Click(object? sender, RoutedEventArgs e)
        {
            ShowView(creditsView);
        }

        private async void SaveButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            if (selectedPersistentFilePath == null)
            {
                await ShowMessageAsync(
                    "Nothing to save",
                    "Select a persistent file before saving."
                );
                return;
            }

            if (isBusy)
            {
                return;
            }

            string sourcePersistentPath = selectedPersistentFilePath;
            string temporaryOutputPath =
                CreateTemporaryPersistentPath();

            SetBusy(true, "save");

            try
            {
                List<PersistentVariable> variables =
                    editorView.GetChangedVariables();
                PersistentEncodeResult? encodeResult = null;

                if (variables.Count == 0)
                {
                    await CopyFileAsync(
                        sourcePersistentPath,
                        temporaryOutputPath
                    );
                }
                else
                {
                    encodeResult =
                        await PythonPersistentEncoder.EncodeAsync(
                            sourcePersistentPath,
                            temporaryOutputPath,
                            variables
                        );
                }

                DeleteSavedPersistentFile();
                savedPersistentFilePath = temporaryOutputPath;

                if (encodeResult == null)
                {
                    await ShowMessageAsync(
                        "No changes",
                        "There are no edited variables.\n" +
                        "The original state was prepared for export."
                    );
                }
                else
                {
                    await ShowMessageAsync(
                        "Changes saved",
                        encodeResult.AppliedChanges +
                        " variables were saved.\n" +
                        "Use Download to export the edited " +
                        "persistent file."
                    );
                }
            }
            catch (Exception error)
            {
                DeleteFileIfExists(temporaryOutputPath);

                await ShowMessageAsync(
                    "Saving error",
                    "The persistent file could not be saved:\n" +
                    error.Message
                );
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void DownloadButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            if (
                savedPersistentFilePath == null ||
                !File.Exists(savedPersistentFilePath)
            )
            {
                await ShowMessageAsync(
                    "Nothing to download",
                    "Save your changes before downloading."
                );
                return;
            }

            if (isBusy)
            {
                return;
            }

            string preparedPersistentPath = savedPersistentFilePath;
            IStorageFile? exportFile;

            try
            {
                exportFile = await StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title =
                            "Export the edited Ren'Py persistent file",
                        SuggestedFileName = "persistent",
                        ShowOverwritePrompt = true,
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType(
                                "Ren'Py persistent file"
                            )
                            {
                                Patterns = new[] { "persistent*" }
                            },
                            FilePickerFileTypes.All
                        }
                    }
                );
            }
            catch (Exception error)
            {
                await ShowMessageAsync(
                    "Download error",
                    "The export window could not be opened:\n" +
                    error.Message
                );
                return;
            }

            if (exportFile == null)
            {
                return;
            }

            using (exportFile)
            {
                string? exportPath = exportFile.TryGetLocalPath();

                if (
                    exportPath != null &&
                    selectedPersistentFilePath != null &&
                    PathsReferToSameFile(
                        exportPath,
                        selectedPersistentFilePath
                    )
                )
                {
                    await ShowMessageAsync(
                        "Original file selected",
                        "Choose a different location or file name.\n" +
                        "The original persistent file will not be " +
                        "overwritten."
                    );
                    return;
                }

                SetBusy(true, "download");

                try
                {
                    await CopyToStorageFileAsync(
                        preparedPersistentPath,
                        exportFile
                    );

                    ResetLoadedPersistent();

                    await ShowMessageAsync(
                        "Download completed",
                        "The edited persistent file was exported " +
                        "successfully."
                    );
                }
                catch (Exception error)
                {
                    await ShowMessageAsync(
                        "Download error",
                        "The persistent file could not be exported:\n" +
                        error.Message
                    );
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }

        private void ShowPersistentWorkspace()
        {
            if (selectedPersistentFilePath == null)
            {
                ShowView(persistentLoaderView);
                return;
            }

            ShowView(editorView);
        }

        private async void OpenPersistentFile(
            object? sender,
            EventArgs e
        )
        {
            IReadOnlyList<IStorageFile> selectedFiles;

            try
            {
                selectedFiles =
                    await StorageProvider.OpenFilePickerAsync(
                        new FilePickerOpenOptions
                        {
                            Title = "Select a Ren'Py persistent file",
                            AllowMultiple = false,
                            FileTypeFilter = new[]
                            {
                                new FilePickerFileType(
                                    "Ren'Py persistent files"
                                )
                                {
                                    Patterns = new[] { "persistent*" }
                                },
                                FilePickerFileTypes.All
                            }
                        }
                    );
            }
            catch (Exception error)
            {
                persistentLoaderView.ShowStatus(
                    "The file selector could not be opened: " +
                    error.Message,
                    true
                );
                return;
            }

            if (selectedFiles.Count == 0)
            {
                return;
            }

            string? selectedFilePath =
                selectedFiles[0].TryGetLocalPath();

            if (selectedFilePath == null)
            {
                persistentLoaderView.ShowStatus(
                    "The selected file does not have a local path.",
                    true
                );
                return;
            }

            persistentLoaderView.SetLoading(true);

            try
            {
                PersistentDecodeResult decodeResult =
                    await PythonPersistentDecoder.DecodeAsync(
                        selectedFilePath
                    );

                editorView.LoadVariables(decodeResult.Variables);
                editorView.SetPreviewMode(isPreviewRequested);
                DeleteSavedPersistentFile();
                selectedPersistentFilePath = selectedFilePath;
                UpdateActionButtons();

                ShowView(editorView);

                if (decodeResult.UnknownClasses.Count > 0)
                {
                    await ShowMessageAsync(
                        "Unknown Ren'Py classes",
                        "The file was decoded, but it contains unknown " +
                        "classes. They can be displayed, but exporting " +
                        "changes may not be available for this file yet."
                    );
                }
            }
            catch (Exception error)
            {
                selectedPersistentFilePath = null;
                DeleteSavedPersistentFile();
                UpdateActionButtons();
                ShowView(persistentLoaderView);
                persistentLoaderView.ShowStatus(
                    "Loading error: " + error.Message,
                    true
                );
            }
            finally
            {
                persistentLoaderView.SetLoading(false);
            }
        }

        private void SetBusy(
            bool busy,
            string? operation = null
        )
        {
            isBusy = busy;
            editorView.IsEnabled = !busy;

            SaveButton.Content = busy && operation == "save"
                ? "SAVING..."
                : "SAVE";
            DownloadButton.Content = busy && operation == "download"
                ? "DOWNLOADING..."
                : "DOWNLOAD";

            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            SaveButton.IsEnabled =
                !isBusy && selectedPersistentFilePath != null;
            DownloadButton.IsEnabled =
                !isBusy &&
                savedPersistentFilePath != null &&
                File.Exists(savedPersistentFilePath);
        }

        private static async Task CopyFileAsync(
            string sourcePath,
            string outputPath
        )
        {
            await using FileStream sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                true
            );
            await using FileStream outputStream = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true
            );

            await sourceStream.CopyToAsync(outputStream);
        }

        private static async Task CopyToStorageFileAsync(
            string sourcePath,
            IStorageFile outputFile
        )
        {
            await using FileStream sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                true
            );
            await using Stream outputStream =
                await outputFile.OpenWriteAsync();

            if (outputStream.CanSeek)
            {
                outputStream.Position = 0;
                outputStream.SetLength(0);
            }

            await sourceStream.CopyToAsync(outputStream);
            await outputStream.FlushAsync();
        }

        private static bool PathsReferToSameFile(
            string firstPath,
            string secondPath
        )
        {
            try
            {
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                return string.Equals(
                    Path.GetFullPath(firstPath),
                    Path.GetFullPath(secondPath),
                    comparison
                );
            }
            catch
            {
                return false;
            }
        }

        private static string CreateTemporaryPersistentPath()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "WojoPersistentEditor"
            );

            Directory.CreateDirectory(temporaryDirectory);

            return Path.Combine(
                temporaryDirectory,
                Guid.NewGuid().ToString("N") + ".persistent"
            );
        }

        private void ResetLoadedPersistent()
        {
            DeleteSavedPersistentFile();
            selectedPersistentFilePath = null;

            editorView.LoadVariables(
                new List<PersistentVariable>()
            );
            persistentLoaderView.ShowStatus(
                "Select another persistent file to continue.",
                false
            );

            ShowView(persistentLoaderView);
            UpdateActionButtons();
        }

        private void DeleteSavedPersistentFile()
        {
            if (savedPersistentFilePath != null)
            {
                DeleteFileIfExists(savedPersistentFilePath);
            }

            savedPersistentFilePath = null;
        }

        private static void DeleteFileIfExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                File.Delete(filePath);
            }
            catch
            {
            }
        }

        private async Task ShowMessageAsync(
            string header,
            string message
        )
        {
            MessageDialog dialog = new MessageDialog(
                header,
                message
            );

            await dialog.ShowDialog(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            DeleteSavedPersistentFile();
            base.OnClosed(e);
        }
    }
}
