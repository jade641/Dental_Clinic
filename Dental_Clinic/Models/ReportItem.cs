namespace Dental_Clinic.Models
{
    public class ReportItem
    {
        public string Type { get; set; } = "";
        public string Icon { get; set; } = "";
        public string IconClass { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime GeneratedDate { get; set; } = DateTime.Now;
    }
}
