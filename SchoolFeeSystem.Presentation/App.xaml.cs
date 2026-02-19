using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolFeeSystem.Core.Interfaces;
using SchoolFeeSystem.Infrastructure.Data;
using SchoolFeeSystem.Infrastructure.Services;
using SchoolFeeSystem.Presentation.Services;
using SchoolFeeSystem.Presentation.ViewModels;
using SchoolFeeSystem.Presentation.Views;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace SchoolFeeSystem.Presentation
{
    public partial class App : System.Windows.Application
    {
        private ServiceProvider _serviceProvider;

        // Allow access to the current App instance and Services from anywhere
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services => _serviceProvider;

        public App()
        {
            // 1. FORCE INDIAN CURRENCY (₹) GLOBALLY
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-IN");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-IN");

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // --- Database ---
            services.AddDbContext<AppDbContext>();

            // --- Services (Logic) ---
            services.AddTransient<IAuthService, AuthService>();
            services.AddTransient<IStudentService, StudentService>();
            services.AddTransient<IFeeService, FeeService>();
            services.AddTransient<IFeeCollectionService, FeeCollectionService>();
            services.AddTransient<IReportService, ReportService>();
            services.AddTransient<IPayrollService, PayrollService>();
            services.AddTransient<IAttendanceService, AttendanceService>();
            services.AddScoped<ILeaveService, LeaveService>();
            services.AddScoped<OvertimeCalculationService>();
            services.AddSingleton<FineCalculationService>();

            // Payment Logging Service — must use a factory because the constructor
            // requires a string (logFilePath); bare AddSingleton<PaymentLogService>()
            // causes "Unable to resolve service for type 'System.String'" at startup.
            services.AddSingleton<PaymentLogService>(_ =>
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SchoolFeeSystem");
                string logPath = System.IO.Path.Combine(logDir, "payment_log.csv");
                return new PaymentLogService(logPath);
            });

            // Core Presentation Services
            services.AddSingleton<CsvDataService>();
            services.AddSingleton<PdfReportService>();

            // --- ViewModels & Views ---

            // Main Shell
            services.AddSingleton<MainWindow>();

            // Login
            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginView>();

            // Dashboard & Main Selection
            services.AddTransient<MainSelectionViewModel>();
            services.AddTransient<MainSelectionView>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<DashboardView>();
            services.AddTransient<PayrollDashboardViewModel>();
            services.AddTransient<PayrollDashboardView>();

            services.AddTransient<PaymentHistoryViewModel>();
            services.AddTransient<PaymentHistoryView>();

            services.AddTransient<FineManagementViewModel>();
            services.AddTransient<FineManagementView>();

            // Core Features
            services.AddTransient<StudentViewModel>();
            services.AddTransient<StudentView>();
            services.AddTransient<FeeViewModel>();
            services.AddTransient<FeeView>();

            // AcademicCycleService — registered BEFORE the ViewModels that depend on it
            services.AddSingleton<AcademicCycleService>(sp =>
                new AcademicCycleService(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PaymentLogService>()
                ));

            // ✅ FIX CS7036: Added missing 4th argument — FineCalculationService.
            // FeeCollectionViewModel constructor signature is:
            //   (CsvDataService, PaymentLogService, AcademicCycleService, FineCalculationService)
            // The old registration only passed 3 args, causing CS7036 at line 110.
            services.AddTransient<FeeCollectionViewModel>(sp =>
                new FeeCollectionViewModel(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PaymentLogService>(),
                    sp.GetRequiredService<AcademicCycleService>(),
                    sp.GetRequiredService<FineCalculationService>()  // ← was missing
                ));
            services.AddTransient<FeeCollectionView>();

            // ReportsViewModel requires manual factory (non-standard constructor)
            services.AddTransient<ReportsViewModel>(sp =>
                new ReportsViewModel(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PdfReportService>(),
                    sp.GetRequiredService<PaymentLogService>()
                ));
            services.AddTransient<ReportsView>();

            // ClassViewModel now accepts optional AcademicCycleService
            services.AddTransient<ClassViewModel>(sp =>
                new ClassViewModel(
                    sp.GetRequiredService<CsvDataService>(),
                    sp.GetRequiredService<PdfReportService>(),
                    sp.GetRequiredService<AcademicCycleService>()
                ));
            services.AddTransient<ClassView>();
            services.AddTransient<HelpViewModel>();
            services.AddTransient<HelpView>();

            // Scholarship
            services.AddTransient<ScholarshipViewModel>();
            services.AddTransient<ScholarshipView>();

            // Staff & Payroll Features
            services.AddTransient<StaffDirectoryViewModel>();
            services.AddTransient<StaffDirectoryView>();
            services.AddTransient<AddStaffViewModel>();
            services.AddTransient<AddStaffView>();

            // Staff Details (Singleton for data passing between pages)
            services.AddSingleton<StaffDetailsViewModel>();
            services.AddSingleton<StaffDetailsView>();

            services.AddTransient<SalarySetupViewModel>();
            services.AddTransient<SalarySetupView>();

            services.AddTransient<AttendanceManagementViewModel>();
            services.AddTransient<AttendanceManagementView>();
            services.AddTransient<EditAttendanceViewModel>();

            services.AddTransient<HolidayManagementViewModel>();
            services.AddTransient<HolidayManagementView>();

            services.AddTransient<ProcessPayrollViewModel>();
            services.AddTransient<ProcessPayrollView>();
            services.AddTransient<SalarySettingsViewModel>();
            services.AddTransient<SalarySettingsView>();
            services.AddTransient<PayrollReportsViewModel>();
            services.AddTransient<PayrollReportsView>();

            // Popups
            services.AddTransient<PayslipViewerViewModel>();
            services.AddTransient<PayslipViewerView>();
            services.AddTransient<ImportHolidaysViewModel>();
            services.AddTransient<ImportHolidaysView>();

            // Allowance Time
            services.AddTransient<AllowanceTimeViewModel>();
            services.AddTransient<AllowanceTimeView>();
            services.AddScoped<ICompanyGatePassService, CompanyGatePassService>();

            // Leave Management
            services.AddTransient<LeaveManagementViewModel>();
            services.AddTransient<LeaveManagementView>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize database in background to avoid blocking UI
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var scope = Services.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Database.Migrate();
                        db.EnsureDefaultDataExists();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ DB Init error: {ex.Message}");
                }
            });

            // Create ONLY ONE window, configured for login
            var mainWindow = Services.GetRequiredService<MainWindow>();
            var loginView = Services.GetRequiredService<LoginView>();

            mainWindow.Content = loginView;
            mainWindow.Width = 500;
            mainWindow.Height = 600;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.ResizeMode = ResizeMode.NoResize;
            mainWindow.Title = "School Management System - Login";
            mainWindow.WindowState = WindowState.Normal;

            mainWindow.Show();
            MainWindow = mainWindow;
        }
    }
}