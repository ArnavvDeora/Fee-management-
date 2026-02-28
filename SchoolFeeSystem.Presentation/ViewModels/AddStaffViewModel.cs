using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SchoolFeeSystem; // Access to App class
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class AddStaffViewModel : ObservableObject
    {
        private readonly IPayrollService _payrollService;

        // --- 1. Personal Details ---
        [ObservableProperty] private string _firstName;
        [ObservableProperty] private string _lastName;
        [ObservableProperty] private string _fatherName;
        [ObservableProperty] private DateTime _dateOfBirth = new DateTime(1990, 1, 1);
        [ObservableProperty] private string _gender = "Male";
        [ObservableProperty] private string _maritalStatus = "Single";
        [ObservableProperty] private string _category = "General";
        [ObservableProperty] private string _qualification;
        [ObservableProperty] private string _address;
        [ObservableProperty] private string _phone;
        [ObservableProperty] private string _email;

        // --- 2. Government IDs & Attendance ---
        [ObservableProperty] private string _aadharNumber;
        [ObservableProperty] private string _panNumber;

        // SS Code = HR payroll identifier from SS_Master (e.g. SS/CIHT/24103)
        // Leave blank here — it gets auto-filled when you do "Import SS Master" in Staff Directory
        [ObservableProperty] private string _ssCode;

        // ESI No = from SS_Master column H
        [ObservableProperty] private string _esiNumber;

        // Biometric ID = attendance device code (e.g. 101, CIHT007)
        // Leave blank — auto-filled on first attendance import
        [ObservableProperty] private string _biometricId;

        // --- 3. Official Details ---
        [ObservableProperty] private string _designation;
        [ObservableProperty] private string _department;
        [ObservableProperty] private DateTime _joiningDate = DateTime.Now;
        [ObservableProperty] private string _staffType = "Teaching";
        [ObservableProperty] private decimal _baseSalary;

        // --- 4. Banking & Statutory ---
        [ObservableProperty] private string _bankAccountNo;
        [ObservableProperty] private string _ifscCode;
        [ObservableProperty] private string _uanNumber;

        // --- 5. Photo Logic ---
        [ObservableProperty] private byte[] _photoBytes;
        [ObservableProperty] private BitmapImage _photoPreview;

        // --- Bulk Upload State ---
        [ObservableProperty] private string _fileName = "No file selected";
        [ObservableProperty] private bool _isImporting;

        public List<string> Categories { get; } = new() { "General", "OBC", "SC", "ST", "BC", "Other" };
        public List<string> MaritalStatuses { get; } = new() { "Single", "Married", "Divorced", "Widowed" };
        public List<string> StaffTypes { get; } = new() { "Teaching", "Non-Teaching", "Admin", "Support" };

        public AddStaffViewModel(IPayrollService payrollService)
        {
            _payrollService = payrollService;
        }

        // ---------------------------------------------------------
        // MANUAL ENTRY LOGIC
        // ---------------------------------------------------------

        [RelayCommand]
        public void BrowsePhoto()
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Select Staff Photo"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    PhotoBytes = File.ReadAllBytes(dlg.FileName);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dlg.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PhotoPreview = bitmap;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message);
                }
            }
        }

        [RelayCommand]
        public void SaveStaff()
        {
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(Phone))
            {
                MessageBox.Show("First Name and Mobile Number are required.");
                return;
            }

            // [FEATURE] Assign Attendance ID if missing
            // If user left Biometric ID blank, we can generate a placeholder or leave null (Optional)
            // The "System ID" (Primary Key) is assigned automatically by the Database upon saving.

            var newEmp = new Employee
            {
                FirstName = FirstName,
                LastName = LastName ?? "",
                FatherName = FatherName,
                DateOfBirth = DateOfBirth,
                Gender = Gender,
                MaritalStatus = MaritalStatus,
                Category = Category,
                Qualification = Qualification,
                Address = Address,
                PhoneNumber = Phone,
                Email = Email,
                AadharNumber = AadharNumber,
                PanNumber = PanNumber,
                SsCode = SsCode,
                EsiNumber = EsiNumber,

                // [NOTE] BiometricId is the ATTENDANCE DEVICE code (101, CIHT007 etc.)
                // It is different from SsCode (HR payroll code).
                // Leave blank here — it gets auto-populated the first time you import
                // an attendance file for this employee.
                BiometricId = BiometricId,

                Designation = Designation,
                Department = Department,
                StaffType = StaffType,
                JoiningDate = JoiningDate,
                BaseSalary = BaseSalary,
                BankAccountNo = BankAccountNo,
                IfscCode = IfscCode,
                UanNumber = UanNumber,
                Photo = PhotoBytes ?? GetDefaultAvatar(),
                IsActive = true
            };

            _payrollService.AddEmployee(newEmp);

            // At this point, newEmp.Id (System ID) is populated by the database.
            MessageBox.Show($"Staff '{FirstName}' added successfully!\nSystem ID: {newEmp.Id}");

            GoBack();
        }

        // ---------------------------------------------------------
        // BULK IMPORT LOGIC
        // ---------------------------------------------------------

        [RelayCommand]
        public void BrowseFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel/CSV Files|*.xlsx;*.xls;*.csv",
                Title = "Select Staff List"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FileName = openFileDialog.FileName;
            }
        }

        [RelayCommand]
        public void ImportExcel()
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            if (string.IsNullOrEmpty(FileName) || FileName == "No file selected")
            {
                MessageBox.Show("Please select a file first.");
                return;
            }

            IsImporting = true;
            var failedRows = new List<(int RowNumber, string Reason)>();
            int successCount = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using var stream = File.Open(FileName, FileMode.Open, FileAccess.Read);
                using var reader = Path.GetExtension(FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? ExcelReaderFactory.CreateCsvReader(stream)
                    : ExcelReaderFactory.CreateReader(stream);

                var table = reader.AsDataSet().Tables[0];

                for (int i = 1; i < table.Rows.Count; i++)
                {
                    try
                    {
                        var row = table.Rows[i];
                        string GetVal(int idx) => row[idx]?.ToString()?.Trim() ?? "";

                        string fullName = GetVal(0);
                        string mobile = GetVal(6);

                        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(mobile))
                            throw new Exception("Name or Mobile missing");

                        var names = fullName.Split(' ', 2);
                        string fName = names[0];
                        string lName = names.Length > 1 ? names[1] : "";

                        DateTime.TryParse(GetVal(2), out DateTime dob);
                        DateTime.TryParse(GetVal(15), out DateTime doj);
                        if (dob == DateTime.MinValue) dob = new DateTime(1990, 1, 1);
                        if (doj == DateTime.MinValue) doj = DateTime.Now;

                        if (!decimal.TryParse(GetVal(11), out decimal salary))
                            throw new Exception("Invalid Basic Salary");

                        string payGrade = GetPayGradeFromSalary(salary);

                        var emp = new Employee
                        {
                            FirstName = fName,
                            LastName = lName,
                            FatherName = GetVal(1),
                            DateOfBirth = dob,
                            AadharNumber = GetVal(3),
                            PanNumber = GetVal(4),
                            Address = GetVal(5),
                            PhoneNumber = mobile,
                            Category = GetVal(7),
                            Qualification = GetVal(8),
                            Designation = GetVal(9),
                            Department = GetVal(10),
                            BaseSalary = salary,
                            BankAccountNo = GetVal(12),
                            IfscCode = GetVal(13),
                            UanNumber = GetVal(14),
                            JoiningDate = doj,
                            MaritalStatus = GetVal(16),
                            PayGrade = payGrade,
                            StaffType = "Teaching",
                            Gender = "Male",
                            Email = $"{mobile}@temp.local",
                            Photo = GetDefaultAvatar(),
                            IsActive = true
                        };

                        _payrollService.AddEmployee(emp);
                        successCount++;
                    }
                    catch (Exception rowEx)
                    {
                        var realError = rowEx.InnerException?.Message ?? rowEx.Message;
                        failedRows.Add((i + 1, realError));
                    }
                }

                if (failedRows.Any())
                {
                    string errorFile = CreateErrorReport(failedRows);
                    MessageBox.Show($"Imported {successCount} staff.\nFailed rows: {failedRows.Count}\n\nError file saved at:\n{errorFile}");
                }
                else
                {
                    MessageBox.Show($"Successfully imported {successCount} staff members!");
                    GoBack();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.InnerException?.Message ?? ex.Message}");
            }
            finally
            {
                IsImporting = false;
            }
        }

        private string GetPayGradeFromSalary(decimal salary)
        {
            if (salary < 20000) return "PG-A";
            if (salary < 40000) return "PG-B";
            if (salary < 60000) return "PG-C";
            return "PG-D";
        }

        private string CreateErrorReport(List<(int RowNumber, string Reason)> errors)
        {
            var wb = new OfficeOpenXml.ExcelPackage();
            var ws = wb.Workbook.Worksheets.Add("Import Errors");
            ws.Cells[1, 1].Value = "Row Number";
            ws.Cells[1, 2].Value = "Error Reason";
            int row = 2;
            foreach (var e in errors)
            {
                ws.Cells[row, 1].Value = e.RowNumber;
                ws.Cells[row, 2].Value = e.Reason;
                row++;
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"ImportErrors_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            File.WriteAllBytes(path, wb.GetAsByteArray());
            return path;
        }

        private byte[] GetDefaultAvatar()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/SchoolFeeSystem.Presentation;component/Assets/default-avatar.png", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch
            {
                return new byte[0]; // Fallback if asset missing
            }
        }

        [RelayCommand]
        public void GoBack()
        {
            var services = ((App)Application.Current).Services;
            var directory = services.GetRequiredService<StaffDirectoryView>();
            var directoryVM = services.GetRequiredService<StaffDirectoryViewModel>();
            directoryVM.RefreshData();
            directory.DataContext = directoryVM;
            Application.Current.MainWindow.Content = directory;
        }
    }
}