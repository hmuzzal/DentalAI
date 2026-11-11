using DentalAI.Application.Common.Phone;
using DentalAI.Application.DTOs.Appointments;
using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.DTOs.Doctors;
using DentalAI.Application.DTOs.Patients;
using DentalAI.Application.DTOs.Services;
using DentalAI.Application.DTOs.Users;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Mappings;

/// <summary>Entity → DTO mappings, written out by hand (no mapping library).</summary>
public static class DtoMappings
{
    public static ClinicDto ToDto(this Clinic src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Phone = src.Phone,
        Email = src.Email,
        Address = src.Address,
        TimeZone = src.TimeZone,
        Country = src.Country,
        SlotIntervalMinutes = src.SlotIntervalMinutes
    };

    public static UserProfileDto ToDto(this User src) => new()
    {
        Id = src.Id,
        ClinicId = src.ClinicId,
        FirstName = src.FirstName,
        LastName = src.LastName,
        FullName = FullName(src.FirstName, src.LastName),
        Email = src.Email,
        Role = src.Role.ToString(),
        IsActive = src.IsActive,
        ClinicName = src.Clinic?.Name,
        ClinicTimeZone = src.Clinic?.TimeZone,
        ClinicCountry = src.Clinic?.Country
    };

    public static DoctorDto ToDto(this Doctor src) => new()
    {
        Id = src.Id,
        ClinicId = src.ClinicId ?? Guid.Empty,
        FirstName = src.FirstName,
        LastName = src.LastName,
        Specialty = src.Specialty,
        Phone = src.Phone,
        Email = src.Email,
        ClinicTimeZone = src.Clinic?.TimeZone
    };

    public static ServiceDto ToDto(this Service src) => new()
    {
        Id = src.Id,
        ClinicId = src.ClinicId ?? Guid.Empty,
        Name = src.Name,
        Description = src.Description,
        Price = src.Price,
        DurationMinutes = src.DurationMinutes,
        IsActive = src.IsActive,
        BookableOnline = src.BookableOnline
    };

    public static PatientDto ToDto(this Patient src) => new()
    {
        Id = src.Id,
        ClinicId = src.ClinicId ?? Guid.Empty,
        FirstName = src.FirstName,
        LastName = src.LastName,
        FullName = FullName(src.FirstName, src.LastName),
        Phone = src.Phone,
        PhoneNeedsReview = !PhoneNumberRules.IsStoredCorrectly(src.Phone),
        Email = src.Email,
        DateOfBirth = src.DateOfBirth,
        Gender = src.Gender,
        Notes = src.Notes,
        AddressLine1 = src.AddressLine1,
        AddressLine2 = src.AddressLine2,
        City = src.City,
        State = src.State,
        PostalCode = src.PostalCode,
        PreferredContactMethod = src.PreferredContactMethod,
        SmsOptIn = src.SmsOptIn,
        SmsOptInAtUtc = src.SmsOptInAtUtc,
        SmsOptOutAtUtc = src.SmsOptOutAtUtc,
        // Rows removed in this request are soft-deleted at save time but still in the collection.
        Insurances = src.Insurances.Where(i => !i.IsDeleted).OrderBy(i => i.Priority).Select(ToDto).ToList()
    };

    public static PatientInsuranceDto ToDto(this PatientInsurance src) => new()
    {
        Id = src.Id,
        Priority = src.Priority,
        CarrierName = src.CarrierName,
        MemberId = src.MemberId,
        GroupNumber = src.GroupNumber,
        RelationshipToPatient = src.RelationshipToPatient,
        SubscriberName = src.SubscriberName,
        SubscriberDateOfBirth = src.SubscriberDateOfBirth
    };

    public static AppointmentDto ToDto(this Appointment src) => new()
    {
        Id = src.Id,
        ClinicId = src.ClinicId,
        DoctorId = src.DoctorId,
        PatientId = src.PatientId,
        ServiceId = src.ServiceId,
        DoctorName = src.Doctor is { } d ? FullName(d.FirstName, d.LastName) : string.Empty,
        PatientName = src.Patient is { } p ? FullName(p.FirstName, p.LastName) : string.Empty,
        ServiceName = src.Service?.Name ?? string.Empty,
        StartTime = src.StartTime,
        EndTime = src.EndTime,
        Notes = src.Notes,
        Status = src.Status,
        Source = src.Source,
        // A just-created appointment has its doctor (with clinic) loaded but not its own Clinic.
        ClinicTimeZone = src.Clinic?.TimeZone ?? src.Doctor?.Clinic?.TimeZone
    };

    private static string FullName(string first, string last) => $"{first} {last}".Trim();
}
