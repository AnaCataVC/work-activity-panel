# Work Activity Panel 🚀

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Windows App SDK](https://img.shields.io/badge/Windows_App_SDK-2.4-00A4EF?logo=windows11&logoColor=white)](https://github.com/microsoft/WindowsAppSDK)
[![Tests](https://img.shields.io/badge/Tests-xUnit%20(22%2F22%20Passed)-4EBA6F?logo=xunit&logoColor=white)](https://xunit.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

*Read this in [English](#english) | Léelo en [Español](#español)*

---

<a name="english"></a>
## English

### 1. Project Description
**Work Activity Panel** is a native Windows 11 desktop application designed to streamline daily work routines. It automatically manages work applications based on your configurable schedule, seamlessly syncs with **Google Calendar** via private iCal feeds, automates meeting preparedness by auto-launching **Granola** 5 minutes prior to any scheduled meeting, ensures **Slack** is open at the start of your workday, and automatically backs up and syncs your local work files to **Google Drive** using a lightweight Google Apps Script Web App bridge.

### 2. Key Features
- ⏰ **Schedule-Aware & Vacation Mode:** Configurable work days and hours. Automatically pauses background triggers when Vacation Mode is enabled.
- 💬 **Slack Integration:** Automatically ensures Slack is running upon the start of your work hours.
- 🥑 **Granola Pre-Meeting Automation:** Programmatically detects approaching Google Calendar meetings and launches Granola 5 minutes before they begin.
- 📅 **Private Google Calendar Sync:** Live view of today's scheduled meetings with 1-click meeting join links (Google Meet, Zoom, Microsoft Teams, Webex).
- ☁️ **Google Drive Backup & Sync:**
  - Lightweight Google Apps Script Web App endpoint (no complex Google Cloud project or third-party client required).
  - SHA-256 hash incremental change detection (only uploads new or modified files).
  - Multi-criteria filtering (whitelist/blacklist extensions, folder exclusion, file size limits).
  - Optional automatic sync at the end of your workday (`AutoSyncOnWorkEnd`).
  - Step-by-step setup guide: [Google Drive Setup Guide](docs/google-setup-guide.md).
- 💻 **Fluent Design & System Tray:** Native Windows 11 Mica backdrop, dark/light theme support, and minimize-to-system-tray capability.
- ⚡ **Event-Driven Efficiency:** Zero continuous polling overhead; uses precision scheduled timers.

### 3. Technologies Used
- **UI Framework:** WinUI 3 (Windows App SDK 2.4) with Fluent Design & Mica backdrop
- **Runtime:** .NET 9 (`net9.0-windows10.0.26100.0`)
- **Architecture:** MVVM Pattern using `CommunityToolkit.Mvvm`
- **Dependency Injection:** `Microsoft.Extensions.Hosting` & `Microsoft.Extensions.DependencyInjection`
- **System Tray:** `H.NotifyIcon.WinUI`
- **Calendar Parsing:** RFC 5545 iCalendar (`.ics`) lightweight parser with cancellation filtering & deduplication
- **Cloud Storage:** Google Apps Script Web App endpoint (`DriveApp` API)
- **Installer:** Inno Setup 6 Wizard
- **Unit Testing:** `xUnit` & `Moq` test suite (22 unit tests)

### 4. Key Learnings
- Building native unpackaged WinUI 3 desktop applications with custom multi-resolution assets and Mica backdrops.
- Designing an RFC 5545 iCalendar parsing engine supporting line unfolding, timezone normalization, and video conference link extraction.
- Building a lightweight cloud synchronization bridge using Google Apps Script Web Apps with streaming SHA-256 hash indexing.
- Implementing non-polling, lifecycle-based background task orchestration in C# / .NET 9.
- Implementing standalone single-file Windows installers using Inno Setup 6.

### 5. Local Setup Instructions

#### Prerequisites
- Windows 10 (version 1809+) or Windows 11
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

#### Building and Running
```powershell
# Clone the repository
git clone https://github.com/your-username/work-activity-panel.git
cd work-activity-panel

# Run unit tests
dotnet test WorkActivityPanel.Tests\WorkActivityPanel.Tests.csproj

# Build and run the project
dotnet run --project WorkActivityPanel.csproj
```

---

<a name="español"></a>
## Español

### 1. Descripción del Proyecto
**Work Activity Panel** es una aplicación de escritorio nativa para Windows 11 diseñada para optimizar tu jornada laboral. Gestiona automáticamente tus herramientas de trabajo según tu horario configurado, se sincroniza con **Google Calendar** mediante un enlace iCal privado, automatiza la preparación de reuniones abriendo **Granola** 5 minutos antes de cada reunión agendada, asegura que **Slack** esté abierto al inicio de tu jornada, y respalda tus archivos de trabajo en **Google Drive** de forma automática y ligera mediante un puente de Google Apps Script.

### 2. Funcionalidades Principales
- ⏰ **Control de Horario y Modo Vacaciones:** Configuración flexible de días y horas de trabajo. Pausa todas las automatizaciones cuando el Modo Vacaciones está activo.
- 💬 **Gestión de Slack:** Verifica y abre Slack automáticamente al inicio del horario laboral.
- 🥑 **Automatización de Granola:** Detecta reuniones de Google Calendar y abre Granola 5 minutos antes de que comiencen.
- 📅 **Sincronización con Google Calendar:** Lista en tiempo real de las reuniones del día con botón directo para unirse (Google Meet, Zoom, Microsoft Teams, Webex).
- ☁️ **Copia de Seguridad en Google Drive:**
  - Endpoint ligero mediante Google Apps Script Web App (sin proyectos complejos de GCP ni clientes pesados de terceros).
  - Detección incremental de cambios por hash SHA-256 (solo sube archivos nuevos o modificados).
  - Filtrado multi-criterio (extensiones permitidas/ignoradas, exclusión de carpetas del sistema, límite de MB).
  - Respaldo automático opcional al finalizar la jornada laboral (`AutoSyncOnWorkEnd`).
  - Guía paso a paso: [Guía de Configuración de Google Drive](docs/google-setup-guide.md).
- 💻 **Diseño Fluent y Bandeja del Sistema:** Efecto Mica nativo de Windows 11, soporte para temas claro/oscuro y minimizado a la bandeja del sistema (System Tray).
- ⚡ **Eficiencia Basada en Eventos:** Cero sobrecarga de sondeo (polling continuo); utiliza temporizadores programados precisos.

### 3. Tecnologías Utilizadas
- **Framework de UI:** WinUI 3 (Windows App SDK 2.4) con Fluent Design y Mica
- **Plataforma / Runtime:** .NET 9 (`net9.0-windows10.0.26100.0`)
- **Arquitectura:** Patrón MVVM con `CommunityToolkit.Mvvm`
- **Inyección de Dependencias:** `Microsoft.Extensions.Hosting` y `Microsoft.Extensions.DependencyInjection`
- **Bandeja del Sistema (Tray):** `H.NotifyIcon.WinUI`
- **Motor iCal:** Parser ligero RFC 5545 (`.ics`) con filtrado de reuniones canceladas y deduplicación
- **Almacenamiento Cloud:** Google Apps Script Web App (`DriveApp` API)
- **Instalador:** Asistente Inno Setup 6
- **Pruebas Unitarias:** Suite `xUnit` y `Moq` (22 pruebas unitarias)

### 4. Aprendizajes Clave
- Desarrollo de aplicaciones de escritorio nativas WinUI 3 con assets multirresolución e integración de Mica.
- Creación de un motor de parsing RFC 5545 para iCalendar con soporte para despliegue de líneas, normalización de zonas horarias y extracción de enlaces de videollamadas.
- Diseño de una arquitectura puente de sincronización con Google Apps Script y persistencia liviana de hashes SHA-256.
- Diseño de arquitecturas eficientes basadas en eventos y temporizadores en C# / .NET 9.
- Creación de asistentes de instalación para Windows con Inno Setup 6.

### 5. Instrucciones de Instalación y Ejecución

#### Requisitos Previos
- Windows 10 (versión 1809 o superior) o Windows 11
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

#### Compilación y Ejecución
```powershell
# Clonar el repositorio
git clone https://github.com/tu-usuario/work-activity-panel.git
cd work-activity-panel

# Ejecutar pruebas unitarias
dotnet test WorkActivityPanel.Tests\WorkActivityPanel.Tests.csproj

# Compilar e iniciar la aplicación
dotnet run --project WorkActivityPanel.csproj
```
