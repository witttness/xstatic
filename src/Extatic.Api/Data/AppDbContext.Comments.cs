using Extatic.Api.Features.Comments;
using Microsoft.EntityFrameworkCore;

namespace Extatic.Api.Data;

public partial class AppDbContext
{
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentSettings> CommentSettings => Set<CommentSettings>();
    public DbSet<CommentPage> CommentPages => Set<CommentPage>();
}
