using Extatic.Api.Features.Members;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MemberSettings> MemberSettings => Set<MemberSettings>();
    public DbSet<MembershipEvent> MembershipEvents => Set<MembershipEvent>();
}
