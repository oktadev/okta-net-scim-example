# Integrate with Okta Using SCIM and dotnet

## What is SCIM?
SCIM, the System for Cross-domain Identity Management is an open standard protocol that allows us to manage identities across systems as well as common user lifecycles. This is a HTTP-based protocal which defines a set of standard endpoints and schemas which can be used to overcome complexity in implementing user management operations across different systems.

## Components of SCIM
There are two major components for a SCIM integration,
- *SCIM Server* - A server which has implemented endpoints as described in SCIM spec. For example, User endpoints, Group endpoints, Schemas endpoints, etc. In our use case, this is developed by the application team(s) where the user profiles are expected to be mastered by Okta. We will go into detail on creating a sample scim server using dotnet
- *SCIM Client* - A service which makes SCIM compliant HTTP calls to a SCIM server to exchange user profile information. In our use case, Okta will act as a client and make calls to the SCIM server we create and configure.

## Preparing SCIM Server

### Prerequisites
- dotnet SDK (I used [dotnet 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for demo)
- Code Editor (I used Visual Studio Code and CLI) 
- Okta Tenant - Signup [here](https://developer.okta.com/signup/)

### Create dotnet Project
- Create a new folder named *okta-scim-server-dotnet*
- Open terminal and make this as current folder using `cd okta-scim-server-dotnet` command
- Create new API project using `dotnet new webapi`
- Add dotnet *.gitignore* file using standard options (You can use [this](https://github.com/ramgandhi-okta/okta-scim-server-dotnet/blob/main/.gitignore))
- Trust self signed TLS certs using `dotnet dev-certs https --trust`

### Test Project Setup
- Run project using `dotnet watch --launch-profile https`
- At this point using the *https://localhost:[port]/swagger/index.html* you will be able to see swagger UI (Typically a browser tab opens automatically, if not check url in Properties/launchSettings.json)

## Create Data Modles
- Add required dependencies for ORM by running following commands
    - `dotnet tool install --global dotnet-ef`
    - `dotnet add package Microsoft.EntityFrameworkCore.Tools`
    - `dotnet add package Microsoft.EntityFrameworkCore.Design`
    - `dotnet add package Microsoft.EntityFrameworkCore.Sqlite`
- Create `DataModels.cs` file and add required Model classes
    ```c#
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;

    namespace okta_scim_server_dotnet;

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
    ```
- Add DB context for entity framework in `DataModels.cs` with some seed data at the bottom add the following code
    ```c#
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
    ```
- Add dbconfiguration in `appsettings.json` file as a top level property. This creates a db file in same folder
    ```json
    "ConnectionStrings": {
        "DefaultConnection": "Data Source=scim-dev.db;"
    }
    ```
- Add DI in `Program.cs`
    - At the top add the `using` statements
        ```c#
        using Microsoft.EntityFrameworkCore;
        using okta_scim_server_dotnet;
        ```
    - After `builder.Services.AddSwaggerGen();` add the following code
        ```c#
        builder.Services.AddDbContextPool<ScimDbContext>(
            options => options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
        );
        ```
- Add migration of this initial database by running `dotnet ef migrations add InitialScimDb`
- Apply these changes to db by running `dotnet ef database update`
- *Optional:* Test db creation using command line tool, 
    - You should have sqllite3 client installed. (I had this OOB in mac OS)
    - Connect using`sqlite3 <<Path to sqlite file>>/scim-dev.db`
    - List tables using `.tables`
    - Then exit using `.exit`

### Create SCIM models and mapping
- Create new file `ScimModles.cs` and add the following SCIM models
    ```c#
    namespace okta_scim_server_dotnet;

    public class ScimListResourceRequest
    {
        public string? filter { get; set; }
        public int? startIndex { get; set; }
        public int? count { get; set; }
        // TODO: Starting with simple parsing on what okta sends. Extend it to be generic to handle other operations
        public Dictionary<string, string> parsedFilter
        {
            get
            {
                Dictionary<string, string> parsedValue = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    var filterTerms = filter.Split(" eq ");
                    if (filterTerms.Length == 2)
                    {
                        parsedValue.Add(filterTerms[0], filterTerms[1].Substring(1, filterTerms[1].Length - 2));
                    }
                }
                return parsedValue;
            }
        }
        public int parsedStartIndex { get { return startIndex ?? 1; } }
        public int parsedCount { get { return count ?? 100; } }
    }

    public class ScimListResourceResponse<T>
    {
        public IEnumerable<string> schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" };
        public int totalResults { get; set; }
        public int startIndex { get; set; }
        public int itemsPerPage { get; set; }
        public IEnumerable<T> Resources { get; set; }
    }

    public class ScimErrorResponse
    {
        public ScimErrorResponse(int status, string detail)
        {
            this.schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" };
            this.status = status;
            this.detail = detail;
        }
        public IEnumerable<string> schemas {get; private set;}
        public string? detail { get; set; }
        public int status { get; set; }
    }
    public class ScimUser
    {
        public IEnumerable<string> schemas { get; set; }
        public string? id { get; set; }
        public string externalId { get; set; }
        public string userName { get; set; }
        public ScimName name { get; set; }
        public string displayName { get; set; }
        public IEnumerable<ScimEmail> emails { get; set; }
        public bool active { get; set; }
    }

    public class ScimName
    {
        public string givenName { get; set; }
        public string familyName { get; set; }
        public string? middleName { get; set; }
    }

    public class ScimEmail
    {
        public string value { get; set; }
        public string type { get; set; }
        public bool primary { get; set; }
    }
    ```
- Add Mappers between SCIM and DB models
    - Install dependencies by running following commands
        - `dotnet add package AutoMapper`
        - `dotnet add package Automapper.Extensions.Microsoft.DependencyInjection`
    - Add Mappings to `ScimModels.cs` 
        - At the top add the `using` statement
            ```c#
            using AutoMapper;
            ```
        - At the bottom add the following code
            ```c#
            public class UserProfile: Profile
            {
                public UserProfile()
                {
                    CreateMap<ScimUser, User>()
                        .ForMember(dest => dest.FirstName, act => act.MapFrom(src => src.name.givenName))
                        .ForMember(dest => dest.LastName, act => act.MapFrom(src => src.name.familyName))
                        .ForMember(dest => dest.MiddleName, act => act.MapFrom(src => src.name.middleName))
                        .ReverseMap()
                        .ForPath(dest => dest.id, act => act.MapFrom(src => src.Id))
                        .ForPath(dest => dest.schemas, act => act.MapFrom(src => new[] { "urn:ietf:params:scim:schemas:core:2.0:User" }));

                    CreateMap<ScimEmail, Email>().ReverseMap();
                }
            }
            ```
    - Add DI in `Program.cs`
        - At the top add the `using` statements
            ```c#
            using AutoMapper;
            ```
        - After `builder.Services.AddDbContextPool<ScimDbContext>(...);` add the following code
            ```c#
            builder.Services.AddAutoMapper(typeof(Program).Assembly);
            ```

## Create User SCIM Endpoints
- Install dependencies by running `dotnet add package Newtonsoft.Json`
- At the top add `using` statements
    ```c#
    using System.Text.Json.Serialization;
    using System.Text.Json;
    using System.Net;
    using Microsoft.AspNetCore.Mvc;
    ```
- Add the following code after `var builder = WebApplication.CreateBuilder(args);` to respond cleanly and overcome parsing limiations
    ```c#
    builder.Services.ConfigureHttpJsonOptions(options => {
        options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
    ```
- *Optional* - Remove WeatherForecast related sample code
- Add following code before `app.run()` for List Users, Get User, Create User, Update User, Deactivate User (Okta does not send HTTP Delete calls) endpoints
    ```c#
    var scimPrefix = "/scim/v2";
    var userRoute = $"{scimPrefix}/users";
    var notFoundResponse = new ScimErrorResponse((int)HttpStatusCode.NotFound, "Resource Not Found");
    app.MapGet(userRoute, async ([AsParameters] ScimListResourceRequest request, ScimDbContext db, IMapper mapper) => {
        string filterUsername = request.parsedFilter.Where(f => f.Key.ToLower() == "username").Select(f => f.Value).SingleOrDefault();
        // filter and paginate results based on input
        var users = await db.Users.Where(u => string.IsNullOrWhiteSpace(filterUsername) || u.UserName == filterUsername).OrderByDescending(u => u.Id).Include(u => u.Emails).ToListAsync();
        return new ScimListResourceResponse<ScimUser>
        {
            totalResults = users.Count,
            startIndex = request.parsedStartIndex,
            itemsPerPage = request.parsedCount,
            Resources = users.Skip(request.parsedStartIndex - 1).Take(request.parsedCount).Select(u => mapper.Map<ScimUser>(u))
        };
    })
    .WithName("ListUsers")
    .WithOpenApi();

    app.MapGet(userRoute + "/{id}", async Task<IResult> (int id, ScimDbContext db, IMapper mapper) => {
        ScimUser? user = await db.Users.Where(u => u.Id == id).Include(u => u.Emails).Select(u => mapper.Map<ScimUser>(u)).FirstOrDefaultAsync();
        if(user is null)
        {
            return Results.NotFound(notFoundResponse);
        }
        return Results.Ok(user);
    })
    .WithName("GetUser")
    .WithOpenApi();
    
    app.MapPost(userRoute, async Task<IResult> (ScimUser scimUser, ScimDbContext db, IMapper mapper) => {
        var user = mapper.Map<User>(scimUser);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"users/{user.Id}", mapper.Map<ScimUser>(user));
    })
    .WithName("CreateUser")
    .WithOpenApi();
    
    app.MapPut(userRoute + "/{id}", async Task<IResult> (int id,ScimUser scimUser, ScimDbContext db, IMapper mapper) => {
        var existingUser = await db.Users.Where(u => u.Id == id).Include(u => u.Emails).FirstOrDefaultAsync();
        if (existingUser is null) { return Results.NotFound(notFoundResponse); }
        db.Entry(existingUser).CurrentValues.SetValues(mapper.Map<User>(scimUser));
        foreach (var email in existingUser.Emails.ToList())
        {
            if(!scimUser.emails.Any(u => u.value == email.Value)) { db.Emails.Remove(email); }
        }
        foreach (var email in scimUser.emails)
        {
            var existingEmail = existingUser.Emails.Where(e => e.Value == email.value).SingleOrDefault();
            if(existingEmail is not null)
            {
                db.Entry(existingEmail).CurrentValues.SetValues(email);
            } else
            {
                existingUser.Emails.Add(mapper.Map<Email>(email));
            }
        }
        await db.SaveChangesAsync();
        return Results.Ok(mapper.Map<ScimUser>(existingUser));
    })
    .WithName("UpdateUser")
    .WithOpenApi();

    app.MapPatch(userRoute + "/{id}", async Task<IResult> (int id, [FromBody] JsonDocument patchJson, ScimDbContext db, IMapper mapper) => {
        var existingUser = await db.Users.Where(u => u.Id == id).Include(u => u.Emails).FirstOrDefaultAsync();
        if (existingUser is null) { return Results.NotFound(notFoundResponse); }

        foreach(JsonElement operation in patchJson.RootElement.GetProperty("Operations").EnumerateArray()) {
            // Handling only active property for simplicity
            if (operation.GetProperty("op").GetString() == "replace" && operation.GetProperty("value").TryGetProperty("active", out var temp)) {
                existingUser.Active = operation.GetProperty("value").GetProperty("active").GetBoolean();
                await db.SaveChangesAsync();
            }
        }
        return Results.Ok(mapper.Map<ScimUser>(existingUser));
    })
    .WithName("UpdateUserPartial")
    .WithOpenApi();
    ```
- This can be tested at this point using steps mentioned in an [earlier secion](#test-project-setup).


### Secure your endpoints
- Install dependency by running `dotnet add package Okta.AspNetCore`
- Add okta configuration in `appsettings.json` file as a top level property
    ```json
    "Okta": {
        "OktaDomain": "<<Your Okta Domain>>",
        "AuthorizationServerId": "<<authorization server id>>"
    },
    ```
- Make the following changes in `Program.cs`
    - At the top add the `using` statement
        ```c#
        using Okta.AspNetCore;
        ```
    - Add the following code after `builder.Services.AddAutoMapper(...);`
        ```c#
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = OktaDefaults.ApiAuthenticationScheme;
                options.DefaultChallengeScheme = OktaDefaults.ApiAuthenticationScheme;
                options.DefaultSignInScheme = OktaDefaults.ApiAuthenticationScheme;
            })
            .AddOktaWebApi(new OktaWebApiOptions
            {
                OktaDomain = builder.Configuration["okta:OktaDomain"],
                AuthorizationServerId = builder.Configuration["okta:AuthorizationServerId"]
            });
        builder.Services.AddAuthorization();    
        ```
    - Add the following code before `app.UseHttpsRedirection();`
        ```c#
        app.UseAuthentication();
        app.UseAuthorization();
        ```
    - Add `RequireAuthorization()` to all routes. For example
        ```c#
        app.MapPost(userRoute, async Task<IResult> (ScimUser scimUser, ScimDbContext db, IMapper mapper) => {
            var user = mapper.Map<User>(scimUser);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"users/{user.Id}", mapper.Map<ScimUser>(user));
        })
        .WithName("CreateUser")
        .WithOpenApi()
        .RequireAuthorization();
        ```
- *Optional:* Bootstrap oAuth to Swashbuckle UI
    - Create an application in Okta
        - In Okta admin console, navigate to *Applications > Applications > Create App Integration*
        - Select *OIDC - OpenID Connect* > *Single-Page Application*
        - Fill a name, add *https://localhost:[test-port]/swagger/oauth2-redirect.html* to *Sign-in redirect URIs* (test port is the port your dev server is running on)
        - Assign to appropriate users. For simplicity, I selected *Allow everyone in your organization to access* as *Assignments*
        - Click *Save* button
        - Note down *Client ID* from the resulting screen
    - Update `Okta` section in `appsettings.json` with the *Client ID* noted above
        ```json
        "Okta": {
            "OktaDomain": "<<Your Okta Domain>>",
            "AuthorizationServerId": "<<authorization server id>>",
            "SwaggerClientId": "<<Swagger client app client ID>>"
        }
        ```
    - In `Program.cs`, add `using Microsoft.OpenApi.Models;` at the top
    - Update `builder.Services.AddSwaggerGen();` to
        ```c#
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{builder.Configuration["okta:OktaDomain"]}/oauth2/{builder.Configuration["okta:AuthorizationServerId"]}/v1/authorize"),
                        TokenUrl = new Uri($"{builder.Configuration["okta:OktaDomain"]}/oauth2/{builder.Configuration["okta:AuthorizationServerId"]}/v1/token"),
                        Scopes = new Dictionary<string, string> {{ "openid", "openid" }, { "profile", "profile" }}
                    }
                }
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" },
                        In = ParameterLocation.Header
                    },
                    new List<string>()
                }
            });
        });
        ```
    - Update `app.UseSwaggerUI();` to 
        ```c#
        app.UseSwaggerUI(c =>
        {
            c.OAuthClientId(builder.Configuration["okta:SwaggerClientId"]);
            c.OAuthUsePkce();
        });
        ```
- At this point, this can be tested at this point using steps mentioned in an [earlier secion](#test-project-setup)
- If you have implemented Swashbuckle oAuth bootstrap, you should be able to use UI to test endpoints. If not, generate oauth token from Okta in a different way and use `curl` or postman to test

### Integrate with Okta
- Expose your SCIM server to the internet
    - Run project using `dotnet watch --launch-profile http`
    - I have used [ngrok](https://ngrok.com/). Feel free to use any other tunneling tool like [localtunnel](https://github.com/localtunnel/localtunnel) or deploy to a public facing domain to test this
    - Tunnel using `ngrok http <<port>>` (you can get this port from *Properties/launchSettings.json*)
    - Note down the domain listed in the console (this will be referred as *scim server domain*)
    - open http://localhost:4040/ to inspect traffic
- Create a provisioning app in Okta
    - In Okta admin console, navigate to *Applications > Applications > Browse App Catalog*
    - Search for *SCIM 2.0 Test App*
    - Select *SCIM 2.0 Test App (OAuth Bearer Token)* > *Add Integration*
    - Fill *Application label*, click *Next* and click *Done*
    - Navigate to *Provisioning* tab and click *Configure API Integration* > *Enable API integration*
        - *SCIM 2.0 Base Url:* https://[scim server domain]/scim/v2
        - *OAuth Bearer Token:* Bearer Token (Can be retrieved from the test you did above either from UI or curl)
        - *Import Groups:* Uncheck as we are not implementing this
    - In application page, under *Provisioning > To App* click *Edit*
    - Check *Create Users*, *Update User Attributes*, *Deactivate Users* and click *Save*
    - In *Assignments* tab, assign to test users.
    - *Voila!* You should be able to see requests coming to your SCIM server from Okta
    - Inspect traffic to see contents of request/response
    - Now you can add more users, update users or remove users and explore more SCIM interactions
