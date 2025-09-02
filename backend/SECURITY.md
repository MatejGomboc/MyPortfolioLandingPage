# Backend Security Architecture

## Overview
This ASP.NET Core backend is designed with a **security-first approach**, implementing defense-in-depth principles to minimize attack surface and prevent unauthorized access.

## Key Security Features

### 1. Production Mode by Default
- Application runs in **Production mode** unless explicitly configured otherwise
- Development features (OpenAPI/Swagger) are only available when `ASPNETCORE_ENVIRONMENT=Development`
- No accidental exposure of debug information or development endpoints

### 2. Localhost-Only Binding (Hardcoded)
- **Critical Security Feature**: The application is hardcoded to bind ONLY to `localhost` (127.0.0.1)
- Cannot be accidentally exposed to external networks
- This binding is enforced in `Program.cs` and cannot be overridden by configuration
- Port is configurable (default: 5001), but the localhost binding is not

### 3. Reverse Proxy Architecture
- Designed to run behind a reverse proxy (nginx, Apache, IIS, etc.)
- HTTPS/TLS termination handled by the reverse proxy
- Application only serves HTTP on localhost
- Benefits:
  - Centralized SSL certificate management
  - Better DDoS protection
  - Request filtering and rate limiting at proxy level
  - Load balancing capabilities

## Configuration

### Running the Application

**Development Mode:**
```bash
dotnet run --launch-profile Development
```
- Enables OpenAPI/Swagger at `/openapi`
- Enhanced logging

**Production Mode (Default):**
```bash
dotnet run --launch-profile Production
# or simply
dotnet run
```
- No development features exposed
- Minimal logging

### Port Configuration
Port can be changed in `appsettings.json`:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Port": 5001
      }
    }
  }
}
```

## Reverse Proxy Setup Example

### nginx Configuration
```nginx
server {
    listen 443 ssl http2;
    server_name api.yourdomain.com;
    
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    
    location / {
        proxy_pass http://localhost:5001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## Security Checklist
✅ Defaults to Production mode  
✅ Localhost-only binding (hardcoded)  
✅ No direct external access possible  
✅ HTTPS handled by reverse proxy  
✅ Development features hidden in production  
✅ Minimal attack surface  

## Why These Decisions?

1. **No External URLs in Config**: Prevents accidental misconfiguration that could expose the API
2. **Hardcoded Localhost**: Makes it impossible to accidentally bind to `0.0.0.0` or external IPs
3. **Reverse Proxy Required**: Forces proper infrastructure setup with security layers
4. **Production by Default**: Follows the principle of "secure by default"

## Testing
Use the included `test.http` file with REST Client extension in VS Code:
```http
GET http://localhost:5001/health
```

## Important Notes
- This API will **NEVER** be directly accessible from outside the host machine
- All external access must go through the configured reverse proxy
- This is by design and cannot be changed without modifying `Program.cs`