using Extatic.Api.Features.Forms;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<Form> Forms => Set<Form>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
}
