using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.Views;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly CsvDataService _csvService;

        public ObservableCollection<string> LoadedFiles { get; } = new();

        [ObservableProperty]
        private string selectedFile;

        public DashboardViewModel(CsvDataService csvService)
        {
            _csvService = csvService;
            RefreshLoadedFiles();
        }

        private void RefreshLoadedFiles()
        {
            LoadedFiles.Clear();
            foreach (var file in _csvService.GetLoadedFiles())
            {
                LoadedFiles.Add(System.IO.Path.GetFileName(file));
            }
        }

        // =========================
        // UPLOAD CSV / EXCEL
        // =========================
        [RelayCommand]
        public void UploadCsv()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                int successCount = 0;
                int failCount = 0;
                string errors = "";

                foreach (var fileName in dialog.FileNames)
                {
                    try
                    {
                        _csvService.LoadFile(fileName);
                        successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        failCount++;
                        errors += $"\n• {System.IO.Path.GetFileName(fileName)}: {ex.Message}";
                    }
                }

                RefreshLoadedFiles();

                if (successCount > 0 && failCount == 0)
                {
                    MessageBox.Show(
                        $"✅ {successCount} Excel file(s) loaded successfully!",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else if (successCount > 0 && failCount > 0)
                {
                    MessageBox.Show(
                        $"⚠️ Partially successful:\n\n" +
                        $"✅ Loaded: {successCount}\n" +
                        $"❌ Failed: {failCount}\n\n" +
                        $"Errors:{errors}",
                        "Partial Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"❌ Failed to load files:\n{errors}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        // =========================
        // DELETE COURSE (REMOVE FILE)
        // =========================
        [RelayCommand]
        public void RemoveSelectedFile()
        {
            if (string.IsNullOrEmpty(SelectedFile))
            {
                MessageBox.Show(
                    "Please select a course/file to delete.",
                    "No File Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"⚠️ Are you sure you want to DELETE this course?\n\n" +
                $"File: {SelectedFile}\n\n" +
                "This will:\n" +
                "• Remove all class/sheet data from this file\n" +
                "• Remove the file from the loaded files list\n" +
                "• This action CANNOT be undone\n\n" +
                "You will need to re-upload the file if you delete it.\n\n" +
                "Do you want to proceed?",
                "⚠️ Confirm Delete Course",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Double confirmation for safety
                var doubleCheck = MessageBox.Show(
                    $"🛑 FINAL CONFIRMATION\n\n" +
                    $"You are about to permanently delete:\n" +
                    $"{SelectedFile}\n\n" +
                    "Are you ABSOLUTELY SURE?",
                    "🛑 Final Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Stop);

                if (doubleCheck == MessageBoxResult.Yes)
                {
                    var fullPath = _csvService.GetLoadedFiles()
                        .FirstOrDefault(f => System.IO.Path.GetFileName(f) == SelectedFile);

                    if (fullPath != null)
                    {
                        try
                        {
                            _csvService.RemoveFile(fullPath);
                            RefreshLoadedFiles();

                            MessageBox.Show(
                                $"✅ Course deleted successfully!\n\n" +
                                $"Deleted: {SelectedFile}\n\n" +
                                "The file has been removed from the system.\n" +
                                "To add it back, you'll need to re-upload it.",
                                "Course Deleted",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            // Clear selection
                            SelectedFile = null;
                        }
                        catch (System.Exception ex)
                        {
                            MessageBox.Show(
                                $"❌ Failed to delete course:\n\n{ex.Message}",
                                "Delete Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        // =========================
        // NAVIGATION BUTTONS
        // =========================
        [RelayCommand]
        public void ShowClasses()
        {
            var view = App.Current.Services.GetRequiredService<ClassView>();
            Application.Current.MainWindow.Content = view;
        }

        
        /*public void ShowStudents()
        {
            var view = App.Current.Services.GetRequiredService<StudentView>();
            Application.Current.MainWindow.Content = view;
        }*/

        [RelayCommand]
        public void ShowFees()
        {
            var view = App.Current.Services.GetRequiredService<FeeView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void ShowFeeCollection()
        {
            var view = App.Current.Services.GetRequiredService<FeeCollectionView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void ShowScholarships()
        {
            var view = App.Current.Services.GetRequiredService<ScholarshipView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void ShowReports()
        {
            var view = App.Current.Services.GetRequiredService<ReportsView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void ShowHelp()
        {
            var view = App.Current.Services.GetRequiredService<HelpView>();
            Application.Current.MainWindow.Content = view;
        }

        [RelayCommand]
        public void GoBackToMain()
        {
            var result = MessageBox.Show(
                "Are you sure you want to go back to the main window?\n\n" +
                "Make sure you've saved any changes.",
                "Confirm Back",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var MainSelection = App.Current.Services.GetRequiredService<MainSelectionView>();
                Application.Current.MainWindow.Content = MainSelection;
            }
        }

        [RelayCommand]
        public void Logout()
        {
            var result = MessageBox.Show(
                "Are you sure you want to logout?",
                "Confirm Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var login = App.Current.Services.GetRequiredService<LoginView>();
                Application.Current.MainWindow.Content = login;
            }
        }
    }
}