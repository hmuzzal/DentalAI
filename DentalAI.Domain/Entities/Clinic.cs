using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class Clinic : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2 code, e.g. "US". Sets the default region for phone numbers.</summary>
    public string Country { get; set; } = "US";
    public int SlotIntervalMinutes { get; set; } = 15;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
