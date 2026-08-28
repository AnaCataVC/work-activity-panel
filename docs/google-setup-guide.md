# Google Drive Setup Guide: Google Apps Script Web App Bridge ☁️

*Read this in [English](#english) | Léelo en [Español](#español)*

---

<a name="english"></a>
## English

This guide provides step-by-step instructions for configuring your Google Drive destination, enabling **Work Activity Panel** to back up and synchronize your work files automatically without requiring complex Google Cloud Platform (GCP) configurations or heavy third-party sync clients.

---

### 📋 Synchronization Architecture

```
[Work Activity Panel (Windows 11)] 
       │
       │ HTTP POST (Base64 Payload + Relative Path)
       ▼
[Google Apps Script Web App] 
       │
       │ DriveApp API (Executes with Google Account credentials)
       ▼
[Destination Folder in Google Drive]
  ├── Subfolder 1/
  │    └── file1.pdf (overwritten if modified)
  ├── Subfolder 2/
  │    └── data.xlsx
  └── claude-md-unversioned/
       └── Repos/
            └── my-project/
                 ├── CLAUDE.md
                 └── .claude/
                      └── references/
                           └── architecture.md
```

---

### 🤖 AI Agent Context & Instruction Sync (CLAUDE.md & References)

When working across multiple software projects, developers frequently maintain local AI agent steering instructions (`CLAUDE.md`), architecture notes, and reference files (`.claude/references/*.md`, `references/*.md`). Often, these files are kept local or gitignored to avoid repository bloat, leaving them vulnerable to machine loss.

**Work Activity Panel** provides automated, secure discovery and backup for these files:
- **Extended Discovery Scope:** Recursively scans the user profile up to a configurable depth (`ClaudeMarkdownScanDepth`, default 6) to detect `CLAUDE.md`, `.claude/**/*.md` (up to 3 levels deep), and `references/*.md`.
- **Git Tracking Verification:** Candidate files are evaluated against Git. Only untracked or unversioned files are backed up—files already committed to Git are skipped since their history is safely preserved in their respective repositories. To eliminate process latency, Git tracking checks are batched in chunks of 50 files per query.
- **Multi-Layer Secret Filtering:**
  - Automatically skips files matching sensitive name patterns (`id_rsa`, `id_ed25519`, `credentials`, `auth_token`, `api_key`).
  - Pre-scans the initial 64 KB of file contents using regex signatures to prevent uploading private SSH keys, AWS access keys, GitHub PATs, and Slack tokens.
- **Folder Tree Preservation:** Files are uploaded to Google Drive under a dedicated destination prefix (`claude-md-unversioned`), faithfully recreating their relative path under the user profile so files with identical names never overwrite one another.

---

### 🚀 Step-by-Step Setup

#### Step 1: Create or Locate Destination Folder ID
1. Open [Google Drive](https://drive.google.com/) with your target Google account.
2. Create a new folder (e.g., `Work_Backup`) or open an existing one.
3. Open the folder and check the URL in your browser's address bar:
   ```
   https://drive.google.com/drive/folders/1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456
   ```
4. Copy the **Folder ID** (the alphanumeric string following `/folders/`):
   - Example: `1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456`

---

#### Step 2: Create the Google Apps Script
1. Navigate to [Google Apps Script](https://script.google.com/) in your browser.
2. Click **+ New project** in the upper left corner.
3. Name your project (e.g., `Work Activity Panel Drive Bridge`).
4. Replace the default placeholder code in `Code.gs` with the following script:

```javascript
function doPost(e) {
  // 1. Concurrency lock to prevent simultaneous duplicate file creation
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(25000);
  } catch (t) {
    return ContentService.createTextOutput(JSON.stringify({
      status: "error",
      message: "Server is busy. Please try again shortly."
    })).setMimeType(ContentService.MimeType.JSON);
  }

  try {
    // 2. Optional: Set a shared secret token to protect your endpoint (leave empty if not needed)
    var AUTH_TOKEN = ""; // e.g. "my-super-secret-token"
    if (AUTH_TOKEN && e.parameter.authToken !== AUTH_TOKEN) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "Unauthorized: Invalid or missing authentication token."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    // 3. Paste your Google Drive Folder ID here
    var rootFolderId = "PASTE_YOUR_FOLDER_ID_HERE"; 
    var currentFolder = DriveApp.getFolderById(rootFolderId);

    var fileName = e.parameter.filename;
    var relativePath = e.parameter.relativePath || fileName;
    var mimeType = e.parameter.mimeType || "application/octet-stream";

    // 4. Recreate subfolder hierarchy in Google Drive
    var pathParts = relativePath.split("/");
    if (pathParts.length > 1) {
      for (var i = 0; i < pathParts.length - 1; i++) {
        var subfolderName = pathParts[i].trim();
        if (subfolderName.length === 0) continue;

        var matchingFolders = currentFolder.getFoldersByName(subfolderName);
        if (matchingFolders.hasNext()) {
          currentFolder = matchingFolders.next();
        } else {
          currentFolder = currentFolder.createFolder(subfolderName);
        }
      }
    }

    // 4. Clean overwrite under lock: Trash previous versions of the same file in this folder
    var existingFiles = currentFolder.getFilesByName(fileName);
    while (existingFiles.hasNext()) {
      var oldFile = existingFiles.next();
      oldFile.setTrashed(true);
    }

    // 5. Decode Base64 and save the new file
    var data = Utilities.base64Decode(e.parameter.data);
    var blob = Utilities.newBlob(data, mimeType, fileName);
    var file = currentFolder.createFile(blob);

    return ContentService.createTextOutput(JSON.stringify({
      status: "success",
      fileId: file.getId(),
      url: file.getUrl()
    })).setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    return ContentService.createTextOutput(JSON.stringify({
      status: "error",
      message: err.toString()
    })).setMimeType(ContentService.MimeType.JSON);
  } finally {
    lock.releaseLock();
  }
}
```

5. Replace `"PASTE_YOUR_FOLDER_ID_HERE"` on line 4 with the Folder ID copied in **Step 1**.
6. Save the project (`Ctrl+S` or click the save icon).

---

#### Step 3: Deploy as a Web App
1. Click the blue **Deploy** button $\rightarrow$ **New deployment**.
2. Click the gear icon ⚙️ (**Select type**) $\rightarrow$ **Web app**.
3. Configure the fields as follows:
   - **Description:** `Work Activity Panel Endpoint`
   - **Execute as:** `Me (your-email@example.com)` *(Executes with your Drive permissions)*
   - **Who has access:** `Anyone`
4. Click **Deploy**.
5. When prompted to authorize permissions:
   - Click **Authorize access** and select your Google account.
   - Click **Advanced** $\rightarrow$ **Go to Work Activity Panel Drive Bridge (unsafe)** to grant file creation access.
6. Copy the generated **Web App URL** (ends with `/exec`):
   - Example: `https://script.google.com/macros/s/AKfycb.../exec`

---

#### Step 4: Configure Work Activity Panel
1. Open **Work Activity Panel** and go to **Settings** ⚙️.
2. In the **Google Drive Backup** section:
   - Paste the **Web App URL** copied from Step 3.
   - Click **Test Connection** to verify response.
   - Select your local working directory to back up.
   - Save your configuration.
3. You can trigger synchronization anytime from the dashboard or enable automatic end-of-workday syncing.

---

<a name="español"></a>
## Español

Esta guía proporciona instrucciones paso a paso para configurar tu destino en Google Drive y permitir que **Work Activity Panel** respalde tus archivos de trabajo de forma ligera y automática, sin necesidad de crear proyectos complejos en Google Cloud Platform (GCP) ni instalar clientes pesados de terceros.

---

### 📋 Arquitectura de la Sincronización

```
[Work Activity Panel (Windows 11)] 
       │
       │ HTTP POST (Carga útil en Base64 + Ruta Relativa)
       ▼
[Google Apps Script Web App] 
       │
       │ API DriveApp (Se ejecuta con los permisos de tu cuenta de Google)
       ▼
[Carpeta Destino en tu Google Drive]
  ├── Subcarpeta 1/
  │    └── archivo1.pdf (sobrescrito si está modificado)
  ├── Subcarpeta 2/
  │    └── datos.xlsx
  └── claude-md-unversioned/
       └── Repos/
            └── mi-proyecto/
                 ├── CLAUDE.md
                 └── .claude/
                      └── references/
                           └── arquitectura.md
```

---

### 🤖 Sincronización de Contexto e Instrucciones de IA (CLAUDE.md y Referencias)

Al desarrollar en múltiples proyectos, es común mantener archivos locales de instrucciones para agentes de IA (`CLAUDE.md`), notas de arquitectura y referencias (`.claude/references/*.md`, `references/*.md`). Con frecuencia, estos archivos permanecen ignorados en Git o sin versionar para no ensuciar el repositorio, quedando expuestos a pérdidas ante cualquier fallo del equipo.

**Work Activity Panel** incluye un motor de descubrimiento y respaldo automatizado y seguro para estos archivos:
- **Descubrimiento Extendido:** Explora el perfil de usuario hasta la profundidad configurada (`ClaudeMarkdownScanDepth`, por defecto 6) detectando `CLAUDE.md`, `.claude/**/*.md` (hasta 3 niveles) y `references/*.md`.
- **Verificación Git por Lotes:** Compara los candidatos contra Git para respaldar únicamente archivos sin versionar o no rastreados (los archivos ya rastreados en Git se omiten puesto que su historial vive en el repositorio). Las consultas a Git se realizan en bloques masivos de 50 archivos para anular el retardo de procesos.
- **Filtrado de Seguridad Multi-Capa:**
  - Omite automáticamente archivos con nombres sensibles (`id_rsa`, `id_ed25519`, `credentials`, `auth_token`, `api_key`).
  - Pre-escanea los primeros 64 KB de cada archivo con expresiones regulares para impedir la subida de claves privadas SSH, claves de AWS, PATs de GitHub o tokens de Slack.
- **Preservación del Árbol de Carpetas:** Los archivos se cargan bajo un prefijo dedicado (`claude-md-unversioned`), recreando su ruta relativa en el perfil para evitar colisiones entre archivos homónimos de distintos proyectos.

---

### 🚀 Configuración Paso a Paso

#### Paso 1: Crear u Obtener el ID de tu Carpeta Destino
1. Abre [Google Drive](https://drive.google.com/) con tu cuenta de Google deseada.
2. Crea una nueva carpeta (por ejemplo, `Respaldo_Trabajo`) o abre una existente.
3. Abre la carpeta y observa la URL en la barra de direcciones de tu navegador:
   ```
   https://drive.google.com/drive/folders/1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456
   ```
4. Copia el **ID de la carpeta** (la cadena alfanumérica después de `/folders/`):
   - Ejemplo: `1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456`

---

#### Paso 2: Crear el Google Apps Script
1. Entra a [Google Apps Script](https://script.google.com/) en tu navegador.
2. Haz clic en **+ Nuevo proyecto** en la esquina superior izquierda.
3. Nombra tu proyecto (ejemplo: `Work Activity Panel Drive Bridge`).
4. Borra todo el código que aparece por defecto en el editor `Código.gs` y pega el siguiente script:

```javascript
function doPost(e) {
  // 1. Bloqueo de concurrencia para evitar subidas duplicadas simultáneas
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(25000);
  } catch (t) {
    return ContentService.createTextOutput(JSON.stringify({
      status: "error",
      message: "Servidor ocupado. Intenta de nuevo en unos momentos."
    })).setMimeType(ContentService.MimeType.JSON);
  }

  try {
    // 2. Opcional: Define un token secreto compartido para proteger tu Web App (déjalo vacío si no lo requieres)
    var AUTH_TOKEN = ""; // ej: "mi-token-super-secreto"
    if (AUTH_TOKEN && e.parameter.authToken !== AUTH_TOKEN) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "No autorizado: Token de autenticación inválido o ausente."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    // 3. Pega aquí el ID de tu carpeta destino de Google Drive
    var rootFolderId = "PEGA_AQUI_EL_ID_DE_TU_CARPETA"; 
    var currentFolder = DriveApp.getFolderById(rootFolderId);

    var fileName = e.parameter.filename;
    var relativePath = e.parameter.relativePath || fileName;
    var mimeType = e.parameter.mimeType || "application/octet-stream";

    // 4. Recrear la jerarquía de subcarpetas en Google Drive
    var pathParts = relativePath.split("/");
    if (pathParts.length > 1) {
      for (var i = 0; i < pathParts.length - 1; i++) {
        var subfolderName = pathParts[i].trim();
        if (subfolderName.length === 0) continue;

        var matchingFolders = currentFolder.getFoldersByName(subfolderName);
        if (matchingFolders.hasNext()) {
          currentFolder = matchingFolders.next();
        } else {
          currentFolder = currentFolder.createFolder(subfolderName);
        }
      }
    }

    // 4. Sobrescritura limpia garantizada bajo lock: papelera a versiones anteriores
    var existingFiles = currentFolder.getFilesByName(fileName);
    while (existingFiles.hasNext()) {
      var oldFile = existingFiles.next();
      oldFile.setTrashed(true);
    }

    // 5. Decodificar Base64 y guardar el nuevo archivo
    var data = Utilities.base64Decode(e.parameter.data);
    var blob = Utilities.newBlob(data, mimeType, fileName);
    var file = currentFolder.createFile(blob);

    return ContentService.createTextOutput(JSON.stringify({
      status: "success",
      fileId: file.getId(),
      url: file.getUrl()
    })).setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    return ContentService.createTextOutput(JSON.stringify({
      status: "error",
      message: err.toString()
    })).setMimeType(ContentService.MimeType.JSON);
  } finally {
    lock.releaseLock();
  }
}
```

5. Reemplaza `"PEGA_AQUI_EL_ID_DE_TU_CARPETA"` en la línea 4 con el ID copiado en el **Paso 1**.
6. Haz clic en el icono del disco para **Guardar** (o pulsa `Ctrl+S`).

---

#### Paso 3: Implementar como Aplicación Web (Web App)
1. Haz clic en el botón azul **Implementar** (Deploy) en la esquina superior derecha.
2. Selecciona **Nueva implementación** (New deployment).
3. Haz clic en el icono del engranaje ⚙️ (**Seleccionar tipo**) y elige **Aplicación web** (Web app).
4. Configura los siguientes campos:
   - **Descripción:** `Work Activity Panel Endpoint`
   - **Ejecutar como:** `Yo (tu-email@example.com)` *(Se ejecuta con los permisos de tu Drive)*
   - **Quién tiene acceso:** `Cualquier usuario` (Anyone)
5. Haz clic en **Implementar**.
6. Si Google te pide autorizar permisos:
   - Haz clic en **Autorizar acceso**.
   - Elige tu cuenta de Google.
   - Haz clic en **Configuración avanzada** (Advanced) y luego en **Ir a Work Activity Panel Drive Bridge (no seguro)** para autorizar la creación de archivos.
7. Copia la **URL de la aplicación web** generada (termina en `/exec`):
   - Ejemplo: `https://script.google.com/macros/s/AKfycb.../exec`

---

#### Paso 4: Configurar en Work Activity Panel
1. Abre **Work Activity Panel** y ve a **Ajustes** ⚙️.
2. En la sección **Copia de Seguridad en Google Drive**:
   - Pega la **URL de la Web App** copiada en el paso anterior.
   - Haz clic en **Probar Conexión** para verificar que responde correctamente.
   - Agrega en **Carpetas a sincronizar** cada carpeta local que deseas respaldar e indica el nombre de la subcarpeta que tendrá en Drive. Todas quedan al mismo nivel dentro de la carpeta del respaldo.
   - Opcionalmente pega la **URL de la carpeta de Google Drive** para que el botón **Abrir Drive** del panel principal la abra directamente.
   - Guarda los cambios.
3. ¡Listo! Puedes sincronizar en cualquier momento desde el panel principal o dejar que se sincronice automáticamente al terminar tu jornada laboral.

---

### 🔄 Actualización del Script en el Futuro
Si modificas el código en `Código.gs`:
1. Haz clic en **Implementar** > **Gestionar implementaciones**.
2. Haz clic en el icono del lápiz ✏️ para editar tu implementación activa.
3. En el desplegable **Versión**, selecciona **Nueva versión**.
4. Haz clic en **Implementar**.
