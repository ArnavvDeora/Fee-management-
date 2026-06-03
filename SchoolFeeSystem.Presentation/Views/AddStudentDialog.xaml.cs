// AddStudentDialog.xaml.cs
// ─────────────────────────────────────────────────────────────────────────────
// Code-behind for AddStudentDialog.xaml.
//
// AddStudentResult is defined in AddStudentResult.cs (same namespace).
// Do NOT redeclare it here.
// ─────────────────────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Controls;
using SchoolFeeSystem.Core.Entities;
namespace SchoolFeeSystem.Presentation.Views
{
    public partial class AddStudentDialog : Window
    {
        // Read by ClassViewModel after ShowDialog() == true
        public ViewModels.AddStudentResult Result { get; private set; }

        public AddStudentDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => TxtName.Focus();
        }

        // ── "Add Student" button ─────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text.Trim();
            string fatherName = TxtFather.Text.Trim();
            string category = (CmbCategory.SelectedItem as ComboBoxItem)
                                    ?.Content?.ToString()?.Trim() ?? "GEN";

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Full Name is required.");
                TxtName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(fatherName))
            {
                ShowError("Father's Name is required.");
                TxtFather.Focus();
                return;
            }

            // Returns 0 for blank / non-numeric input
            static decimal ParseDec(string raw)
            {
                raw = raw?.Trim().Replace("₹", "").Replace(",", "") ?? "";
                return decimal.TryParse(raw,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal v) ? v : 0m;
            }

            decimal quarterly = ParseDec(TxtQuarterly.Text);
            decimal insurance = ParseDec(TxtInsurance.Text);
            decimal redCross = ParseDec(TxtRedCross.Text);
            decimal welfare = ParseDec(TxtWelfare.Text);
            decimal activities = ParseDec(TxtStudentActivities.Text);
            decimal institutional = ParseDec(TxtInstitutional.Text);
            decimal stationary = ParseDec(TxtStationary.Text);
            decimal hostel = ParseDec(TxtHostel.Text);
            decimal prevPending = ParseDec(TxtPrevPending.Text);

            if (quarterly < 0 || insurance < 0 || redCross < 0 || welfare < 0 ||
                activities < 0 || institutional < 0 || stationary < 0 ||
                hostel < 0 || prevPending < 0)
            {
                ShowError("Fee amounts cannot be negative.");
                return;
            }

            Result = new ViewModels.AddStudentResult
            {
                Name = name,
                FatherName = fatherName,
                PhoneNumber = TxtPhone.Text.Trim(),
                Category = category,
                QuarterlyFee = quarterly,
                ComprehensiveInsurance = insurance,
                RedCrossFund = redCross,
                DevelopmentWelfare = welfare,
                StudentActivities = activities,
                InstitutionalSecurity = institutional,
                Stationary = stationary,
                Hostel = hostel,
                PreviousPending = prevPending
            };

            DialogResult = true;
        }

        // ── "Cancel" button ──────────────────────────────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ShowError(string message)
        {
            TxtError.Text = message;
            ErrorBanner.Visibility = Visibility.Visible;
        }
    }
}