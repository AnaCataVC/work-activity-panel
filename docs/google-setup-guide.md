# Guía de Configuración de Google Drive: Integración con Google Apps Script Web App

Esta guía proporciona instrucciones paso a paso para configurar tu destino en Google Drive y permitir que **Work Activity Panel** respalde tus archivos de trabajo de forma ligera y automática, sin necesidad de crear proyectos complejos en Google Cloud Platform (GCP) ni instalar clientes pesados de terceros.

---

## 📋 Arquitectura de la Sincronización

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

## 🚀 Configuración Paso a Paso

### Paso 1: Crear u Obtener el ID de tu Carpeta Destino
1. Abre [Google Drive](https://drive.google.com/) con tu cuenta de Google deseada.
2. Crea una nueva carpeta (por ejemplo, `Respaldo_Trabajo`) o abre una existente.
3. Abre la carpeta y observa la URL en la barra de direcciones de tu navegador:
   ```
   https://drive.google.com/drive/folders/1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456
   ```
4. Copia el **ID de la carpeta** (la cadena alfanumérica después de `/folders/`):
   - Ejemplo: `1aBcDeFgHiJkLmNoPqRsTuVwXyZ123456`

---

### Paso 2: Crear el Google Apps Script
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

### Paso 3: Implementar como Aplicación Web (Web App)
1. Haz clic en el botón azul **Implementar** (Deploy) en la esquina superior derecha.
2. Selecciona **Nueva implementación** (New deployment).
3. Haz clic en el icono del engranaje ⚙️ (**Seleccionar tipo**) y elige **Aplicación web** (Web app).
4. Configura los siguientes campos:
   - **Descripción:** `Work Activity Panel Endpoint`
   - **Ejecutar como:** `Yo (tu-email@gmail.com)` *(Se ejecuta con los permisos de tu Drive)*
   - **Quién tiene acceso:** `Cualquier usuario` (Anyone)
5. Haz clic en **Implementar**.
6. Si Google te pide autorizar permisos:
   - Haz clic en **Autorizar acceso**.
   - Elige tu cuenta de Google.
   - Haz clic en **Configuración avanzada** (Advanced) y luego en **Ir a Work Activity Panel Drive Bridge (no seguro)** para autorizar la creación de archivos.
7. Copia la **URL de la aplicación web** generada (termina en `/exec`):
   - Ejemplo: `https://script.google.com/macros/s/AKfycb.../exec`

---

### Paso 4: Configurar en Work Activity Panel
1. Abre **Work Activity Panel** y ve a **Ajustes** ⚙️.
2. En la sección **Copia de Seguridad en Google Drive**:
   - Pega la **URL de la Web App** copiada en el paso anterior.
   - Haz clic en **Probar Conexión** para verificar que responde correctamente.
   - Selecciona la carpeta local que deseas respaldar.
   - Guarda los cambios.
3. ¡Listo! Puedes sincronizar en cualquier momento desde el panel principal o dejar que se sincronice automáticamente al terminar tu jornada laboral.

---

## 🔄 Actualización del Script en el Futuro
Si modificas el código en `Código.gs`:
1. Haz clic en **Implementar** > **Gestionar implementaciones**.
2. Haz clic en el icono del lápiz ✏️ para editar tu implementación activa.
3. En el desplegable **Versión**, selecciona **Nueva versión**.
4. Haz clic en **Implementar**.
