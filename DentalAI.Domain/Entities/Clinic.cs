using DentalAI.Domain.Common;

namespace DentalAI.Domain.Entities;

public sealed class Clinic : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
