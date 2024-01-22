# Integrate with Okta Using SCIM and dotnet

## What is SCIM?
SCIM, the System for Cross-domain Identity Management is an open standard protocol that allows us to manage identities across systems as well as common user lifecycles. This is a HTTP-based protocal which defines a set of standard endpoints and schemas which can be used to overcome complexity in implementing user lifecycle management operations across different systems.

## Components of SCIM
There are two major components for a SCIM integration,
- *SCIM Server* - A server which has implemented endpoints as described in SCIM spec. For example, User endpoints, Group endpoints, Schemas endpoints, etc. In our use case, this is developed by the application team(s) where the user profiles are expected to be mastered by Okta. We will go into detail on creating a sample scim server using dotnet
- *SCIM Client* - A service which makes SCIM compliant HTTP calls to a SCIM server to exchange user profile information. In our use case, Okta will act as a client and make calls to the SCIM server we create and configure.

## Develop SCIM Server

### Prerequisites
- dotnet SDK (I used [dotnet 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for demo)
- Code Editor (I used Visual Studio Code) 
- [Okta CLI](https://cli.okta.com/)

### Create dotnet Project
- Create a new folder named *okta-scim-server-dotnet*
- Open terminal and make this as current folder using `cd okta-scim-server-dotnet` command
- Create new API project using `dotnet new webapi`
- Trust self signed TLS certs using `dotnet dev-certs https --trust`

### Testing
- Run project using `dotnet watch --launch-profile https`
- At this point using the *https://localhost:[port]/swagger/index.html* you will be able to see swagger UI. Typically a browser tab opens automatically, if not check url in `Properties/launchSettings.json`

### Setup Okta
Before you begin, you’ll need a free Okta developer account. Install the [Okta CLI](https://cli.okta.com/) and run `okta register` to sign up for a new account. If you already have an account, run `okta login`. 
- Then, run `okta apps create`
- Application name: `okta dotnet swagger client`
- Type of Application: `Single Page App`
- Enter your Redirect URI(s): `https://localhost:[port]]/swagger/oauth2-redirect.html` (Port is from previous testing step)
- Enter your Post Logout Redirect URI(s): leave default
- For authorization server, select `default`.
- Keep `Issuer` and `ClientId` for configuring later

### Create Data Models
Lets develop database models and setup ORM. For this code first model has been used. This sample uses `EntityFrameworkCore` as ORM and `Sqlite` databsae

Add required dependencies by running following commands
- `dotnet tool install --global dotnet-ef`
- `dotnet add package Microsoft.EntityFrameworkCore.Tools`
- `dotnet add package Microsoft.EntityFrameworkCore.Design`
- `dotnet add package Microsoft.EntityFrameworkCore.Sqlite`

Create `DataModels.cs` file and add required Model classes for `User` and child object `Email`.
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
Add DB context for entity framework in `DataModels.cs`. This will include the two models we created above. Also we can add some seed data in `OnModelCreating` which will be useful for testing. In this sample, we are going to create support for User resource type. But if you would like to expand, you can use similar concepts to care resources adn enpoints for other resource types such as groups. Paste the following code at the bottom of the class
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
            new Email { Type = "work", Value="dslem@fake.domain", Primary = true, UserId = 2 },
            new Email { Type = "work", Value="smahesh@fake.domain", Primary = true, UserId = 3 }
        });
        base.OnModelCreating(modelBuilder);
    }
}
```
Add dbconfiguration in `appsettings.json` file as a top level property. This creates a db file in project folder named `scim-db.dev`
```json
"ConnectionStrings": {
    "DefaultConnection": "Data Source=scim-dev.db;"
}
```
Now we can wire it all up in `Program.cs` for DI using `AddDbContextPool` middleware
- At the top add dependecies with `using` statements
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
At this point we can migrate the coded database models and data into the actual database.
- Prepare db migration by running `dotnet ef migrations add InitialScimDb`
- Apply these changes to db by running `dotnet ef database update`
- *Optional:* Test db creation using command line tool, 
    - You should have sqllite3 client installed. (I had this OOB in mac OS)
    - Connect using`sqlite3 <<Path to sqlite file>>/scim-dev.db`
    - List tables using `.tables`
    - List users by running `select * from Users;`
    - Then exit using `.exit`

### Create SCIM models and mapping
Having taken care of data models and database creation, lets move on to creating models which are SCIM compliant. Our request and responses will be using these models to communicate with SCIM clients. Create new file `ScimModles.cs` and add the following SCIM models
- `ScimListResourceRequest` is used when list resources is called. It has pagination and filtering parameters
- `ScimListResourceResponse<T>` is used when returning list of resources. Since it is generic it can be reused for multiple resource types
- `ScimErrorResponse` is used when returning error such as resource not found in standard SCIM format
- `ScimUser`, `ScimName`, `ScimEmail` are user object and child objects which are SCIM compliant
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
We also need to create mappers between DB models and SCIM models to avoid lot of manual conversions. For this we are going to use `AutoMapper` package
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
- To wire up SCIM models and the mappers we have created, `AddAutoMapper` middleware in `Program.cs`
    - At the top add the `using` statements
        ```c#
        using AutoMapper;
        ```
    - After `builder.Services.AddDbContextPool<ScimDbContext>(...);` add the following code
        ```c#
        builder.Services.AddAutoMapper(typeof(Program).Assembly);
        ```

### Create SCIM Endpoints
Since we have created the necessary data and scim models. We can move on to creating the endpoints for User lifecycle management. First lets setup dependencies, some basic global configuration and cleanup unnecessary code
- Install dependencies by running `dotnet add package Newtonsoft.Json`
- At the top of `program.cs` file, add `using` statements
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
- Remove WeatherForecast related sample code

#### Get User Endpoint

We are using minimal APIs for creating endpoints. For Get user, lets create the route which expects user's `id` in the path and responds with a single `ScimUser` object if found and `ScimErrorResponse` if no users are found. Add following code before `app.run()`
```c#
var scimPrefix = "/scim/v2";
var userRoute = $"{scimPrefix}/users";
var notFoundResponse = new ScimErrorResponse((int)HttpStatusCode.NotFound, "Resource Not Found");
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
```
Some variables are declared outside this method for reuse.

Now run the project by using steps mentioned in [testing](#testing) section. Click `Try it out` and enter `3` as id. You should recieve the following as response based on the seeding data with status code `200`

```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "id": "3",
  "userName": "smahesh@fake.domain",
  "name": {
    "givenName": "Sarika",
    "familyName": "Mahesh"
  },
  "displayName": "Sarika Mahesh",
  "emails": [
    {
      "value": "smahesh@fake.domain",
      "type": "work",
      "primary": true
    }
  ],
  "active": true
}
```

Now change `id` to `100` and try again. You should receive the following as response with status code `404`
```json
{
  "schemas": [
    "urn:ietf:params:scim:api:messages:2.0:Error"
  ],
  "detail": "Resource Not Found",
  "status": 404
}
```
This completes our testing for get user endpoint. Lets add other endpoints

#### List User Endpoint
For listing users add following code below the previously added secion. This expects `ScimListResourceRequest` attributes in query and responds with `200` status and `ScimListResourceResponse<ScimUser>` user in body. Pagination will be used by Okta to retrive if the users count is large

```c#
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
```

To test this, just execute without filling parameters. Code has some defaults and will return the first 100 users. You can expect a response with `200` status and body similar to the following
```json
{
  "totalResults": 4,
  "startIndex": 1,
  "itemsPerPage": 100,
  "resources": [
    {
      "schemas": [
        "urn:ietf:params:scim:schemas:core:2.0:User"
      ],
      "id": "4",
      "userName": "fakeguy@fake.domain",
      "name": {
        "givenName": "Fake",
        "familyName": "Guy"
      },
      "displayName": "Fake Guy",
      "emails": [],
      "active": true
    },
    ... //Removed for brevity
  ]
}
```
You can explore by adding different numerical values in `startIndex`, `count` and also some valid `filter` such as `userName eq "fakeguy@fake.domain"`

#### Create User Endpoint
For creating user add following code below the previously added section. This expects as `ScimUser` object in request body

```c#
app.MapPost(userRoute, async Task<IResult> (ScimUser scimUser, ScimDbContext db, IMapper mapper) => {
    var user = mapper.Map<User>(scimUser);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"users/{user.Id}", mapper.Map<ScimUser>(user));
})
.WithName("CreateUser")
.WithOpenApi();
```

To test this, enter the following as request body in Swagger UI
```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "userName": "fakeguy@fake.domain",
  "name": {
    "givenName": "Fake",
    "familyName": "Guy"
  },
  "displayName": "Fake Guy",
  "active": true
}
```
The expected response will be something like below with `201` status
```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "id": "4",
  "userName": "fakeguy@fake.domain",
  "name": {
    "givenName": "Fake",
    "familyName": "Guy"
  },
  "displayName": "Fake Guy",
  "emails": [],
  "active": true
}
```

#### Update User Endpoint
For Updating user add following code below the previously added section. This uses `PUT` operation but you can also develop `PATCH` request instead as well. This operation expects user's `id` in path and `ScimUser` object in body. This can return either `200` or `404` response

```c#
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
```

To test this, enter `4` as `id` and the following as request body in Swagger UI
```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "id": "4",
  "userName": "fakeguy@fake.domain",
  "name": {
    "givenName": "Fake",
    "familyName": "Guy",
    "middleName": "R"
  },
  "displayName": "Fake Guy",
  "emails": [],
  "active": true
}
```
The expected response will be something like below with `200` status
```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "id": "4",
  "userName": "fakeguy@fake.domain",
  "name": {
    "givenName": "Fake",
    "familyName": "Guy",
    "middleName": "R"
  },
  "displayName": "Fake Guy",
  "emails": [],
  "active": true
}
```
Feel free to test it out with an invalid user id to get `404` response

#### Delete User Endpoint
For deleting user add following code below the previously added section. This uses `PATCH` operation by setting property `active: false`. This operation expects user's `id` in path and `JsonPatchDocument` object in body. This can return either `200` or `404` response

```c#
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

