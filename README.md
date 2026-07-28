# PostgreManagementStudio

Windows-only WPF PostgreSQL management application. Current milestone: minimal `SELECT version()` vertical slice.

Prerequisites: Git, .NET SDK 9 (upgrade to current LTS when installed), Visual Studio with .NET desktop development, and PostgreSQL for Windows.

Build/test: `dotnet restore`, `dotnet build --configuration Release`, `dotnet test --configuration Release`.

Run the desktop project after setting `PMS_CONNECTION_STRING`, for example `Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<password>` (do not commit credentials).
