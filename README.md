# BDP Mobile App

Mobile app for the [Bike Data Project](https://www.bikedataproject.org) that tracks bike rides via GPS and uploads them to the BDP API.

Built with .NET MAUI targeting Android (iOS support planned).

## Features

- **GPS ride tracking** with background location support (Android foreground service)
- **Local storage** of rides in SQLite for offline use
- **GPX upload** to the BDP API (`POST /manual/upload?source=mobile`)
- **Ride history** with per-ride detail view and upload status
- **Stats** from the BDP API (total tracks, distance, breakdown by provider)
- **Authentication** via Keycloak (Authorization Code + PKCE)

## Project Structure

```
src/BDP.App/
  Models/          Data models (TrackPoint, RideRecord, UploadResult, StatsResult)
  Services/        Auth, GPS location, ride tracking, database, GPX serialization, API client
  ViewModels/      MVVM view models (CommunityToolkit.Mvvm)
  Views/           XAML pages (Login, Record, History, Detail, Stats)
  Converters/      Value converters for distance, duration, speed
  Platforms/
    Android/       Foreground service, manifest, permissions
```

## Prerequisites

- .NET 10 SDK (preview)
- MAUI workload: `dotnet workload install maui-android`
- Java 17 (for Android build)

## Build

```sh
dotnet build src/BDP.App/BDP.App.csproj -f net10.0-android
```

## Publish APK

```sh
dotnet publish src/BDP.App/BDP.App.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=apk -o ./artifacts
```

## CI/CD

The GitHub Actions workflow (`.github/workflows/build-android.yml`) builds a signed APK on every push to `main`. Tagging a release (`v*`) creates a GitHub Release with the APK attached.

## API Integration

The app uploads rides to the existing BDP Manual upload endpoint with `?source=mobile` so the API can distinguish mobile-tracked rides from manual GPX uploads. Authentication is handled via Keycloak at `https://www.bikedataproject.org/api/users/realms/bdp`.
