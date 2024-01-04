using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace okta_scim_server_dotnet;

public partial class ScimDbContext : DbContext
{
    public ScimDbContext(){}
    public ScimDbContext(DbContextOptions<ScimDbContext> options) : base(options) { }

    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<Email> Emails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.UserName).IsUnique();
        modelBuilder.Entity<User>().HasData(new List<User> {
            new User { Id = 1, FirstName = "Micky", LastName = "Daldo", DisplayName = "Micky Daldo", UserName = "mdaldo@fake.domain", Active = true },
            new User { Id = 2, FirstName = "Dan", LastName = "Slem", DisplayName = "Dan Slem", UserName = "dslem@fake.domain", Active = true },
            new User { Id = 3, FirstName = "Sarika", LastName = "Mahesh", DisplayName = "Sarika Mahesh", UserName = "smahesh@fake.domain", Active = true }
        });
        modelBuilder.Entity<Email>().HasData(new List<Email> {
            new Email { Type = "work", Value="mdaldo@fake.domain", Primary = true, UserId = 1 },
            new Email { Type = "personal", Value="mdaldo@personal.domain", Primary = false, UserId = 1 },
            new Email { Type = "work", Value="smahesh@fake.domain", Primary = true, UserId = 3 }
        });
        base.OnModelCreating(modelBuilder);
    }
}

public partial class User
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string? ExternalId { get; set; }

    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? MiddleName { get; set; }
    public string DisplayName { get; set; }
    public bool Active { get; set; }

    public virtual ICollection<Email>? Emails { get; set; }
}

[PrimaryKey(nameof(Value), nameof(UserId))]
public class Email
{
    public string Type { get; set; }
    public string Value { get; set; }
    public bool Primary { get; set; }

    public int UserId { get; set; }
    public virtual User User { get; set; }
}