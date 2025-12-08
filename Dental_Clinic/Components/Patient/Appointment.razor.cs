using Microsoft.AspNetCore.Components;
using Dental_Clinic.Models;
using Dental_Clinic.Services;

namespace Dental_Clinic.Components.Patient
{
   public partial class Appointment
   {
      [Inject] private AppointmentService AppointmentService { get; set; } = default!;
      [Inject] private SessionService SessionService { get; set; } = default!;
      [Inject] private NavigationManager Navigation { get; set; } = default!;

      private List<Models.Appointment> appointments = new();
      private bool showBookingModal = false;
      private int currentStep = 1;
      private bool isLoading = false;

      private List<Service> services = new();
      private Service? selectedService;

      private DateTime currentMonth = DateTime.Today;
      private DateTime selectedDate = DateTime.Today;
      private TimeSlotModel? selectedTimeSlot;
      private List<TimeSlotModel> availableTimeSlots = new();
      private List<CalendarDay> calendarDays = new();
      private List<DentistAvailability> availableDentists = new();
      private int? selectedDentistFilter = null;

      private string patientName = "";
      private string patientEmail = "";
      private string patientPhone = "";
      private string appointmentNotes = "";
      private int currentPatientId = 0;
      private string errorMessage = "";
      private string successMessage = "";
      private int? reschedulingAppointmentId = null;
      private string _defaultPatientName = "";
      private string _defaultPatientEmail = "";

      // Navbar state
      private bool showSettings = false;
      private bool appointmentReminders = true;

      protected override async Task OnInitializedAsync()
      {
         try
         {
            // Get current user session
            var session = await SessionService.GetCurrentSessionAsync();
            if (session == null || session.Role != "Patient")
            {
               Navigation.NavigateTo("/login", true);
               return;
            }

            currentPatientId = session.RoleSpecificId ?? 0;
            if (currentPatientId == 0)
            {
               Navigation.NavigateTo("/login", true);
               return;
            }

            // set patient display name for navbar and form defaults
            patientName = string.IsNullOrWhiteSpace(session.UserName) ? "Patient" : session.UserName!;
            patientEmail = await AppointmentService.GetPatientEmailAsync(currentPatientId);

            _defaultPatientName = patientName;
            _defaultPatientEmail = patientEmail;

            // Load patient appointments
            await LoadAppointmentsAsync();
         }
         catch (Exception ex)
         {
            System.Diagnostics.Debug.WriteLine($"Error initializing appointments: {ex.Message}");
            Navigation.NavigateTo("/login", true);
         }
      }

      private async Task LoadAppointmentsAsync()
      {
         try
         {
            isLoading = true;
            appointments = await AppointmentService.GetPatientAppointmentsAsync(currentPatientId);
         }
         catch (Exception ex)
         {
            System.Diagnostics.Debug.WriteLine($"Error loading appointments: {ex.Message}");
         }
         finally
         {
            isLoading = false;
            StateHasChanged();
         }
      }

      private async Task BookNewAppointment()
      {
         showBookingModal = true;
         currentStep = 1;
         ResetBookingData();

         // Load services from database
         await LoadServicesAsync();
      }

      private async Task LoadServicesAsync()
      {
         try
         {
            isLoading = true;
            errorMessage = "";
            System.Diagnostics.Debug.WriteLine("[Appointment.razor] Loading services...");

            services = await AppointmentService.GetAllServicesAsync();

            System.Diagnostics.Debug.WriteLine($"[Appointment.razor] Loaded {services.Count} services");

            if (services.Count == 0)
            {
               errorMessage = "No services available. Please contact the clinic.";
               System.Diagnostics.Debug.WriteLine("[Appointment.razor] WARNING: No services found!");
            }
         }
         catch (Exception ex)
         {
            errorMessage = $"Failed to load services: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Appointment.razor] ERROR loading services: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Appointment.razor] Stack trace: {ex.StackTrace}");
         }
         finally
         {
            isLoading = false;
            StateHasChanged();
         }
      }

      private void CloseBookingModal()
      {
         showBookingModal = false;
         ResetBookingData();
      }

      private void ResetBookingData()
      {
         reschedulingAppointmentId = null;
         currentStep = 1;
         selectedService = null;
         selectedDate = DateTime.Today;
         selectedTimeSlot = null;
         selectedDentistFilter = null;
         availableTimeSlots.Clear();
         availableDentists.Clear();
         patientName = _defaultPatientName;
         patientEmail = _defaultPatientEmail;
         patientPhone = "";
         appointmentNotes = "";
      }

      private void SelectService(Service service)
      {
         selectedService = service;
      }

      private async Task SelectDate(DateTime date)
      {
         if (date >= DateTime.Today)
         {
            selectedDate = date;
            selectedTimeSlot = null;

            // Load available time slots for the selected date
            await LoadAvailableTimeSlotsAsync();
         }
      }