To test this, enter `4` as `id` and the following as request body in Swagger UI
```json
{
    "schemas": [
        "urn:ietf:params:scim:api:messages:2.0:PatchOp"
    ],
    "Operations": [
        {
            "op": "replace",
            "value": {
                "active": false
            }
        }
    ]
}
```
The expected response will be something like below with `200` status
```json
{
  "schemas": [
    "urn:ietf:params:scim:schemas:core:2.0:User"
  ],
  "id": "4",
  "userName": "fakeguy@fake.domain",
  "name": {
    "givenName": "Fake",
    "familyName": "Guy",
    "middleName": "R"
  },
  "displayName": "Fake Guy",
  "emails": [],
  "active": false
}
```
Feel free to test it out with an invalid user id to get `404` response

### Secure your endpoints

Now that endpoints are created and tested. It is time to secure it before integration with Okta. Okta recommends atleast one of three ways to secure your server. For this sample, we are going to use *oAuth* using `Okta.AspNetCore` package.
- Install dependency by running `dotnet add package Okta.AspNetCore`
- Add okta configuration in `appsettings.json` file as a top level property
    ```json
    "Okta": {
        "OktaDomain": "<<Your Okta Domain>>",
        "AuthorizationServerId": "<<authorization server id>>"
    },
    ```
