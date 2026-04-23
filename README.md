# AlbionPrices Overlay

Overlay de Precios en Tiempo Real para Albion Online.

Muestra los mejores precios de compra y venta por ciudad directamente sobre el juego.

![Captura de pantalla](screenshot.png)

## Características

- **Overlay flotante** - Ventana transparente siempre visible sobre el juego
- **Acceso rápido** - Presiona `Ctrl+D` para mostrar/ocultar
- **Precios en tiempo real** - Datos directamente de la API oficial de Albion
- **Selección de Tier/Encantamiento** - Interface para items levelables
- **Base de datos offline** - Búsqueda instantanea por nombre
- **Sistema de notificaciones** - Icono en la bandeja del sistema
- **Auto-ocultado** - Se oculta automáticamente al perder foco

## Requisitos del Sistema

- Windows 10 (Build 17763) o superior
- .NET 10.0 Runtime (incluído en el build self-contained)
- Conexión a Internet para API de precios

## Instalación

### Opción 1: Instalador
Descarga `AlbionPrices-Setup-{version}.exe` y ejecuta.

### Opción 2: Portátil
Extrae el ZIP en cualquier carpeta y ejecuta `AlbionPrices.exe`.

## Uso

### Atajos de Teclado

| Atajo | Acción |
|------|--------|
| `Ctrl+D` | Mostrar/Ocultar overlay |
| `Enter` | Buscar item (cuando el input tiene foco) |
| Click en título | Arrastrar ventana |
| Click en `_` | Minimizar a bandeja |

### Búsqueda de Items

1. Escribe el nombre del item en el campo de texto
2. Presiona `Enter` o Klik en `Check`
3. Visualiza los precios por ciudad

**Items con Tier/Enchant**: Usa los botones T1-T8 y .0-.3 para seleccionar nivel y encantamiento.

### Modo Ventana

La ventana se posiciona en el centro de la pantalla al mostrarse y se oculta automáticamente cuando pierdes el foco (desactivación).

## Especificaciones Técnicas

### Arquitectura

```
AlbionPrices/
├── App.xaml[.cs]          # Punto de entrada, inicialización
├── MainWindow.xaml[.cs]   # UI principal y lógica
├── Services/
│   ├── AlbionApiService.cs  # cliente HTTP para API de precios
│   ├── ItemDatabase.cs   # DB local de items
│   └── UpdateService.cs # sistema de updates
├── Helpers/
│   ├── GlobalHotkey.cs  #Registro de hotkey global (Ctrl+D)
│   ├── IconHelper.cs    # Generación de ícono
│   └── ScreenCapture.cs # Captura de screen (OCR)
└── Models/
    └── PriceModels.cs   #Modelos de datos
```

### Stack Tecnológico

- **Framework**: WPF (.NET 10)
- **UI**: XAML con estilo custom (ventana sin borders)
- **API**: Albion Online Data API v2
- **OCR**: Tesseract v5.2.0 (para futuras features)
- **Instalador**: Inno Setup

### API de Precios

Endpoint usado:
```
GET https://west.albion-online-data.com/api/v2/stats/prices/{itemId}.json
```

Respuesta procesada para obtener:
- **Buy Low**: Ciudad con precio mínimo de venta
- **Sell High**: Ciudad con precio máximo de compra
- Lista completa de precios por ciudad

### Formato de Item ID

- Simple: `ITEM_NAME` (e.g., `T4_BAG`)
- Tiered: `T{tier}_{base_id}` (e.g., `T4_BAG@2`)
- Variations cargados desde DB local

### Sistema de Updates

1. Verifica versión en GitHub releases en startup
2. Muestra banner cuando nueva versión disponible
3. Descarga y reinstala automáticamente

## Construcción del Release

### Requisitos

- .NET 10 SDK
- Windows 10 SDK (para compilación win-x64)
- Inno Setup 6+ (opcional, para installer)

### Pasos de Build

```powershell
# Opción 1: Script automático
.\build.ps1 -GitHubOwner "GitHubUser"

# Pasos del script:
# 1. Lee versión desde AlbionPrices.csproj
# 2. dotnet build --configuration Release
# 3. dotnet publish (carpeta publish/)
# 4. Copia tessdata/
# 5. Crea ZIP en Releases/
# 6. Actualiza setup.iss con versión
```

### Comandos Manual

```powershell
# Build
dotnet build --configuration Release -c Release

# Publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o ./publish

# Crear ZIP
Compress-Archive -Path './publish/*' -DestinationPath './Releases/AlbionPrices-{version}.zip'
```

### Generación de Release en GitHub

1. **Ejecutar build.bat** o build manual
2. **Crear Tag**:
   ```bash
   git tag v{version}
   git push origin v{version}
   ```
3. **Crear Release** en GitHub:
   - Tag: `v{version}`
   - Title: `Release v{version}`
   - Attach: `Releases/AlbionPrices-{version}.zip`
4. **Compilar Installer** (opcional):
   - Abrir `setup.iss` con Inno Setup
   - Compile
   - Adjuntar installer al release

###Archivos Generados

| Archivo | Descripción |
|---------|-------------|
| `Releases/AlbionPrices-{version}.zip` | Build portable |
| `Installer/AlbionPrices-Setup-{version}.exe` | Instalador |
| `setup.iss` | Script de Inno Setup (actualizado) |

## Configuración Avanzada

### Cambiar Hotkey

Modifica `GlobalHotkey.cs`:
```csharp
private const int VK_D = 0x44;  // 'D' key
private const int MOD_CONTROL = 0x0002;  // Ctrl
```

Valores válidos:
- `MOD_ALT`: 0x0001
- `MOD_CONTROL`: 0x0002
- `MOD_SHIFT`: 0x0004
- `MOD_WIN`: 0x0008

### Puerto del Sistema de Updates

Por defecto apunta a GitHub releases del repositorio actual. Configurable en `UpdateService.cs`.

## Troubleshooting

### La ventana no aparece
- Verifica que el hotkey se registró correctamente (busca "Hotkey registered" en debug output)

### Sin datos de precios
- Verifica conexión a Internet
- El item puede no existir en la API

### OCR no funciona
- Requiere `tessdata/eng.traineddata` en la carpeta de ejecución

### Error de compilación
- Asegúrate de tener .NET 10 SDK instalado
- Windows 10 SDK requerido para target win-x64

## Licencia

MIT License - Uso personal y comercial permitido.

## Acknowledgments

- [Albion Online Data API](https://github.com/biud436/API) - Datos de precios
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) - OCR engine
- [Inno Setup](https://jrsoftware.org/isinfo.php) - Instalador