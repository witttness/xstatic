using Extatic.Api.Features.Events;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
}
