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
 currentStep = 1;
     selectedService = null;
    selectedDate = DateTime.Today;
   selectedTimeSlot = null;
        selectedDentistFilter = null;
    availableTimeSlots.Clear();
        availableDentists.Clear();
 patientName = "";
 patientEmail = "";
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
   if (selectedDentistFilter.HasValue)
  {
     return availableTimeSlots.Where(s => s.DentistID == selectedDentistFilter.Value).ToList();
 }
       return availableTimeSlots;
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
    catch (Exception ex)
     {
       errorMessage = $"Failed to create appointment: {ex.Message}";
System.Diagnostics.Debug.WriteLine($"Error confirming booking: {ex.Message}");
    }
        finally
 {
   isLoading = false;
   StateHasChanged();
}
  }

        private async Task RescheduleAppointment(int appointmentId)
   {
       // TODO: Implement reschedule functionality
       System.Diagnostics.Debug.WriteLine($"Reschedule appointment {appointmentId}");
     }

     public class CalendarDay
        {
  public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
        }
    }
}
