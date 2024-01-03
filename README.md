# okta-scim-server-dotnet

## Steps to Create SCIM Server using dotnet

### Prepare project (Already completed in repo)
- Create a new folder named *okta-scim-server-dotnet*
- Open terminal and make this as current folder using `cd <folder path>` command
- Create new API project using `dotnet new webapi`
- Add dotnet *.gitignore* file using standard options

### Test run initial setup
- Trust self signed TLS certs using `dotnet dev-certs https --trust`
- Run project using `dotnet watch --launch-profile https`
- At this point using the *https://localhost:<port>/swagger/index.html* you will be able to see swagger UI (Typically a browser tab opens automatically, if not check url in Properties/launchSettings.json)

### Create ORM for the previously created DB
- Add required dependencies for ORM
    - `dotnet tool install --global dotnet-ef`
    - `dotnet add package Microsoft.EntityFrameworkCore.Tools`
    - `dotnet add package Microsoft.EntityFrameworkCore.Design`
    - `dotnet add package Microsoft.EntityFrameworkCore.Sqlite`
- Create Required model classes in Models.cs file
- Add dbconfiguration in Program.cs and appsettings.json files
- Add migration of this initial database using `dotnet ef migrations add InitialScimDb`
- Apply these changes to db using `dotnet ef database update`


- Test db creation using command line tool, 
    - Connect using`sqlite3 <<Path to sqlite file>>/scim-dev.db`
    - List tables using `.tables`
    - Then exit using `.exit`



### Create User SCIM Endpoints