- Make the following changes in `Program.cs` to implement authentication using `AddOktaWebApi` middleware.
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
- Next lets wire up this authentication requirement to all endpoints we developed. Simply add `RequireAuthorization()` to all routes. For example
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
#### Bootstrap oAuth to Swagger UI
- Update `Okta` section in `appsettings.json` with the *Client ID* from earlier [section](#setup-okta)
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
- At this point, this can be tested like the earlier section. However this needs an additional step to click *Authorize > Select all scopes > Authorize > Complete authentication*. If you do not authenticate, you will get `401` responses from these protected endpoints.

### Integrate with Okta
- Expose your SCIM server to the internet
    - Run project using `dotnet watch --launch-profile http` (intentionally running using http profile for tunneling)
    - I have used [ngrok](https://ngrok.com/). Feel free to use any other tunneling tool like [localtunnel](https://github.com/localtunnel/localtunnel) or deploy to a public facing domain to test this
    - Tunnel using `ngrok http <<port>>` (you can get this port from *Properties/launchSettings.json*)
    - Note down the domain listed in the console (this will be referred as *scim server domain*)
    - open http://localhost:4040 to inspect traffic
- Create a provisioning app in Okta
    - In Okta admin console, navigate to *Applications > Applications > Browse App Catalog*
    - Search for *SCIM 2.0 Test App*
    - Select *SCIM 2.0 Test App (OAuth Bearer Token)* > *Add Integration*
    - Fill *Application label*, click *Next* and click *Done*
    - Navigate to *Provisioning* tab and click *Configure API Integration* > *Enable API integration*
        - *SCIM 2.0 Base Url:* https://[scim-server-domain]/scim/v2
        - *OAuth Bearer Token:* Bearer Token (Can be retrieved from the test you did above either from UI or curl)
        - *Import Groups:* Uncheck as we are not implementing this
    - In application page, under *Provisioning > To App* click *Edit*
    - Check *Create Users*, *Update User Attributes*, *Deactivate Users* and click *Save*
    - In *Assignments* tab, assign to test users.
    - *Voila!* You should be able to see requests coming to your SCIM server from Okta

## Next Steps - More Exploration :partying_face:
    - Inspect traffic to see contents of request/response. If you use ngrok, you can use http://localhost:4040
    - Now you can add more users, update users or remove users and explore more SCIM interactions
    - You can extend resouces supported by adding groups
    - Change authentication to use basic auth or header auth
    - Update SCIM Model to add more attributes you might need
    - Always raise your hand and comment if you have question or want something added to this blog.
    - Cheers!
