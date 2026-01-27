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
using System.Data;
using System.IO;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AddStaffViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // --- Manual Entry Fields ---
        [ObservableProperty] private string _firstName;
        [ObservableProperty] private string _lastName;
        [ObservableProperty] private string _designation;
        [ObservableProperty] private string _department;
        [ObservableProperty] private string _email;
        [ObservableProperty] private string _phone;
        [ObservableProperty] private decimal _baseSalary;
        [ObservableProperty] private string _staffType = "Teaching";

        // --- MISSING PROPERTIES ADDED HERE ---
        [ObservableProperty] private DateTime _joiningDate = DateTime.Now;
        [ObservableProperty] private string _address;
        [ObservableProperty] private string _emergencyContact;

        // --- Bulk Upload State ---
        [ObservableProperty] private string _fileName = "No file selected";
        [ObservableProperty] private bool _isImporting = false;

        public AddStaffViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        [RelayCommand]
        public void SaveEmployee()
        {
            // 1. Basic Validation
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(Designation))
            {
                MessageBox.Show("Please enter at least a First Name and Designation.");
                return;
            }

            // 2. PHONE NUMBER VALIDATION (New Fix)
            // Check if Phone is not empty
            if (!string.IsNullOrWhiteSpace(Phone))
            {
                // Check if it contains only digits
                if (!System.Text.RegularExpressions.Regex.IsMatch(Phone, @"^\d+$"))
                {
                    MessageBox.Show("Phone number must contain only alphabets (0-9). No letters allowed.");
                    return;
                }

                // Check length (Must be exactly 10 digits for standard mobile numbers)
                if (Phone.Length != 10)
                {
                    MessageBox.Show($"Phone number must be exactly 10 digits.\nYou entered {Phone.Length} digits.");
                    return;
                }
            }

            // 3. Create Employee Object
            var newEmp = new Employee
            {
                FirstName = FirstName,
                LastName = LastName,
                Designation = Designation,
                Department = Department,
                Email = Email,
                PhoneNumber = Phone,
                BaseSalary = BaseSalary,
                StaffType = StaffType,
                IsActive = true,
                JoiningDate = JoiningDate,
                Address = Address,
                //EmergencyContact = EmergencyContact
            };

            // 4. Save to Database
            try
            {
                _payrollService.AddEmployee(newEmp);
                MessageBox.Show("Staff member added successfully!");
                GoBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving employee: {ex.Message}");
            }
        }
        [RelayCommand]
        public void BrowseFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xls;*.xlsx;*.csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FileName = openFileDialog.FileName;
            }
        }

        [RelayCommand]
        public void ImportExcel()
        {
            if (string.IsNullOrEmpty(FileName) || FileName == "No file selected")
            {
                MessageBox.Show("Please select a file first.");
                return;
            }

            IsImporting = true;
            try
            {
                // 1. Get List of EXISTING Emails to prevent duplicates
                var existingStaff = _payrollService.GetAllEmployees();
                // Create a quick lookup list of emails (lowercase for safety)
                var existingEmails = new HashSet<string>(existingStaff.Select(e => e.Email.ToLower()));

                var newEmployees = new List<Employee>();
                int duplicatesSkipped = 0;

                using (var stream = File.Open(FileName, FileMode.Open, FileAccess.Read))
                {
                    IExcelDataReader reader;

                    // Choose Reader
                    if (Path.GetExtension(FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        reader = ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration()
                        {
                            FallbackEncoding = System.Text.Encoding.GetEncoding(1252)
                        });
                    }
                    else
                    {
                        reader = ExcelReaderFactory.CreateReader(stream);
                    }

                    using (reader)
                    {
                        var result = reader.AsDataSet();
                        if (result.Tables.Count == 0 || result.Tables[0].Rows.Count == 0) return;

                        var table = result.Tables[0];
                        if (table.Columns.Count < 8)
                        {
                            MessageBox.Show("Error: File needs 8 columns.");
                            return;
                        }

                        // Iterate Rows
                        for (int i = 1; i < table.Rows.Count; i++)
                        {
                            var row = table.Rows[i];
                            if (row[0] == null || string.IsNullOrWhiteSpace(row[0].ToString())) continue;

                            string email = row[5]?.ToString() ?? "";

                            // --- DUPLICATE CHECK ---
                            if (existingEmails.Contains(email.ToLower()))
                            {
                                duplicatesSkipped++;
                                continue; 
                            }

                            try
                            {
                                var emp = new Employee
                                {
                                    FirstName = row[0]?.ToString() ?? "",
                                    LastName = row[1]?.ToString() ?? "",
                                    Designation = row[2]?.ToString() ?? "",
                                    Department = row[3]?.ToString() ?? "",
                                    StaffType = row[4]?.ToString() ?? "Teaching",
                                    Email = email,
                                    PhoneNumber = row[6]?.ToString() ?? "",
                                    BaseSalary = decimal.TryParse(row[7]?.ToString(), out decimal sal) ? sal : 0,
                                    IsActive = true,
                                    JoiningDate = DateTime.Now
                                };
                                newEmployees.Add(emp);

                                // Add to our checking list so we don't add duplicates from within the same file too
                                existingEmails.Add(email.ToLower());
                            }
                            catch { continue; }
                        }
                    }
                }

                // 2. Save Only NEW People
                if (newEmployees.Count > 0)
                {
                    _payrollService.AddEmployeesBulk(newEmployees);
                    MessageBox.Show($"Success!\n\nImported: {newEmployees.Count} new staff.\nSkipped: {duplicatesSkipped} duplicates.");
                    GoBack();
                }
                else
                {
                    if (duplicatesSkipped > 0)
                        MessageBox.Show($"No new data found.\nAll {duplicatesSkipped} rows were duplicates and already exist.");
                    else
                        MessageBox.Show("No valid data found in file.");
                }
            }
            catch (IOException)
            {
                MessageBox.Show("The file is open in Excel. Close it and try again.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
            finally
            {
                IsImporting = false;
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            // Fix: Use correct namespace casting if needed, or simply resolve via DI
            var services = ((App)Application.Current).Services;
            var directory = services.GetRequiredService<StaffDirectoryView>();
            var directoryVM = services.GetRequiredService<StaffDirectoryViewModel>();

            directoryVM.PerformSearch();
            directory.DataContext = directoryVM;

            Application.Current.MainWindow.Content = directory;
        }
    }
}