using Extatic.Api.Features.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<BookingCalendar> BookingCalendars => Set<BookingCalendar>();
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<AvailabilityOverride> AvailabilityOverrides => Set<AvailabilityOverride>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
}
