using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SchoolFeeSystem.Infrastructure.Services
{
    public class AttendanceService : IAttendanceService
    {
        private readonly AppDbContext _context;

        public AttendanceService(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // 1. SMART BIOMETRIC IMPORT (Updated for Designation & ID)
        // =========================================================
        public void ImportBiometricReport(string filePath)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                IExcelDataReader reader;
                if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    reader = ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration()
                    {
                        FallbackEncoding = System.Text.Encoding.GetEncoding(1252),
                        AutodetectSeparators = new char[] { ',', ';', '\t' }
                    });
                }
                else { reader = ExcelReaderFactory.CreateReader(stream); }

                using (reader)
                {
                    var result = reader.AsDataSet();
                    if (result.Tables.Count == 0) return;
                    var table = result.Tables[0];

                    // 1. EXTRACT MONTH & YEAR
                    int month = DateTime.Now.Month;
                    int year = DateTime.Now.Year;

                    for (int r = 0; r < 5 && r < table.Rows.Count; r++)
                    {
                        string txt = table.Rows[r][0]?.ToString() ?? "";
                        var m = Regex.Match(txt, @"(\d{1,2})\s*-\s*(\d{4})");
                        if (m.Success)
                        {
                            month = int.Parse(m.Groups[1].Value);
                            year = int.Parse(m.Groups[2].Value);
                            break;
                        }
                    }

                    // 2. FIND HEADER ROW
                    int headerRow = -1;
                    for (int r = 0; r < 20 && r < table.Rows.Count; r++)
                    {
                        var cell = table.Rows[r][0]?.ToString() ?? "";
                        if (cell.Contains("Attendance ID", StringComparison.OrdinalIgnoreCase))
                        {
                            headerRow = r;
                            break;
                        }
                    }
                    if (headerRow == -1) throw new Exception("Header 'Attendance ID' not found.");

                    // 3. PROCESS ROWS
                    var employeesCache = _context.Employees.ToList();

                    for (int i = headerRow + 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        if (row.ItemArray.Length < 4) continue;

                        string type = row[3]?.ToString() ?? "";
                        if (!type.Contains("In-Time", StringComparison.OrdinalIgnoreCase)) continue;

                        // --- READ ATTRIBUTES CORRECTLY ---
                        string bioId = row[0]?.ToString()?.Trim();
                        string rawName = row[1]?.ToString()?.Trim();
                        string designation = row[2]?.ToString()?.Trim(); // Reads Column 2

                        if (string.IsNullOrEmpty(bioId)) continue;

                        // Clean Name: "Aman Verma (111)" -> "Aman Verma"
                        string cleanName = Regex.Replace(rawName, @"\s*\(\d+\)", "").Trim();

                        // --- FIND OR CREATE STAFF ---
                        var emp = employeesCache.FirstOrDefault(e => e.BiometricId == bioId);

                        if (emp == null && !string.IsNullOrEmpty(cleanName))
                        {
                            // Try finding by name match if ID is missing
                            emp = employeesCache.FirstOrDefault(e =>
                                (e.FirstName + " " + e.LastName).Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (emp == null)
                        {
                            // CREATE NEW EMPLOYEE with Designation
                            emp = new Employee
                            {
                                FirstName = cleanName.Split(' ')[0],
                                LastName = cleanName.Contains(" ") ? cleanName.Substring(cleanName.IndexOf(" ") + 1) : "",
                                BiometricId = bioId,
                                Designation = string.IsNullOrWhiteSpace(designation) ? "Staff" : designation,
                                Department = "General",
                                StaffType = "Teaching", // Default, Admin can change later
                                IsActive = true,
                                JoiningDate = DateTime.Now,
                                Email = bioId + "@school.com",
                                PhoneNumber = "0000000000"
                            };
                            _context.Employees.Add(emp);
                            _context.SaveChanges();
                            employeesCache.Add(emp);
                        }
                        else
                        {
                            // UPDATE EXISTING EMPLOYEE DETAILS
                            // If they already exist, update the designation from the CSV to keep it fresh
                            if (!string.IsNullOrWhiteSpace(designation) && emp.Designation != designation)
                            {
                                emp.Designation = designation;
                                _context.Employees.Update(emp);
                            }
                            // Ensure Biometric ID is linked
                            if (emp.BiometricId != bioId)
                            {
                                emp.BiometricId = bioId;
                                _context.Employees.Update(emp);
                            }
                        }

                        // --- READ OUT-TIME & TOTAL-TIME ROWS ---
                        DataRow outRow = (i + 1 < table.Rows.Count) ? table.Rows[i + 1] : null;
                        DataRow totalRow = (i + 2 < table.Rows.Count) ? table.Rows[i + 2] : null;

                        // --- SAVE ATTENDANCE ---
                        for (int day = 1; day <= 31; day++)
                        {
                            int colIndex = 3 + day;
                            if (colIndex >= table.Columns.Count) break;
                            if (day > DateTime.DaysInMonth(year, month)) break;

                            string inTime = row[colIndex]?.ToString()?.Trim();
                            string outTime = (outRow != null) ? outRow[colIndex]?.ToString()?.Trim() : "00:00";
                            string duration = (totalRow != null) ? totalRow[colIndex]?.ToString()?.Trim() : "00:00";

                            // Normalize data
                            if (inTime == "0" || string.IsNullOrWhiteSpace(inTime)) inTime = "00:00";
                            if (outTime == "0" || string.IsNullOrWhiteSpace(outTime)) outTime = "00:00";
                            if (duration == "0" || string.IsNullOrWhiteSpace(duration)) duration = "00:00";

                            // Mark Present if InTime exists
                            if (inTime != "00:00")
                            {
                                DateTime date = new DateTime(year, month, day);
                                var record = _context.AttendanceRecords
                                    .FirstOrDefault(a => a.EmployeeId == emp.Id && a.Date == date);

                                if (record == null)
                                {
                                    _context.AttendanceRecords.Add(new AttendanceRecord
                                    {
                                        EmployeeId = emp.Id,
                                        Date = date,
                                        Status = "Present",
                                        InTime = inTime,
                                        OutTime = outTime,
                                        Duration = duration
                                    });
                                }
                                else
                                {
                                    record.Status = "Present";
                                    record.InTime = inTime;
                                    record.OutTime = outTime;
                                    record.Duration = duration;
                                    _context.AttendanceRecords.Update(record);
                                }
                            }
                        }
                    }
                    _context.SaveChanges();
                }
            }
        }

        // =========================================================
        // 2. STANDARD METHODS (Unchanged)
        // =========================================================

        public List<AttendanceRecord> GetAttendance(int month, int year, int? employeeId = null)
        {
            var query = _context.AttendanceRecords
                .Include(a => a.Employee)
                .AsQueryable();

            if (employeeId.HasValue && employeeId.Value > 0)
                query = query.Where(a => a.EmployeeId == employeeId);

            if (month > 0 && year > 0)
                query = query.Where(a => a.Date.Month == month && a.Date.Year == year);

            return query.OrderByDescending(a => a.Date).ToList();
        }

        public void MarkAttendance(AttendanceRecord record)
        {
            var existing = _context.AttendanceRecords
                .FirstOrDefault(a => a.EmployeeId == record.EmployeeId && a.Date == record.Date);

            if (existing != null)
            {
                existing.Status = record.Status;
                existing.InTime = record.InTime;
                existing.OutTime = record.OutTime;
                existing.Duration = record.Duration;
                _context.AttendanceRecords.Update(existing);
            }
            else
            {
                _context.AttendanceRecords.Add(record);
            }
            _context.SaveChanges();
        }

        public void BulkMarkAttendance(List<AttendanceRecord> records)
        {
            foreach (var r in records) MarkAttendance(r);
        }

        public List<Holiday> GetHolidays(int year)
        {
            return _context.Holidays
                .Where(h => h.Date.Year == year)
                .OrderBy(h => h.Date)
                .ToList();
        }

        public void AddHoliday(Holiday holiday)
        {
            if (!_context.Holidays.Any(h => h.Date == holiday.Date))
            {
                _context.Holidays.Add(holiday);
                _context.SaveChanges();
            }
        }

        public void DeleteHoliday(int id)
        {
            var item = _context.Holidays.Find(id);
            if (item != null)
            {
                _context.Holidays.Remove(item);
                _context.SaveChanges();
            }
        }

        public void ImportHolidays(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length >= 2 && DateTime.TryParse(cols[0], out DateTime date))
                {
                    AddHoliday(new Holiday { Date = date, Name = cols[1].Trim(), IsRecurring = true });
                }
            }
        }

        public void ImportAttendance(string filePath) => ImportBiometricReport(filePath);
        public List<AttendanceRecord> GetRecords(int employeeId, int month, int year) => GetAttendance(month, year, employeeId);
        public void UpdateRecord(AttendanceRecord record) => MarkAttendance(record);

        public AttendanceSettings GetSettings()
        {
            var s = _context.AttendanceSettings.FirstOrDefault();
            if (s == null) { s = new AttendanceSettings(); _context.AttendanceSettings.Add(s); _context.SaveChanges(); }
            return s;
        }
        public void SaveSettings(AttendanceSettings s) { _context.AttendanceSettings.Update(s); _context.SaveChanges(); }

        IEnumerable<AttendanceRecord> IAttendanceService.GetRecords(int id, int month, int year)
        {
            return GetRecords(id, month, year);
        }
    }
}