# okta-scim-server-dotnet

This project is intended to demonstate integrating Okta and a sample SCIM server developed in dotnet

## Prerequisites
- dotnet SDK (I used [dotnet 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for demo)
- Code Editor (I used Visual Studio Code and CLI) 
- Okta Tenant - Signup [here](https://developer.okta.com/signup/)

## Get this code locally

- If you have git client, run `git clone https://github.com/ramgandhi-okta/okta-scim-server-dotnet.git` to download the code
- If not, you can download the repo

## Prepare Database
- Install ef tools by running `dotnet tool install --global dotnet-ef`
- Add migration of this initial database by running `dotnet ef migrations add InitialScimDb`
- Apply these changes to db by running `dotnet ef database update`
- *Optional:* Test db creation using command line tool, 
    - You should have sqllite3 client installed. (I had this OOB in mac OS)
    - Connect using`sqlite3 <<Path to sqlite file>>/scim-dev.db`
    - List tables using `.tables`
    - Then exit using `.exit`

## Update Configuration

- Update `Okta` section in `appsettings.json` file
- `SwaggerClientId` is optional and is needed only when you want to use UI to test endpoints
    - Create an application in Okta
        - In Okta admin console, navigate to *Applications > Applications > Create App Integration*
        - Select *OIDC - OpenID Connect* > *Single-Page Application*
        - Fill a name, add *https://localhost:[test-port]/swagger/oauth2-redirect.html* to *Sign-in redirect URIs* (test port is the port your dev server is running on)
        - Assign to appropriate users. For simplicity, I selected *Allow everyone in your organization to access* as *Assignments*
        - Click *Save* button
        - Note down *Client ID* from the resulting screen

## Test Project Setup
- Run project using `dotnet watch --launch-profile https`
- At this point using the *https://localhost:[port]/swagger/index.html* you will be able to see swagger UI (Typically a browser tab opens automatically, if not check url in Properties/launchSettings.json)

## Integrate with Okta
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

## Want to develop from Scratch?
You can look at [this](CreateProject.md) document for steps I used to develop this project
