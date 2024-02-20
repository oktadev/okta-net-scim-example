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
git clone https://github.com/oktadev/okta-net-maui-example.git
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

## Run the app

In Visual Studio Code run ...

## Help

Please post any questions as comments on the [blog post][blog], or visit our [Okta Developer Forums](https://devforum.okta.com/).

## License

Apache 2.0, see [LICENSE](LICENSE).

[blog]: https://developer.okta.com/blog/2023/06/21/net-maui-authentication
