using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolFeeSystem.Core.Entities
{
    /// <summary>
    /// Stores biometric machine entries that could NOT be automatically matched to
    /// an employee in the SS Master during attendance import.
    ///
    /// The admin can open the "Unmatched Biometrics" tab in Staff Directory,
    /// pick the correct employee from a searchable dropdown, and click "Link" —
    /// this writes the BiometricId to Employee.BiometricId so all future imports
    /// match by ID and the entry is never flagged again.
    ///
    /// MIGRATION REQUIRED:
    ///   Add-Migration AddFlaggedBiometricEntry
    ///   Update-Database
    ///
    /// ALSO add to AppDbContext:
    ///   public DbSet<FlaggedBiometricEntry> FlaggedBiometricEntries { get; set; }
    /// </summary>
    public class FlaggedBiometricEntry
    {
        [Key]
        public int Id { get; set; }

        // ── From the biometric machine file ──────────────────────────────────

        /// <summary>BioID as seen in the file (e.g. "CIHT012", "117").</summary>
        [Required]
        public string BiometricId { get; set; } = "";

        /// <summary>Name as seen in the file (e.g. "Mrs Ritu Goyal").</summary>
        [Required]
        public string BiometricName { get; set; } = "";

        /// <summary>"FACE_ATTENDANCE", "WORK_DURATION_REPORT", or "DETAILED_REPORT".</summary>
        public string SourceFormat { get; set; } = "";

        // ── Resolution ───────────────────────────────────────────────────────

        /// <summary>False = pending action; True = admin linked or dismissed.</summary>
        public bool IsResolved { get; set; } = false;

        /// <summary>Employee.Id this was linked to. Null = dismissed (ex-employee / not in org).</summary>
        public int? ResolvedToEmployeeId { get; set; }

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime FirstSeenOn { get; set; } = DateTime.Now;
        public DateTime? ResolvedOn { get; set; }

        // ── Display helpers for XAML bindings ────────────────────────────────
        public string StatusText => IsResolved ? "Linked" : "Unmatched";
        public string StatusColor => IsResolved ? "#27AE60" : "#E67E22";
        public string DisplayLabel => $"{BiometricName}  (BioID: {BiometricId})";
    }
}