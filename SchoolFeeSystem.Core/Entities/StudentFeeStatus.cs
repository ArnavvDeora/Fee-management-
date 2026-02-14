using System;

namespace SchoolFeeSystem.Presentation.Entities
{
    /// <summary>
    /// Enhanced fee status tracking for students
    /// </summary>
    public enum FeeStatusLevel
    {
        NoPending = 0,          // White - No fees pending at all
        CurrentQuarterDue = 1,  // Yellow - Only current quarter pending
        PreviousQuarterDue = 2  // Red - Previous quarter(s) pending
    }

    public class StudentFeeStatus
    {
        public string StudentName { get; set; }
        public string PhoneNumber { get; set; }
        public decimal PreviousQuarterPending { get; set; }
        public decimal CurrentQuarterFee { get; set; }
        public decimal TotalPending { get; set; }
        public FeeStatusLevel StatusLevel { get; set; }
        public string StatusColor { get; set; }
        public string StatusDescription { get; set; }

        public StudentFeeStatus()
        {
            CalculateStatus();
        }

        public void CalculateStatus()
        {
            TotalPending = PreviousQuarterPending + CurrentQuarterFee;

            if (PreviousQuarterPending > 0)
            {
                // Has old pending fees
                StatusLevel = FeeStatusLevel.PreviousQuarterDue;
                StatusColor = "#FFCDD2"; // Light Red
                StatusDescription = "⚠️ Previous Quarter Pending";
            }
            else if (CurrentQuarterFee > 0)
            {
                // Only current quarter pending
                StatusLevel = FeeStatusLevel.CurrentQuarterDue;
                StatusColor = "#FFF9C4"; // Light Yellow
                StatusDescription = "📅 Current Quarter Due";
            }
            else
            {
                // No fees pending
                StatusLevel = FeeStatusLevel.NoPending;
                StatusColor = "#FFFFFF"; // White
                StatusDescription = "✅ No Fees Pending";
            }
        }
    }
}