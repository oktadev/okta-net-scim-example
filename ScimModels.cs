using AutoMapper;

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
