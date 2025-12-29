using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows;

namespace SchoolFeeSystem.Presentation.ViewModels
{
    public partial class ClassViewModel : ObservableObject
    {
        private readonly IStudentService _studentService;

        [ObservableProperty]
        private string _className = string.Empty; // e.g. "8th"

        [ObservableProperty]
        private string _section = string.Empty; // e.g. "B"

        [ObservableProperty]
        private ObservableCollection<Class> _classList;

        public ClassViewModel(IStudentService studentService)
        {
            _studentService = studentService;
            LoadClasses();
        }

        private void LoadClasses()
        {
            ClassList = new ObservableCollection<Class>(_studentService.GetAllClasses());
        }

        [RelayCommand]
        public void AddClass()
        {
            if (string.IsNullOrWhiteSpace(ClassName) || string.IsNullOrWhiteSpace(Section))
            {
                MessageBox.Show("Please enter both Class Name and Section.");
                return;
            }

            var newClass = new Class
            {
                Name = ClassName,
                Section = Section
            };

            _studentService.AddClass(newClass);

            MessageBox.Show($"Class '{ClassName} - {Section}' Added Successfully!");

            // Refresh list and clear inputs
            LoadClasses();
            ClassName = string.Empty;
            Section = string.Empty;
        }
    }
}