      private async Task LoadAvailableTimeSlotsAsync()
      {
         try
         {
            isLoading = true;

            // Load all time slots
            availableTimeSlots = await AppointmentService.GetAvailableTimeSlotsAsync(
             selectedDate,
             selectedService?.ServiceID);

            // Load available dentists for the filter
            if (availableDentists.Count == 0)
            {
               availableDentists = await AppointmentService.GetAvailableDentistsAsync(selectedDate);
            }

            System.Diagnostics.Debug.WriteLine($"[Appointment.razor] Loaded {availableTimeSlots.Count} time slots");
         }
         catch (Exception ex)
         {
            System.Diagnostics.Debug.WriteLine($"Error loading time slots: {ex.Message}");
         }
         finally
         {
            isLoading = false;
            StateHasChanged();
         }
      }

      private void SelectTimeSlot(TimeSlotModel slot)
      {
         if (slot.IsAvailable)
         {
            selectedTimeSlot = slot;
            StateHasChanged();
         }
      }

      private void FilterByDentist(int? dentistId)
      {
         selectedDentistFilter = dentistId;
         selectedTimeSlot = null;
         StateHasChanged();
      }

      private List<TimeSlotModel> GetFilteredTimeSlots()
      {
         IEnumerable<TimeSlotModel> query = availableTimeSlots;

         // Disallow booking past times on the current day
         if (selectedDate.Date == DateTime.Today)
         {
            var now = DateTime.Now.TimeOfDay;
            query = query.Where(s => s.StartTime > now);
         }

         if (selectedDentistFilter.HasValue)
         {
            query = query.Where(s => s.DentistID == selectedDentistFilter.Value);
         }
         return query.OrderBy(s => s.StartTime).ToList();
      }

      private void PreviousMonth()
      {
         currentMonth = currentMonth.AddMonths(-1);
         GenerateCalendarDays();
      }

      private void NextMonth()
      {
         currentMonth = currentMonth.AddMonths(1);
         GenerateCalendarDays();
      }

      private List<CalendarDay> GetCalendarDays()
      {
         // Only regenerate calendar if it's empty OR if we're viewing a different month
         if (calendarDays.Count == 0 ||
              (calendarDays.Count > 0 && calendarDays[15].Date.Month != currentMonth.Month))
         {
            System.Diagnostics.Debug.WriteLine($"[Appointment.razor] Regenerating calendar for {currentMonth:MMMM yyyy}");
            GenerateCalendarDays();
         }
         return calendarDays;
      }

      private void GenerateCalendarDays()
      {
         calendarDays.Clear();

         var firstDay = new DateTime(currentMonth.Year, currentMonth.Month, 1);
         var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);

         // Always generate exactly 42 days (6 weeks) for consistent layout
         for (int i = 0; i < 42; i++)
         {
            var date = startDate.AddDays(i);
            calendarDays.Add(new CalendarDay
            {
               Date = date,
               IsCurrentMonth = date.Month == currentMonth.Month
            });
         }

