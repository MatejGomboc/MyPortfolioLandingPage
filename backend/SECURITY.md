# Backend Security Architecture

## Overview
This ASP.NET Core backend is designed with a **security-first approach**, implementing defense-in-depth principles with **end-to-end encryption** to minimize attack surface and prevent unauthorized access.

## Key Security Features

### 1. Production Mode by Default
- Application runs in **Production mode** unless explicitly configured otherwise
- **No explicit Production environment variable** in launch settings to test default behavior
- Development features (OpenAPI/Swagger) are only available when `ASPNETCORE_ENVIRONMENT=Development`
- No accidental exposure of debug information or development endpoints

### 2. HTTPS-Only Configuration
- **End-to-end encryption** from reverse proxy to application
- No HTTP endpoints - all traffic must use HTTPS
- Even localhost traffic is encrypted for defense in depth
- Protects against local network sniffing or compromised machines

### 3. Localhost-Only Binding (Hardcoded)
- **Critical Security Feature**: The application is hardcoded to bind ONLY to `localhost` (127.0.0.1)
- Cannot be accidentally exposed to external networks
- This binding is enforced in `Program.cs` and cannot be overridden by configuration
- Port is configurable (default: 5000), but the localhost binding is not

### 4. Reverse Proxy Architecture
- Designed to run behind a reverse proxy (nginx, Apache, IIS, etc.)
- Proxy handles external HTTPS connections
- Application handles internal HTTPS on localhost
- Benefits:
  - Double encryption layer possible
  - Centralized SSL certificate management
  - Better DDoS protection
  - Request filtering and rate limiting at proxy level
  - Load balancing capabilities

## Configuration

### Development Certificate Setup
For local development, you need to trust the ASP.NET Core HTTPS development certificate:
```bash
dotnet dev-certs https --trust
```

### Running the Application

**Development Mode:**
```bash
dotnet run --launch-profile Development
```
- Enables OpenAPI/Swagger at `/openapi`
- Enhanced logging
- Uses HTTPS on localhost:5000

**Production Mode (Default):**
```bash
dotnet run --launch-profile Production
# or simply
dotnet run
```
- No development features exposed
- Minimal logging
- Uses HTTPS on localhost:5000
- **Note**: Production profile intentionally has no environment variable to test default behavior

### Port Configuration
Port can be changed in `appsettings.json`:
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

## Reverse Proxy Setup Example

### nginx Configuration (with end-to-end HTTPS)
```nginx
server {
    listen 443 ssl http2;
    server_name api.yourdomain.com;
    
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    
    location / {
        # Proxy to HTTPS backend
        proxy_pass https://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Backend certificate verification (optional for localhost)
        proxy_ssl_verify off;
    }
}
```

## Security Checklist
✅ Defaults to Production mode (tested by not setting environment)  
✅ HTTPS-only configuration (end-to-end encryption)  
✅ Localhost-only binding (hardcoded)  
✅ No direct external access possible  
✅ Traffic encrypted between proxy and application  
✅ Development features hidden in production  
✅ Minimal attack surface  

## Why These Decisions?

1. **HTTPS-Only**: Provides end-to-end encryption, protecting against local network attacks
2. **No External URLs in Config**: Prevents accidental misconfiguration that could expose the API
3. **Hardcoded Localhost**: Makes it impossible to accidentally bind to `0.0.0.0` or external IPs
4. **Reverse Proxy Required**: Forces proper infrastructure setup with security layers
5. **Production by Default**: Follows the principle of "secure by default"
6. **No Explicit Production Setting**: Tests that defaults work correctly

## Testing
Use the included `test.http` file with REST Client extension in VS Code:
```http
GET https://localhost:5000/health
```

**Note**: You may need to configure your REST client to accept the development certificate.

## Important Notes
- This API will **NEVER** be directly accessible from outside the host machine
- All external access must go through the configured reverse proxy
- Traffic is encrypted end-to-end (proxy → app)
- This is by design and cannot be changed without modifying `Program.cs`

## Production Deployment
In production environments:
1. Use proper SSL certificates (not development certificates)
2. Consider certificate pinning between proxy and application
3. Implement monitoring and alerting
4. Use secrets management for sensitive configuration
5. Enable security headers at the reverse proxy level