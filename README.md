# How to Manage User Lifecycle with .NET and SCIM

This repo is a .NET server that implements RESTful APIs to manage user lifecycle as written in the SCIM [System for Cross-domain Management](https://datatracker.ietf.org/doc/html/rfc7644). 

Please read [How to Add Authentication to .NET MAUI Apps][blog] to see how it was created and how to integrate with a SCIM-compliant Identity Provider such as Okta. 

**Prerequisites:**

* MacOS Sonoma 14.3 (23D56)
* [Visual Studio Code Version: 1.85.2](https://code.visualstudio.com/)
* [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* [Okta CLI](https://cli.okta.com)
* [Okta](https://developer.okta.com/)

----

* [Getting Started](#getting-started)
* [Links](#links)
* [Help](#help)
* [License](#license)

## Getting Started

Clone or download the sample. You can download the files as a zip file. To clone the repo follow the instructions below:

```bash
git clone https://github.com/oktadev/okta-net-scim-example.git
cd okta-net-maui-example
```

Open the project in Visual Studio Code.

### Create an OIDC Application in Okta

Create a free developer account with the following command using the [Okta CLI](https://cli.okta.com):

```shell
okta register
```

If you already have a developer account, use `okta login` to integrate it with the Okta CLI. 
Create a client application in Okta with the following command:

```shell
okta apps create
```

You will be prompted to select the following options:
- Application name: 
- Type of Application: 
- Callback: `myapp://callback`
- Post Logout Redirect URI: `myapp://callback`

The application configuration will be printed in the terminal. You will see output like the following when it's finished:

```console
Okta application configuration:
Issuer:    https://{yourOktaDomain}/oauth2/default
Client ID: {yourClientID}
```

Replace all instances of {yourOktaDomain} and {yourClientID} in the project.

## Update Configuration

- Update `Okta` section in `appsettings.json` file
- `SwaggerClientId` is optional and is needed only when you want to use UI to test endpoints
    - Create an application in Okta
        - In Okta admin console, navigate to *Applications > Applications > Create App Integration*
        - Select *OIDC - OpenID Connect* > *Single-Page Application*
        - Fill a name, add *https://localhost:7094/swagger/oauth2-redirect.html* to *Sign-in redirect URIs* (test port is the port your dev server is running on)
        - Assign to appropriate users. For simplicity, I selected *Allow everyone in your organization to access* as *Assignments*
        - Click *Save* button
        - Note down *Client ID* from the resulting screen

## Prepare Database
- Install ef tools by running `dotnet tool install --global dotnet-ef`
- Add migration of this initial database by running `dotnet ef migrations add InitialScimDb`
- Apply these changes to db by running `dotnet ef database update`
- *Optional:* Test db creation using command line tool, 
    - You should have [sqlite3](https://www.sqlite.org/) client installed. (I had this out of the box in mac OS)
    - Connect using`sqlite3 <<Path to sqlite file>>/scim-dev.db`
    - List tables using `.tables`
    - Then exit using `.exit`

## Test Project Setup
- Run project using `dotnet watch --launch-profile https`
- At this point using *https://localhost:7094/swagger/index.html* you will be able to see swagger UI (Typically, a browser tab opens automatically)

## Integrate with Okta
- Expose your SCIM server to the internet
    - Run project using `dotnet watch --launch-profile http`
    - I have used [ngrok](https://ngrok.com/). Feel free to use any other tunneling tool like [localtunnel](https://github.com/localtunnel/localtunnel) or deploy to a public facing domain to test this
    - Tunnel using `ngrok http 5156`
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

## Help

Please post any questions as comments on the [blog post][blog], or visit our [Okta Developer Forums](https://devforum.okta.com/).

## License

Apache 2.0, see [LICENSE](LICENSE).

[blog]: https://developer.okta.com/blog/2023/06/21/net-maui-authentication
