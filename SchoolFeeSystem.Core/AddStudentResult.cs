// AddStudentResult.cs
// ─────────────────────────────────────────────────────────────────────────────
// Data-transfer object that carries the "Add Student" dialog output to both
// ClassViewModel and CsvDataService.
//
// PLACEMENT: SchoolFeeSystem.Presentation.ViewModels  (same namespace / assembly
// as ClassViewModel and FeeCollectionViewModel).  This keeps it visible to both
// the dialog code-behind AND CsvDataService after you add the using below.
//
// In CsvDataService.cs add at the top:
//   using SchoolFeeSystem.Presentation.ViewModels;
// ─────────────────────────────────────────────────────────────────────────────

namespace SchoolFeeSystem.Presentation.ViewModels
{
    /// <summary>
    /// Carries the values collected in AddStudentDialog to ClassViewModel
    /// and onward to CsvDataService.AddStudentRow().
    /// </summary>
    public class AddStudentResult
    {
        public string Name { get; set; }
        public string FatherName { get; set; }
        public string PhoneNumber { get; set; }
        public string Category { get; set; }

        public decimal QuarterlyFee { get; set; }
        public decimal ComprehensiveInsurance { get; set; }
        public decimal RedCrossFund { get; set; }
        public decimal DevelopmentWelfare { get; set; }
        public decimal StudentActivities { get; set; }
        public decimal InstitutionalSecurity { get; set; }
        public decimal Stationary { get; set; }
        public decimal Hostel { get; set; }
        public decimal PreviousPending { get; set; }
    }
}