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

    // 3. Paste your Google Drive Folder ID here
    var rootFolderId = "PASTE_YOUR_FOLDER_ID_HERE";

    // 4. The request body is JSON: { authToken, files: [{ filename, relativePath, mimeType, data }, ...] }.
    //    Sending several files per call (a batch) amortizes the fixed per-request cost of Apps
    //    Script (cold start + this lock acquisition) across all of them instead of paying it once
    //    per file, which is what keeps sync fast once many files change at once. Every call is a
    //    batch, even a single-file one (an array with one entry), so there is only one wire format.
    var body = JSON.parse(e.postData.contents);

    if (AUTH_TOKEN && body.authToken !== AUTH_TOKEN) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "Unauthorized: Invalid or missing authentication token."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    var files = body.files || [];
    var folderCache = PropertiesService.getScriptProperties();
    var results = [];

    for (var f = 0; f < files.length; f++) {
      var fileEntry = files[f];
      var relativePath = fileEntry.relativePath || fileEntry.filename;

      // Per-file try/catch: one bad file (locked, quota, odd name) fails only its own entry
      // in the results array instead of aborting the rest of the batch.
      try {
        var currentFolder = DriveApp.getFolderById(rootFolderId);
        var fileName = fileEntry.filename;
        var mimeType = fileEntry.mimeType || "application/octet-stream";

        // 5. Recreate subfolder hierarchy in Google Drive. Resolved folder IDs are cached in
        //    PropertiesService so a repeat upload into an already-seen folder skips the
        //    getFoldersByName search (an O(children) Drive query) and goes straight to a
        //    direct getFolderById lookup instead. Without this, every single file uploaded
        //    into a deeply nested path re-walks and re-searches the whole chain from the
        //    root on every sync, which used to be the main reason uploads stayed slow even
        //    once most of the tree was already mirrored in Drive.
        var pathParts = relativePath.split("/");
        if (pathParts.length > 1) {
          var cacheKeyParts = [];
          for (var i = 0; i < pathParts.length - 1; i++) {
            var subfolderName = pathParts[i].trim();
            if (subfolderName.length === 0) continue;
            cacheKeyParts.push(subfolderName);

            var cacheKey = "folderId:" + cacheKeyParts.join("/");
            var cachedId = folderCache.getProperty(cacheKey);
            var resolvedFolder = null;

            if (cachedId) {
              try {
                resolvedFolder = DriveApp.getFolderById(cachedId);
                if (resolvedFolder.isTrashed()) resolvedFolder = null; // getFolderById does not throw on trashed folders
              } catch (staleIdErr) {
                // Cached folder was deleted/moved out from under us; fall through and re-resolve.
                resolvedFolder = null;
              }
            }

            if (!resolvedFolder) {
              var matchingFolders = currentFolder.getFoldersByName(subfolderName);
              resolvedFolder = matchingFolders.hasNext() ? matchingFolders.next() : currentFolder.createFolder(subfolderName);
              folderCache.setProperty(cacheKey, resolvedFolder.getId());
            }

            currentFolder = resolvedFolder;
          }
        }

        // 6. Clean overwrite under lock: Trash previous versions of the same file in this folder
        var existingFiles = currentFolder.getFilesByName(fileName);
        while (existingFiles.hasNext()) {
          existingFiles.next().setTrashed(true);
        }

        // 7. Decode Base64 and save the new file
        var data = Utilities.base64Decode(fileEntry.data);
        var blob = Utilities.newBlob(data, mimeType, fileName);
        var file = currentFolder.createFile(blob);

        results.push({ relativePath: relativePath, status: "success", fileId: file.getId(), url: file.getUrl() });
      } catch (fileErr) {
        results.push({ relativePath: relativePath, status: "error", message: fileErr.toString() });
      }
    }

    return ContentService.createTextOutput(JSON.stringify({
      status: "success",
      results: results
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

5. Replace `"PASTE_YOUR_FOLDER_ID_HERE"` with the Folder ID copied in **Step 1**.
6. Save the project (`Ctrl+S` or click the save icon).

> **Already have this deployed and syncing feels slow?** Update your `Code.gs` with the version above — it batches several files per request instead of one call per file, on top of the folder-ID cache — and follow [Updating the Script in the Future](#-updating-the-script-in-the-future) to redeploy. **This version changes the request format from form fields to a JSON body**, so the deployed script and the app version must be updated together; no re-upload of files already in Drive is triggered.

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
   - Under **Folders to sync**, add each local folder you want to back up and name the Drive subfolder it should land in. All of them sit side by side in Drive — add as many as you need.
   - Save your configuration.
3. Sync everything at once from the dashboard's **Sync Now** button, or sync just one folder from its own row in Settings (the ⟳ icon next to it) — handy for testing a single folder's config without waiting on the rest. You can also enable automatic end-of-workday syncing.

---

### 🔄 Updating the Script in the Future
If you modify the code in `Code.gs`:
1. Click **Deploy** > **Manage deployments**.
2. Click the pencil icon ✏️ to edit your active deployment.
3. In the **Version** dropdown, select **New version**.
4. Click **Deploy**.

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

    // 3. Pega aquí el ID de tu carpeta destino de Google Drive
    var rootFolderId = "PEGA_AQUI_EL_ID_DE_TU_CARPETA";

    // 4. El cuerpo de la petición es JSON: { authToken, files: [{ filename, relativePath, mimeType, data }, ...] }.
    //    Enviar varios archivos por llamada (un lote) reparte el costo fijo por petición de Apps
    //    Script (arranque en frío + esta adquisición del lock) entre todos ellos en vez de pagarlo
    //    una vez por archivo, que es lo que mantiene rápida la sincronización cuando cambian muchos
    //    archivos a la vez. Toda llamada es un lote, incluso una de un solo archivo (un arreglo con
    //    una sola entrada), así que hay un único formato de mensaje.
    var body = JSON.parse(e.postData.contents);

    if (AUTH_TOKEN && body.authToken !== AUTH_TOKEN) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "No autorizado: Token de autenticación inválido o ausente."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    var files = body.files || [];
    var folderCache = PropertiesService.getScriptProperties();
    var results = [];

    for (var f = 0; f < files.length; f++) {
      var fileEntry = files[f];
      var relativePath = fileEntry.relativePath || fileEntry.filename;

      // Try/catch por archivo: un archivo problemático (bloqueado, cuota, nombre raro) falla
      // solo su propia entrada en el arreglo de resultados en vez de abortar todo el lote.
      try {
        var currentFolder = DriveApp.getFolderById(rootFolderId);
        var fileName = fileEntry.filename;
        var mimeType = fileEntry.mimeType || "application/octet-stream";

        // 5. Recrear la jerarquía de subcarpetas en Google Drive. Los IDs de carpeta resueltos
        //    se cachean en PropertiesService para que una subida repetida a una carpeta ya vista
        //    se salte la búsqueda getFoldersByName (una consulta Drive con costo O(hijos)) y vaya
        //    directo a un getFolderById por ID. Sin esto, cada archivo subido a una ruta anidada
        //    recorre y vuelve a buscar toda la cadena desde la raíz en cada sincronización, que
        //    antes era la razón principal por la que las subidas seguían lentas aunque la mayor
        //    parte del árbol ya estuviera reflejada en Drive.
        var pathParts = relativePath.split("/");
        if (pathParts.length > 1) {
          var cacheKeyParts = [];
          for (var i = 0; i < pathParts.length - 1; i++) {
            var subfolderName = pathParts[i].trim();
            if (subfolderName.length === 0) continue;
            cacheKeyParts.push(subfolderName);

            var cacheKey = "folderId:" + cacheKeyParts.join("/");
            var cachedId = folderCache.getProperty(cacheKey);
            var resolvedFolder = null;

            if (cachedId) {
              try {
                resolvedFolder = DriveApp.getFolderById(cachedId);
                if (resolvedFolder.isTrashed()) resolvedFolder = null; // getFolderById no lanza error con carpetas en la papelera
              } catch (staleIdErr) {
                // La carpeta cacheada fue borrada o movida; sigue de largo y vuelve a resolverla.
                resolvedFolder = null;
              }
            }

            if (!resolvedFolder) {
              var matchingFolders = currentFolder.getFoldersByName(subfolderName);
              resolvedFolder = matchingFolders.hasNext() ? matchingFolders.next() : currentFolder.createFolder(subfolderName);
              folderCache.setProperty(cacheKey, resolvedFolder.getId());
            }

            currentFolder = resolvedFolder;
          }
        }

        // 6. Sobrescritura limpia garantizada bajo lock: papelera a versiones anteriores
        var existingFiles = currentFolder.getFilesByName(fileName);
        while (existingFiles.hasNext()) {
          existingFiles.next().setTrashed(true);
        }

        // 7. Decodificar Base64 y guardar el nuevo archivo
        var data = Utilities.base64Decode(fileEntry.data);
        var blob = Utilities.newBlob(data, mimeType, fileName);
        var file = currentFolder.createFile(blob);

        results.push({ relativePath: relativePath, status: "success", fileId: file.getId(), url: file.getUrl() });
      } catch (fileErr) {
        results.push({ relativePath: relativePath, status: "error", message: fileErr.toString() });
      }
    }

    return ContentService.createTextOutput(JSON.stringify({
      status: "success",
      results: results
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

5. Reemplaza `"PEGA_AQUI_EL_ID_DE_TU_CARPETA"` con el ID copiado en el **Paso 1**.
6. Haz clic en el icono del disco para **Guardar** (o pulsa `Ctrl+S`).

> **¿Ya tienes esto desplegado y la sincronización sigue lenta?** Actualiza tu `Código.gs` con la versión de arriba — agrupa varios archivos por petición en vez de una llamada por archivo, además de la caché de IDs de carpeta — y sigue [Actualización del Script en el Futuro](#-actualización-del-script-en-el-futuro) para volver a desplegar. **Esta versión cambia el formato de la petición, de campos de formulario a un cuerpo JSON**, así que el script desplegado y la versión de la app deben actualizarse juntos; no se dispara una nueva subida de los archivos que ya están en Drive.

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
   - Agrega en **Carpetas a sincronizar** cada carpeta local que deseas respaldar e indica el nombre de la subcarpeta que tendrá en Drive. Todas quedan al mismo nivel dentro de la carpeta del respaldo — agrega las que necesites.
   - Opcionalmente pega la **URL de la carpeta de Google Drive** para que el botón **Abrir Drive** del panel principal la abra directamente.
   - Guarda los cambios.
3. ¡Listo! Puedes sincronizar todo de una vez desde el botón **Sincronizar Ahora** del panel principal, o sincronizar solo una carpeta puntual desde su propia fila en Ajustes (ícono ⟳) — útil para probar la config de una sola carpeta sin esperar al resto. También puedes dejar que se sincronice automáticamente al terminar tu jornada laboral.

---

### 🔄 Actualización del Script en el Futuro
Si modificas el código en `Código.gs`:
1. Haz clic en **Implementar** > **Gestionar implementaciones**.
2. Haz clic en el icono del lápiz ✏️ para editar tu implementación activa.
3. En el desplegable **Versión**, selecciona **Nueva versión**.
4. Haz clic en **Implementar**.
