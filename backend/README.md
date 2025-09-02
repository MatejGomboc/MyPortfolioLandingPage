# MyPortfolioLandingPage Backend

A secure ASP.NET Core backend API designed with security-first principles and end-to-end encryption.

## Quick Start

### Prerequisites
- .NET 8.0 SDK or later
- Development HTTPS certificate

### Setup Development Certificate
```bash
dotnet dev-certs https --trust
```

### Run the Application

**Development Mode** (with OpenAPI/Swagger):
```bash
dotnet run --launch-profile Development
```
Access OpenAPI at: https://localhost:5000/openapi

**Production Mode** (default, secure):
```bash
dotnet run --launch-profile Production
# or simply
dotnet run
```

### Test the API
```bash
curl -k https://localhost:5000/health
```

## Security Features

- 🔒 **HTTPS-Only**: End-to-end encryption, no HTTP endpoints
- 🏠 **Localhost-Only**: Hardcoded binding to 127.0.0.1
- 🚀 **Production by Default**: Secure unless explicitly set to Development
- 🔐 **Reverse Proxy Ready**: Designed for nginx/Apache/IIS frontend
- 🛡️ **Minimal Attack Surface**: No external access possible

## Architecture

```
Internet → [Reverse Proxy] → HTTPS → [localhost:5000] → API
              ↓                           ↓
         External SSL               Internal SSL
         Certificate                Dev/Prod Certificate
```

## Configuration

Edit `appsettings.json` to change the HTTPS port:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Port": 5000
      }
    }
  }
}
```

## Testing Default Production Mode

The Production launch profile intentionally has **no environment variable set** to verify that the application defaults to Production mode. This is a security test to ensure "secure by default" behavior.

## Documentation

- [Security Architecture](./SECURITY.md) - Detailed security documentation
- [Test Endpoints](./test.http) - REST client test file

## Important Security Notes

⚠️ This API **cannot** be accessed from outside the machine - this is by design!  
⚠️ All external access must go through a properly configured reverse proxy  
⚠️ The localhost binding is hardcoded and cannot be changed via configuration  

## License

See [LICENSE](../LICENSE) in the repository root.