         System.Diagnostics.Debug.WriteLine($"[Appointment.razor] Generated {calendarDays.Count} calendar days for {currentMonth:MMMM yyyy}");
      }

      private bool CanProceed()
      {
         return currentStep switch
         {
            1 => selectedService != null,
            2 => selectedTimeSlot != null && selectedTimeSlot.IsAvailable,
            3 => !string.IsNullOrEmpty(patientName) &&
          !string.IsNullOrEmpty(patientEmail) &&
          !string.IsNullOrEmpty(patientPhone),
            _ => false
         };
      }

      private void HandlePrevious()
      {
         if (currentStep == 1)
         {
            CloseBookingModal();
         }
         else
         {
            currentStep--;
         }
      }

      private async Task HandleNext()
      {
         if (currentStep < 3)
         {
            currentStep++;

            // Load time slots when moving to step 2
            if (currentStep == 2)
            {
               await LoadAvailableTimeSlotsAsync();
            }
         }
         else
         {
            await ConfirmBooking();
         }
      }

      private async Task ConfirmBooking()
      {
         try
         {
            isLoading = true;
            errorMessage = "";

            if (reschedulingAppointmentId.HasValue)
            {
               var result = await AppointmentService.UpdateAppointmentAsync(
                   reschedulingAppointmentId.Value,
                   selectedDate,
                   selectedTimeSlot!.StartTime,
                   selectedService!.ServiceID,
                   selectedTimeSlot!.DentistID,
                   appointmentNotes
               );

               if (result)
               {
                  successMessage = "Appointment rescheduled successfully!";
                  CloseBookingModal();
                  await LoadAppointmentsAsync();
                  StateHasChanged();
               }
               else
               {
                  errorMessage = "Failed to reschedule appointment.";
               }
            }
            else
            {
               var appointmentModel = new CreateAppointmentModel
               {
                  PatientID = currentPatientId,
                  ServiceID = selectedService!.ServiceID,
                  DentistID = selectedTimeSlot!.DentistID,
                  SlotID = selectedTimeSlot!.SlotID, // Include SlotID
                  AppointmentDate = selectedDate,
                  StartTime = selectedTimeSlot.StartTime,
                  EndTime = selectedTimeSlot.EndTime,
                  Notes = appointmentNotes,
                  PatientName = patientName,
                  PatientEmail = patientEmail,
                  PatientPhone = patientPhone
               };

               var result = await AppointmentService.CreateAppointmentAsync(appointmentModel);

               if (result.Success)
               {
                  successMessage = "Appointment booked successfully!";
                  CloseBookingModal();
                  await LoadAppointmentsAsync();
                  StateHasChanged();
               }
               else
               {
                  // Show error message
                  errorMessage = result.Message;
                  System.Diagnostics.Debug.WriteLine($"Booking failed: {result.Message}");
               }
            }
         }
         catch (Exception ex)
         {
            errorMessage = $"Failed to {(reschedulingAppointmentId.HasValue ? "reschedule" : "create")} appointment: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error confirming booking: {ex.Message}");
         }
         finally
         {
            isLoading = false;
            StateHasChanged();
         }
      }

      private bool showCancelModal = false;
      private int cancelAppointmentId = 0;
      private string cancellationReason = "";

      // ...existing code...

      private void OpenCancelModal(int appointmentId)
      {
         cancelAppointmentId = appointmentId;
         cancellationReason = "";
         showCancelModal = true;
         StateHasChanged();
      }

      private void CloseCancelModal()
      {
         showCancelModal = false;
         cancelAppointmentId = 0;
         cancellationReason = "";
      }

      private async Task ConfirmCancelAsync()
      {
         if (cancelAppointmentId <= 0 || string.IsNullOrWhiteSpace(cancellationReason))
         {
            errorMessage = string.IsNullOrWhiteSpace(cancellationReason) ? "Please provide a cancellation reason." : "Invalid appointment.";
            return;
         }
         try
         {
            isLoading = true;
            var result = await AppointmentService.CancelAppointmentAsync(cancelAppointmentId, currentPatientId, cancellationReason);
            if (result.Success)
            {
               successMessage = "Appointment cancelled.";
               showCancelModal = false;
               await LoadAppointmentsAsync();
            }
            else
            {
               errorMessage = result.Message;
            }
         }
         catch (Exception ex)
         {
            errorMessage = $"Failed to cancel: {ex.Message}";
         }
         finally
         {
            isLoading = false;
            StateHasChanged();
         }
      }

      private async Task RescheduleAppointment(int appointmentId)
      {
         var appt = appointments.FirstOrDefault(a => a.AppointmentID == appointmentId);
         if (appt != null)
         {
            reschedulingAppointmentId = appointmentId;

            // Fetch full service details to ensure Duration and Cost are correct
            if (appt.ServiceID.HasValue)
            {
               selectedService = await AppointmentService.GetServiceByIdAsync(appt.ServiceID.Value);
            }

            if (selectedService == null)
            {
               selectedService = new Service { ServiceID = appt.ServiceID ?? 0, ServiceName = appt.ServiceName, Duration = 60, Cost = 0, CategoryName = "" };
            }

            selectedDate = appt.AppointmentDate;
            selectedTimeSlot = null;

            // Pre-fill patient details
            patientName = !string.IsNullOrEmpty(appt.PatientName) ? appt.PatientName : patientName;
            patientEmail = !string.IsNullOrEmpty(appt.PatientEmail) ? appt.PatientEmail : patientEmail;
            patientPhone = appt.PatientPhone;
            appointmentNotes = appt.Notes;

            showBookingModal = true;
            currentStep = 2;
            await LoadAvailableTimeSlotsAsync();
         }
      }

      // Navbar handlers
      private void ToggleSettings() => showSettings = !showSettings;
      private void EditProfile() { Navigation.NavigateTo("/patient-profile"); showSettings = false; }
      private void ToggleReminders(ChangeEventArgs e) { appointmentReminders = e.Value is bool b ? b : appointmentReminders; }
      private void MedicalUploads() { Navigation.NavigateTo("/history"); showSettings = false; }
      private void HelpSupport() { Navigation.NavigateTo("/help"); showSettings = false; }
      private void Logout() { Navigation.NavigateTo("/login", true); showSettings = false; }

      public class CalendarDay
      {
         public DateTime Date { get; set; }
         public bool IsCurrentMonth { get; set; }
      }
   }
}
