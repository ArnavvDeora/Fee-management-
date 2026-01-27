using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Interfaces; // Ensure this is here
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AttendanceImportViewModel : ObservableObject
    {
        // FIX: Use the new Attendance Interface
        private readonly IAttendanceService _attendanceService;

        [ObservableProperty]
        private string _statusMessage = "Select a file to upload";

        // FIX: Inject the Attendance Service
        public AttendanceImportViewModel(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        [RelayCommand]
        public void UploadCsv()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files|*.*",
                Title = "Select Attendance Report"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                StatusMessage = "Processing file... please wait.";
                try
                {
                    // FIX: Call the method on the correct service
                    _attendanceService.ImportAttendance(openFileDialog.FileName);

                    StatusMessage = "Success! Attendance database updated.";
                    MessageBox.Show("Attendance imported successfully!");
                }
                catch (System.Exception ex)
                {
                    StatusMessage = "Error: " + ex.Message;
                    MessageBox.Show("Failed to import: " + ex.Message);
                }
            }
        }
    }
}