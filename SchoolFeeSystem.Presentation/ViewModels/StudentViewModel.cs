using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Presentation.Views;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ClassViewModel : ObservableObject
    {
        [RelayCommand]
        public void GoBack4()
        {
            var dashboard = App.Current.Services.GetRequiredService<DashboardView>();
            Application.Current.MainWindow.Content = dashboard;
        }
    }
}


namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class StudentViewModel : ObservableObject
    {
        private readonly IStudentService _studentService;
        private List<Class> _allClassesCache; // Store all classes in memory to search quickly

        // The main student list (Displayed on right)
        [ObservableProperty]
        private ObservableCollection<Student> _students;

        // Dropdown Lists
        public List<string> Standards { get; } = new() { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th", "10th", "11th", "12th" };
        public List<string> Sections { get; } = new() { "A", "B", "C", "D", "E" };

        // User Selections
        [ObservableProperty]
        private string _selectedStandard;

        [ObservableProperty]
        private string _selectedSection;

        // New Student Form Data
        [ObservableProperty]
        private Student _newStudent = new Student();

        public StudentViewModel(IStudentService studentService)
        {
            _studentService = studentService;
            LoadData();
        }

        private void LoadData()
        {
            _allClassesCache = _studentService.GetAllClasses();

            // Sort students Alphabetically by Name
            var sortedStudents = _studentService.GetAllStudents().OrderBy(s => s.FullName);
            Students = new ObservableCollection<Student>(sortedStudents);
        }

        [RelayCommand]
        public void AddStudent()
        {
            // 1. Validate Dropdowns
            if (string.IsNullOrEmpty(SelectedStandard) || string.IsNullOrEmpty(SelectedSection))
            {
                MessageBox.Show("Please select both Class (Standard) and Section.");
                return;
            }

            // 2. Validate Inputs
            if (string.IsNullOrWhiteSpace(NewStudent.FullName) || string.IsNullOrWhiteSpace(NewStudent.ContactNumber))
            {
                MessageBox.Show("Name and Contact Number are required.");
                return;
            }

            // 3. Find the Class ID based on selection
            var targetClass = _allClassesCache.FirstOrDefault(c => c.Name == SelectedStandard && c.Section == SelectedSection);

            if (targetClass == null)
            {
                MessageBox.Show($"Class '{SelectedStandard} - {SelectedSection}' does not exist in database.");
                return;
            }

            // 4. Prepare & Save
            NewStudent.ClassId = targetClass.Id;
            NewStudent.IsActive = true;

            _studentService.AddStudent(NewStudent);

            // 5. Refresh List (Sorted) & Reset Fields
            LoadData();

            // Reset Name/Phone but KEEP Standard/Section selected for fast entry!
            NewStudent = new Student();

            // Optional: Remove this if you want to be even faster
            // MessageBox.Show("Student Added!"); 
        }
    }
}