using AutoMapper;
using DentalAI.Application.DTOs.Appointments;
using DentalAI.Application.DTOs.Clinics;
using DentalAI.Application.DTOs.Doctors;
using DentalAI.Application.DTOs.Patients;
using DentalAI.Application.DTOs.Services;
using DentalAI.Application.DTOs.Users;
using DentalAI.Domain.Entities;

namespace DentalAI.Application.Mappings;

public sealed class ApplicationMappingProfile : Profile
{
    public ApplicationMappingProfile()
    {
        CreateMap<Clinic, ClinicDto>();

        CreateMap<User, UserProfileDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}".Trim()))
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()))
            .ForMember(dest => dest.ClinicName, opt => opt.MapFrom(src => src.Clinic != null ? src.Clinic.Name : null))
            .ForMember(dest => dest.ClinicTimeZone, opt => opt.MapFrom(src => src.Clinic != null ? src.Clinic.TimeZone : null));

        CreateMap<Doctor, DoctorDto>();

        CreateMap<DentalAI.Domain.Entities.Service, ServiceDto>();

        CreateMap<Patient, PatientDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}".Trim()));

        CreateMap<Appointment, AppointmentDto>()
            .ForMember(dest => dest.PatientName, opt => opt.MapFrom(src =>
                src.Patient != null ? $"{src.Patient.FirstName} {src.Patient.LastName}".Trim() : string.Empty))
            .ForMember(dest => dest.DoctorName, opt => opt.MapFrom(src =>
                src.Doctor != null ? $"{src.Doctor.FirstName} {src.Doctor.LastName}".Trim() : string.Empty))
            .ForMember(dest => dest.ServiceName, opt => opt.MapFrom(src =>
                src.Service != null ? src.Service.Name : string.Empty));
    }
}
