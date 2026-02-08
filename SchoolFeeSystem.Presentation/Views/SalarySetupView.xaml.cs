using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SchoolFeeSystem.Core.Entities;
using SchoolFeeSystem.Presentation.ViewModels;

namespace SchoolFeeSystem.Presentation.Views
{
    public partial class SalarySetupView : UserControl
    {
        public SalarySetupView(SalarySetupViewModel viewModel)
        {
            InitializeComponent();

            // ✅ Set DataContext
            DataContext = viewModel;
        }

        /// <summary>
        /// Handle employee card click to select employee
        /// ✅ FIXED: Changed MouseButtonEventArgs to MouseEventArgs to match XAML's MouseDown event
        /// </summary>
        private void EmployeeCard_Click(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.Tag is Employee employee)
            {
                var viewModel = DataContext as SalarySetupViewModel;

                // ✅ DEBUG: Add diagnostic output
                System.Diagnostics.Debug.WriteLine($"Employee clicked: {employee.FullName}");

                if (viewModel != null)
                {
                    viewModel.SelectEmployeeCommand.Execute(employee);
                    System.Diagnostics.Debug.WriteLine("SelectEmployeeCommand executed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: ViewModel is null!");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ERROR: Border or Employee tag is null!");
            }
        }
    }
}