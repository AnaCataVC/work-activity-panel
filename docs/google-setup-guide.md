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
  └── Subfolder 2/
       └── data.xlsx
```

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
  try {
    // 1. Paste your Google Drive Folder ID here
    var rootFolderId = "PASTE_YOUR_FOLDER_ID_HERE"; 
    var currentFolder = DriveApp.getFolderById(rootFolderId);

    var fileName = e.parameter.filename;
    var relativePath = e.parameter.relativePath || fileName;
    var mimeType = e.parameter.mimeType || "application/octet-stream";

    // 2. Recreate subfolder hierarchy in Google Drive
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

    // 3. Clean overwrite: Trash previous versions of the same file in this folder
    var existingFiles = currentFolder.getFilesByName(fileName);
    while (existingFiles.hasNext()) {
      var oldFile = existingFiles.next();
      oldFile.setTrashed(true);
    }

    // 4. Decode Base64 and save the new file
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
  └── Subcarpeta 2/
       └── datos.xlsx
```

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
  try {
    // 1. Pega aquí el ID de tu carpeta destino de Google Drive
    var rootFolderId = "PEGA_AQUI_EL_ID_DE_TU_CARPETA"; 
    var currentFolder = DriveApp.getFolderById(rootFolderId);

    var fileName = e.parameter.filename;
    var relativePath = e.parameter.relativePath || fileName;
    var mimeType = e.parameter.mimeType || "application/octet-stream";

    // 2. Recrear la jerarquía de subcarpetas en Google Drive
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

    // 3. Sobrescritura limpia: Elimina versiones anteriores del mismo archivo
    var existingFiles = currentFolder.getFilesByName(fileName);
    while (existingFiles.hasNext()) {
      var oldFile = existingFiles.next();
      oldFile.setTrashed(true);
    }

    // 4. Decodificar Base64 y guardar el nuevo archivo
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
   - Selecciona la carpeta local que deseas respaldar.
   - Guarda los cambios.
3. ¡Listo! Puedes sincronizar en cualquier momento desde el panel principal o dejar que se sincronice automáticamente al terminar tu jornada laboral.

---

### 🔄 Actualización del Script en el Futuro
Si modificas el código en `Código.gs`:
1. Haz clic en **Implementar** > **Gestionar implementaciones**.
2. Haz clic en el icono del lápiz ✏️ para editar tu implementación activa.
3. En el desplegable **Versión**, selecciona **Nueva versión**.
4. Haz clic en **Implementar**.
