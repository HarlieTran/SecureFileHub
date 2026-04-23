# SecureFileHub

SecureFileHub is an ASP.NET Core MVC web application for secure file upload, encrypted storage, controlled sharing, and audit logging.

## Features

- User registration and login
- BCrypt password hashing
- Session-based authentication
- Account lockout after repeated failed logins
- File upload with extension, size, and magic-byte validation
- Encrypted file storage using AES-256-GCM
- Shareable links with expiry, password protection, and permission level
- Admin dashboard with audit log visibility

## Tech Stack

- C# / .NET 9
- ASP.NET Core MVC
- Entity Framework Core
- SQLite
- Bootstrap

## Prerequisites

- .NET 9 SDK

## Setup

1. Copy `.env.example` to `.env`
2. Set `ENCRYPTION_KEY` to a 32-byte key
3. Restore packages:

```powershell
dotnet restore
```

4. Build the project:

```powershell
dotnet build
```

5. Run the application:

```powershell
dotnet run
```

## Environment Variable

Example `.env` value:

```text
ENCRYPTION_KEY=12345678901234567890123456789012
```

The encryption key must be exactly 32 bytes.

## Database

- The app uses SQLite with `SecureFileHub.db`
- EF Core migrations are applied automatically on startup

## Development Accounts

In development only, if the database has no users yet, the app seeds:

- `admin@test.com` / `Admin@123`
- `user@test.com` / `User@1234`

Do not use these credentials in production.

## Running Profiles

Default launch settings:

- HTTP: `http://localhost:5106`
- HTTPS: `https://localhost:7193`

## Security Notes

- Do not commit `.env`
- Do not commit real secrets or production credentials
- Uploaded files are stored outside the web root in the `uploads/` directory
- Files are encrypted before being written to disk

## Submission Notes

- Include `.env.example`, not the real `.env`
- Include screenshots required by the final project document
- Run dependency audit before submission:

```powershell
dotnet list package --vulnerable --include-transitive
```
