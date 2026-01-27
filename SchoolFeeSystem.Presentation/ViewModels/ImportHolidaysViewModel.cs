using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using SchoolFeeSystem.Presentation;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ImportHolidaysViewModel : ObservableObject
    {
        private readonly IAttendanceService _attendanceService;

        public ImportHolidaysViewModel(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
            // Register encoding for Excel Reader
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        [RelayCommand]
        public void UploadFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel/CSV Files|*.csv;*.xlsx;*.xls"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ProcessFile(openFileDialog.FileName);
            }
        }

        private void ProcessFile(string filePath)
        {
            try
            {
                var newHolidays = new List<Holiday>();

                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                {
                    IExcelDataReader reader;

                    // 1. Select Correct Reader
                    if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        reader = ExcelReaderFactory.CreateCsvReader(stream);
                    }
                    else
                    {
                        reader = ExcelReaderFactory.CreateReader(stream);
                    }

                    // 2. Parse Data
                    using (reader)
                    {
                        var result = reader.AsDataSet();
                        if (result.Tables.Count == 0) return;

                        var table = result.Tables[0];

                        // Skip Header Row (Start at i = 1)
                        for (int i = 1; i < table.Rows.Count; i++)
                        {
                            var row = table.Rows[i];

                            // Expecting: Column 0 = Date, Column 1 = Holiday Name
                            if (row[0] == null || string.IsNullOrWhiteSpace(row[0].ToString())) continue;

                            if (DateTime.TryParse(row[0].ToString(), out DateTime date))
                            {
                                var name = row[1]?.ToString() ?? "Holiday";
                                newHolidays.Add(new Holiday { Date = date, Name = name, IsRecurring = true });
                            }
                        }
                    }
                }

                if (newHolidays.Count > 0)
                {
                    // Save individually or add a BulkAdd method to your Service
                    foreach (var h in newHolidays)
                    {
                        _attendanceService.AddHoliday(h);
                    }

                    MessageBox.Show($"Success! Imported {newHolidays.Count} holidays.");
                    GoBack();
                }
                else
                {
                    MessageBox.Show("No valid data found. Check the date format.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var view = services.GetRequiredService<HolidayManagementView>();
            // Refresh the list when returning
            ((HolidayManagementViewModel)view.DataContext).LoadHolidays();
            Application.Current.MainWindow.Content = view;
        }
    }
}