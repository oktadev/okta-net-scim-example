using okta_scim_server_dotnet;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using AutoMapper;
using System.Net;
using Microsoft.OpenApi.Models;
using Okta.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

#region Configure Services
var builder = WebApplication.CreateBuilder(args);
    
    builder.Services.ConfigureHttpJsonOptions(options => {
        options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
    builder.Services.AddEndpointsApiExplorer();
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
    builder.Services.AddDbContextPool<ScimDbContext>(
        options => options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
    );
    builder.Services.AddAutoMapper(typeof(Program).Assembly);
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
#endregion
#region Configure App
    var app = builder.Build();
    
    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.OAuthClientId(builder.Configuration["okta:SwaggerClientId"]);
            c.OAuthUsePkce();
        });
    }
    
    app.UseAuthentication();
    app.UseAuthorization();
    
    app.UseHttpsRedirection();
#endregion

#region Configure Routes
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
    .WithOpenApi()
    .RequireAuthorization();
    
    app.MapGet(userRoute + "/{id}", async Task<IResult> (int id, ScimDbContext db, IMapper mapper) => {
        ScimUser? user = await db.Users.Where(u => u.Id == id).Include(u => u.Emails).Select(u => mapper.Map<ScimUser>(u)).FirstOrDefaultAsync();
        if(user is null)
        {
            return Results.NotFound(notFoundResponse);
        }
        return Results.Ok(user);
    })
    .WithName("GetUser")
    .WithOpenApi()
    .RequireAuthorization();
    
    app.MapPost(userRoute, async Task<IResult> (ScimUser scimUser, ScimDbContext db, IMapper mapper) => {
        var user = mapper.Map<User>(scimUser);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"users/{user.Id}", mapper.Map<ScimUser>(user));
    })
    .WithName("CreateUser")
    .WithOpenApi()
    .RequireAuthorization();
    
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
    .WithOpenApi()
    .RequireAuthorization();

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
    .WithOpenApi()
    .RequireAuthorization();
#endregion

app.Run();
